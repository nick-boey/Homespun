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
/// Tests for the GraphService's behavior of showing issues with open PRs regardless of status.
/// Issues that have open PRs should always appear in the task graph, even if their status is Complete/Closed/etc.
/// </summary>
[TestFixture]
public class GraphServiceOpenPrIssueTests
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
        var testPath = Path.Combine(Path.GetTempPath(), $"graphservice-openpr-test-{Guid.NewGuid()}");
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

    #region Open PR Issue Inclusion Tests

    [Test]
    public async Task BuildEnhancedTaskGraphAsync_CompletedIssueWithOpenPR_IsIncludedInTaskGraph()
    {
        // Arrange - Create a completed issue with an open PR linked to it
        var completedIssue = CreateIssue("issue-123", IssueStatus.Complete);

        // Create a tracked PR (tracked PRs are always open)
        var trackedPr = await CreateTrackedPullRequest(_testProject.Id, "issue-123", prNumber: 42);

        // Set up FleeceService to return the completed issue when specifically requested
        // and the task graph with the issue included
        var taskGraph = new TaskGraph
        {
            TotalLanes = 1,
            Nodes = [new TaskGraphNode { Issue = completedIssue, Lane = 0, Row = 0, IsActionable = false }]
        };

        _mockFleeceService
            .Setup(s => s.GetTaskGraphWithAdditionalIssuesAsync(
                _testProject.LocalPath,
                It.Is<IEnumerable<string>>(ids => ids.Contains("issue-123")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(taskGraph);

        // Act
        var response = await _service.BuildEnhancedTaskGraphAsync(_testProject.Id);

        // Assert - Completed issue with open PR should be in the graph
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Nodes, Has.Count.EqualTo(1));
        Assert.That(response.Nodes[0].Issue.Id, Is.EqualTo("issue-123"));
        Assert.That(response.LinkedPrs.ContainsKey("issue-123"), Is.True);
        Assert.That(response.LinkedPrs["issue-123"].Number, Is.EqualTo(42));
    }

    [Test]
    public async Task BuildEnhancedTaskGraphAsync_OpenIssueWithoutPR_IsIncludedInTaskGraph()
    {
        // Arrange - Create an open issue without any linked PR
        var openIssue = CreateIssue("issue-456", IssueStatus.Open);

        var taskGraph = new TaskGraph
        {
            TotalLanes = 1,
            Nodes = [new TaskGraphNode { Issue = openIssue, Lane = 0, Row = 0, IsActionable = true }]
        };

        _mockFleeceService
            .Setup(s => s.GetTaskGraphWithAdditionalIssuesAsync(
                _testProject.LocalPath,
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(taskGraph);

        // Act
        var response = await _service.BuildEnhancedTaskGraphAsync(_testProject.Id);

        // Assert - Open issue should be in the graph (standard behavior)
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Nodes, Has.Count.EqualTo(1));
        Assert.That(response.Nodes[0].Issue.Id, Is.EqualTo("issue-456"));
    }

    [Test]
    public async Task BuildEnhancedTaskGraphAsync_CompletedIssueWithoutOpenPR_IsNotIncludedInTaskGraph()
    {
        // Arrange - Create a completed issue WITHOUT any linked PR
        // The task graph should be empty because there are no open issues and no open-PR-linked issues

        var emptyTaskGraph = new TaskGraph
        {
            TotalLanes = 1,
            Nodes = []
        };

        _mockFleeceService
            .Setup(s => s.GetTaskGraphWithAdditionalIssuesAsync(
                _testProject.LocalPath,
                It.Is<IEnumerable<string>>(ids => !ids.Any()),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyTaskGraph);

        // Act
        var response = await _service.BuildEnhancedTaskGraphAsync(_testProject.Id);

        // Assert - No issues should be in the graph
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Nodes, Is.Empty);
    }

    [Test]
    public async Task BuildEnhancedTaskGraphAsync_MixedIssues_CorrectlyFilters()
    {
        // Arrange - Create a mix of issues:
        // 1. Open issue without PR (included - standard behavior)
        // 2. Completed issue with open PR (included - new behavior)
        // 3. Completed issue without PR (excluded)
        var openIssue = CreateIssue("open-issue", IssueStatus.Open);
        var completedWithPr = CreateIssue("completed-with-pr", IssueStatus.Complete);

        // Create tracked PR for the completed issue
        await CreateTrackedPullRequest(_testProject.Id, "completed-with-pr", prNumber: 99);

        var taskGraph = new TaskGraph
        {
            TotalLanes = 1,
            Nodes =
            [
                new TaskGraphNode { Issue = openIssue, Lane = 0, Row = 0, IsActionable = true },
                new TaskGraphNode { Issue = completedWithPr, Lane = 0, Row = 1, IsActionable = false }
            ]
        };

        _mockFleeceService
            .Setup(s => s.GetTaskGraphWithAdditionalIssuesAsync(
                _testProject.LocalPath,
                It.Is<IEnumerable<string>>(ids => ids.Contains("completed-with-pr")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(taskGraph);

        // Act
        var response = await _service.BuildEnhancedTaskGraphAsync(_testProject.Id);

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Nodes, Has.Count.EqualTo(2));

        var nodeIds = response.Nodes.Select(n => n.Issue.Id).ToHashSet();
        Assert.That(nodeIds.Contains("open-issue"), Is.True, "Open issue should be included");
        Assert.That(nodeIds.Contains("completed-with-pr"), Is.True, "Completed issue with open PR should be included");
    }

    [Test]
    public async Task BuildEnhancedTaskGraphAsync_IssueWithOpenPR_HasLinkedPrInfo()
    {
        // Arrange
        var issue = CreateIssue("issue-with-pr", IssueStatus.Progress);
        await CreateTrackedPullRequest(_testProject.Id, "issue-with-pr", prNumber: 123);

        var taskGraph = new TaskGraph
        {
            TotalLanes = 1,
            Nodes = [new TaskGraphNode { Issue = issue, Lane = 0, Row = 0, IsActionable = true }]
        };

        _mockFleeceService
            .Setup(s => s.GetTaskGraphWithAdditionalIssuesAsync(
                _testProject.LocalPath,
                It.IsAny<IEnumerable<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(taskGraph);

        // Act
        var response = await _service.BuildEnhancedTaskGraphAsync(_testProject.Id);

        // Assert - Should have linked PR info
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.LinkedPrs.ContainsKey("issue-with-pr"), Is.True);
        Assert.That(response.LinkedPrs["issue-with-pr"].Number, Is.EqualTo(123));
        Assert.That(response.LinkedPrs["issue-with-pr"].Url, Does.Contain("/pull/123"));
    }

    [Test]
    public async Task BuildEnhancedTaskGraphAsync_CaseInsensitiveIssueIdMatching_WithOpenPR()
    {
        // Arrange - Create an issue with uppercase ID but PR linked with lowercase
        var issue = CreateIssue("ISSUE-ABC", IssueStatus.Complete);
        await CreateTrackedPullRequest(_testProject.Id, "issue-abc", prNumber: 50); // lowercase

        var taskGraph = new TaskGraph
        {
            TotalLanes = 1,
            Nodes = [new TaskGraphNode { Issue = issue, Lane = 0, Row = 0, IsActionable = false }]
        };

        _mockFleeceService
            .Setup(s => s.GetTaskGraphWithAdditionalIssuesAsync(
                _testProject.LocalPath,
                It.Is<IEnumerable<string>>(ids => ids.Any(id =>
                    string.Equals(id, "issue-abc", StringComparison.OrdinalIgnoreCase))),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(taskGraph);

        // Act
        var response = await _service.BuildEnhancedTaskGraphAsync(_testProject.Id);

        // Assert - Case-insensitive matching should work
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Nodes, Has.Count.EqualTo(1));
    }

    #endregion

    #region Helper Methods

    private static Issue CreateIssue(string id, IssueStatus status)
    {
        return new Issue
        {
            Id = id,
            Title = $"Issue {id}",
            Status = status,
            Type = IssueType.Task,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow
        };
    }

    private async Task<PullRequest> CreateTrackedPullRequest(string projectId, string beadsIssueId, int prNumber)
    {
        var pr = new PullRequest
        {
            ProjectId = projectId,
            Title = $"PR #{prNumber} for {beadsIssueId}",
            BranchName = $"feature/{beadsIssueId}",
            BeadsIssueId = beadsIssueId,
            GitHubPRNumber = prNumber,
            Status = OpenPullRequestStatus.InDevelopment
        };
        await _dataStore.AddPullRequestAsync(pr);
        return pr;
    }

    #endregion
}
