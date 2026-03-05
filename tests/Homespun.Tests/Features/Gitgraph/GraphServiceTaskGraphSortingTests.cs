using Fleece.Core.Models;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Gitgraph.Services;
using Homespun.Features.Projects;
using Homespun.Features.Testing;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Gitgraph;

/// <summary>
/// Tests for the GraphService's task graph sorting functionality.
/// Verifies that actionable issues are sorted by priority and then by age (oldest first).
/// </summary>
[TestFixture]
public class GraphServiceTaskGraphSortingTests
{
    private MockDataStore _dataStore = null!;
    private Mock<IProjectService> _mockProjectService = null!;
    private Mock<IGitHubService> _mockGitHubService = null!;
    private Mock<IFleeceService> _mockFleeceService = null!;
    private Mock<IClaudeSessionStore> _mockSessionStore = null!;
    private Mock<PullRequestWorkflowService> _mockWorkflowService = null!;
    private Mock<IGraphCacheService> _mockCacheService = null!;
    private Mock<ILogger<GraphService>> _mockLogger = null!;
    private GraphService _service = null!;
    private Project _testProject = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dataStore = new MockDataStore();

        // Create test project - use a temp path that exists with .fleece directory
        var testPath = Path.Combine(Path.GetTempPath(), $"graphservice-sorting-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(testPath);
        Directory.CreateDirectory(Path.Combine(testPath, ".fleece"));

        _testProject = new Project
        {
            Name = "test-repo",
            LocalPath = testPath,
            GitHubOwner = "test-owner",
            GitHubRepo = "test-repo",
            DefaultBranch = "main"
        };
        await _dataStore.AddProjectAsync(_testProject);

        // Set up mocks
        _mockProjectService = new Mock<IProjectService>();
        _mockProjectService.Setup(s => s.GetByIdAsync(_testProject.Id))
            .ReturnsAsync(_testProject);

        _mockGitHubService = new Mock<IGitHubService>();
        _mockFleeceService = new Mock<IFleeceService>();
        _mockSessionStore = new Mock<IClaudeSessionStore>();
        _mockSessionStore.Setup(s => s.GetByProjectId(_testProject.Id))
            .Returns(new List<ClaudeSession>());

        _mockWorkflowService = new Mock<PullRequestWorkflowService>(
            MockBehavior.Loose,
            _dataStore,
            null!,
            null!,
            null!,
            null!);

        _mockCacheService = new Mock<IGraphCacheService>();
        _mockLogger = new Mock<ILogger<GraphService>>();

        _service = new GraphService(
            _mockProjectService.Object,
            _mockGitHubService.Object,
            _mockFleeceService.Object,
            _mockSessionStore.Object,
            _dataStore,
            _mockWorkflowService.Object,
            _mockCacheService.Object,
            new Mock<IPRStatusResolver>().Object,
            _mockLogger.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _dataStore.Clear();

        // Clean up the temp directory
        if (_testProject != null && Directory.Exists(_testProject.LocalPath))
        {
            try
            {
                Directory.Delete(_testProject.LocalPath, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    #region Actionable Issue Sorting Tests

    [Test]
    public async Task BuildEnhancedTaskGraphAsync_ActionableIssues_SortedByPriority()
    {
        // Arrange - Create actionable issues with different priorities
        var issues = new[]
        {
            CreateIssue("issue-1", "P2 Issue", priority: 2, createdAt: DateTime.UtcNow.AddDays(-1)),
            CreateIssue("issue-2", "P0 Issue", priority: 0, createdAt: DateTime.UtcNow.AddDays(-2)),
            CreateIssue("issue-3", "P1 Issue", priority: 1, createdAt: DateTime.UtcNow.AddDays(-3)),
            CreateIssue("issue-4", "Unprioritized", priority: null, createdAt: DateTime.UtcNow.AddDays(-4))
        };

        var taskGraph = new TaskGraph
        {
            TotalLanes = 1,
            Nodes = issues.Select((issue, index) => new TaskGraphNode
            {
                Issue = issue,
                Lane = 0,
                Row = index,
                IsActionable = true
            }).ToList()
        };

        _mockFleeceService.Setup(s => s.GetTaskGraphWithAdditionalIssuesAsync(
                _testProject.LocalPath,
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(taskGraph);

        // Act
        var response = await _service.BuildEnhancedTaskGraphAsync(_testProject.Id);

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Nodes, Has.Count.EqualTo(4));

        // Check priority order: P0, P1, P2, then null
        var sortedNodes = response.Nodes.ToList();
        Assert.That(sortedNodes[0].Issue.Priority, Is.EqualTo(0), "First should be P0");
        Assert.That(sortedNodes[1].Issue.Priority, Is.EqualTo(1), "Second should be P1");
        Assert.That(sortedNodes[2].Issue.Priority, Is.EqualTo(2), "Third should be P2");
        Assert.That(sortedNodes[3].Issue.Priority, Is.Null, "Last should be unprioritized");
    }

    [Test]
    public async Task BuildEnhancedTaskGraphAsync_ActionableIssues_SamePriority_SortedByAge()
    {
        // Arrange - Create actionable issues with same priority but different ages
        var oldestDate = DateTime.UtcNow.AddDays(-10);
        var middleDate = DateTime.UtcNow.AddDays(-5);
        var newestDate = DateTime.UtcNow.AddDays(-1);

        var issues = new[]
        {
            CreateIssue("issue-1", "Newest P1", priority: 1, createdAt: newestDate),
            CreateIssue("issue-2", "Oldest P1", priority: 1, createdAt: oldestDate),
            CreateIssue("issue-3", "Middle P1", priority: 1, createdAt: middleDate)
        };

        var taskGraph = new TaskGraph
        {
            TotalLanes = 1,
            Nodes = issues.Select((issue, index) => new TaskGraphNode
            {
                Issue = issue,
                Lane = 0,
                Row = index,
                IsActionable = true
            }).ToList()
        };

        _mockFleeceService.Setup(s => s.GetTaskGraphWithAdditionalIssuesAsync(
                _testProject.LocalPath,
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(taskGraph);

        // Act
        var response = await _service.BuildEnhancedTaskGraphAsync(_testProject.Id);

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Nodes, Has.Count.EqualTo(3));

        // Check age order: oldest first
        var sortedNodes = response.Nodes.ToList();
        Assert.That(sortedNodes[0].Issue.Title, Is.EqualTo("Oldest P1"), "Oldest should be first");
        Assert.That(sortedNodes[1].Issue.Title, Is.EqualTo("Middle P1"), "Middle should be second");
        Assert.That(sortedNodes[2].Issue.Title, Is.EqualTo("Newest P1"), "Newest should be last");
    }

    [Test]
    public async Task BuildEnhancedTaskGraphAsync_MixedActionableAndNonActionable_OnlyActionableSorted()
    {
        // Arrange - Create mix of actionable and non-actionable issues
        var issues = new[]
        {
            CreateIssue("issue-1", "Non-actionable P0", priority: 0, createdAt: DateTime.UtcNow.AddDays(-10)),
            CreateIssue("issue-2", "Actionable P2", priority: 2, createdAt: DateTime.UtcNow.AddDays(-5)),
            CreateIssue("issue-3", "Actionable P1", priority: 1, createdAt: DateTime.UtcNow.AddDays(-3)),
            CreateIssue("issue-4", "Non-actionable P1", priority: 1, createdAt: DateTime.UtcNow.AddDays(-1))
        };

        var taskGraph = new TaskGraph
        {
            TotalLanes = 1,
            Nodes = new List<TaskGraphNode>
            {
                new TaskGraphNode { Issue = issues[0], Lane = 0, Row = 0, IsActionable = false },
                new TaskGraphNode { Issue = issues[1], Lane = 0, Row = 1, IsActionable = true },
                new TaskGraphNode { Issue = issues[2], Lane = 0, Row = 2, IsActionable = true },
                new TaskGraphNode { Issue = issues[3], Lane = 0, Row = 3, IsActionable = false }
            }
        };

        _mockFleeceService.Setup(s => s.GetTaskGraphWithAdditionalIssuesAsync(
                _testProject.LocalPath,
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(taskGraph);

        // Act
        var response = await _service.BuildEnhancedTaskGraphAsync(_testProject.Id);

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Nodes, Has.Count.EqualTo(4));

        // Actionable issues should be sorted by priority
        var actionableNodes = response.Nodes.Where(n => n.IsActionable).ToList();
        Assert.That(actionableNodes[0].Issue.Priority, Is.EqualTo(1), "First actionable should be P1");
        Assert.That(actionableNodes[1].Issue.Priority, Is.EqualTo(2), "Second actionable should be P2");

        // Non-actionable issues should maintain their original positions
        var nonActionableNodes = response.Nodes.Where(n => !n.IsActionable).ToList();
        Assert.That(nonActionableNodes[0].Issue.Title, Is.EqualTo("Non-actionable P0"));
        Assert.That(nonActionableNodes[1].Issue.Title, Is.EqualTo("Non-actionable P1"));
    }

    [Test]
    public async Task BuildEnhancedTaskGraphAsync_SingleActionableIssue_NoSortingNeeded()
    {
        // Arrange - Single actionable issue
        var issue = CreateIssue("issue-1", "Single Issue", priority: 2, createdAt: DateTime.UtcNow);

        var taskGraph = new TaskGraph
        {
            TotalLanes = 1,
            Nodes = [new TaskGraphNode { Issue = issue, Lane = 0, Row = 0, IsActionable = true }]
        };

        _mockFleeceService.Setup(s => s.GetTaskGraphWithAdditionalIssuesAsync(
                _testProject.LocalPath,
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(taskGraph);

        // Act
        var response = await _service.BuildEnhancedTaskGraphAsync(_testProject.Id);

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Nodes, Has.Count.EqualTo(1));
        Assert.That(response.Nodes[0].Issue.Title, Is.EqualTo("Single Issue"));
    }

    [Test]
    public async Task BuildEnhancedTaskGraphAsync_ActionableIssuesInGroups_MaintainsGrouping()
    {
        // Arrange - Create issues in different lanes (representing groups)
        var issues = new[]
        {
            // Group 1 (Lane 0)
            CreateIssue("issue-1", "Group1 P2", priority: 2, createdAt: DateTime.UtcNow.AddDays(-5)),
            CreateIssue("issue-2", "Group1 P1", priority: 1, createdAt: DateTime.UtcNow.AddDays(-3)),
            // Group 2 (Lane 1)
            CreateIssue("issue-3", "Group2 P0", priority: 0, createdAt: DateTime.UtcNow.AddDays(-4)),
            CreateIssue("issue-4", "Group2 P3", priority: 3, createdAt: DateTime.UtcNow.AddDays(-2))
        };

        var taskGraph = new TaskGraph
        {
            TotalLanes = 2,
            Nodes = new List<TaskGraphNode>
            {
                new TaskGraphNode { Issue = issues[0], Lane = 0, Row = 0, IsActionable = true },
                new TaskGraphNode { Issue = issues[1], Lane = 0, Row = 1, IsActionable = true },
                new TaskGraphNode { Issue = issues[2], Lane = 1, Row = 0, IsActionable = true },
                new TaskGraphNode { Issue = issues[3], Lane = 1, Row = 1, IsActionable = true }
            }
        };

        _mockFleeceService.Setup(s => s.GetTaskGraphWithAdditionalIssuesAsync(
                _testProject.LocalPath,
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(taskGraph);

        // Act
        var response = await _service.BuildEnhancedTaskGraphAsync(_testProject.Id);

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Nodes, Has.Count.EqualTo(4));

        // Verify each group is sorted by priority
        var group1Nodes = response.Nodes.Where(n => n.Lane == 0).OrderBy(n => n.Row).ToList();
        Assert.That(group1Nodes[0].Issue.Priority, Is.EqualTo(1), "Group 1: P1 should be first");
        Assert.That(group1Nodes[1].Issue.Priority, Is.EqualTo(2), "Group 1: P2 should be second");

        var group2Nodes = response.Nodes.Where(n => n.Lane == 1).OrderBy(n => n.Row).ToList();
        Assert.That(group2Nodes[0].Issue.Priority, Is.EqualTo(0), "Group 2: P0 should be first");
        Assert.That(group2Nodes[1].Issue.Priority, Is.EqualTo(3), "Group 2: P3 should be second");
    }

    [Test]
    public async Task BuildEnhancedTaskGraphAsync_AllUnprioritized_SortedByAgeOnly()
    {
        // Arrange - Create actionable issues with no priority
        var oldestDate = DateTime.UtcNow.AddDays(-10);
        var middleDate = DateTime.UtcNow.AddDays(-5);
        var newestDate = DateTime.UtcNow.AddDays(-1);

        var issues = new[]
        {
            CreateIssue("issue-1", "Newest", priority: null, createdAt: newestDate),
            CreateIssue("issue-2", "Oldest", priority: null, createdAt: oldestDate),
            CreateIssue("issue-3", "Middle", priority: null, createdAt: middleDate)
        };

        var taskGraph = new TaskGraph
        {
            TotalLanes = 1,
            Nodes = issues.Select((issue, index) => new TaskGraphNode
            {
                Issue = issue,
                Lane = 0,
                Row = index,
                IsActionable = true
            }).ToList()
        };

        _mockFleeceService.Setup(s => s.GetTaskGraphWithAdditionalIssuesAsync(
                _testProject.LocalPath,
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(taskGraph);

        // Act
        var response = await _service.BuildEnhancedTaskGraphAsync(_testProject.Id);

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Nodes, Has.Count.EqualTo(3));

        // Check age order: oldest first
        var sortedNodes = response.Nodes.ToList();
        Assert.That(sortedNodes[0].Issue.Title, Is.EqualTo("Oldest"), "Oldest should be first");
        Assert.That(sortedNodes[1].Issue.Title, Is.EqualTo("Middle"), "Middle should be second");
        Assert.That(sortedNodes[2].Issue.Title, Is.EqualTo("Newest"), "Newest should be last");
    }

    #endregion

    #region Helper Methods

    private static Issue CreateIssue(string id, string title, int? priority = null, DateTime? createdAt = null)
    {
        return new Issue
        {
            Id = id,
            Title = title,
            Status = IssueStatus.Open,
            Type = IssueType.Task,
            Priority = priority,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            LastUpdate = createdAt ?? DateTime.UtcNow,
            Description = "",
            ExecutionMode = ExecutionMode.Series,
            ParentIssues = [],
            Tags = [],
            LinkedIssues = [],
            CreatedBy = "test-user",
            AssignedTo = null
        };
    }

    #endregion
}