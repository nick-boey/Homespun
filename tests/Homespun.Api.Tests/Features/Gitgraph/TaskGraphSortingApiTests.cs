using System.Net.Http.Json;
using Fleece.Core.Models;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Projects;
using Homespun.Shared.Models.Fleece;
using Homespun.Tests.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace Homespun.Api.Tests.Features.Gitgraph;

/// <summary>
/// API integration tests for task graph sorting functionality.
/// Verifies that actionable issues are sorted by priority and then by age via the API.
/// </summary>
[TestFixture]
public class TaskGraphSortingApiTests : IDisposable
{
    private HomespunWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;
    private Project _testProject = null!;

    [SetUp]
    public async Task SetUp()
    {
        _factory = new HomespunWebApplicationFactory();

        _client = _factory.CreateClient();

        // Create a test project
        _testProject = new Project
        {
            Name = "test-project",
            LocalPath = "/test/path",
            GitHubOwner = "test-owner",
            GitHubRepo = "test-repo",
            DefaultBranch = "main"
        };

        await _factory.MockDataStore.AddProjectAsync(_testProject);
    }

    [TearDown]
    public void TearDown()
    {
        _client?.Dispose();
        _factory?.Dispose();
    }

    [Test]
    public async Task GetTaskGraph_ActionableIssuesSortedByPriorityAndAge()
    {
        // Arrange - Create actionable issues with mixed priorities and ages
        var issues = new[]
        {
            CreateIssue("issue-1", "P2 Old", priority: 2, createdAt: DateTime.UtcNow.AddDays(-10)),
            CreateIssue("issue-2", "P0 New", priority: 0, createdAt: DateTime.UtcNow.AddDays(-1)),
            CreateIssue("issue-3", "P1 Mid", priority: 1, createdAt: DateTime.UtcNow.AddDays(-5)),
            CreateIssue("issue-4", "P2 New", priority: 2, createdAt: DateTime.UtcNow.AddDays(-2)),
            CreateIssue("issue-5", "No Priority Old", priority: null, createdAt: DateTime.UtcNow.AddDays(-15))
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

        // Add the test issues to the mock fleece service
        foreach (var node in taskGraph.Nodes)
        {
            await _factory.MockFleeceService.CreateIssueAsync(
                _testProject.LocalPath,
                node.Issue.Title,
                node.Issue.Type,
                node.Issue.Description,
                node.Issue.Priority,
                node.Issue.ExecutionMode,
                node.Issue.Status
            );
        }

        // Act
        var response = await _client.GetFromJsonAsync<TaskGraphResponse>($"/api/graph/{_testProject.Id}/taskgraph/data");

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Nodes, Has.Count.EqualTo(5));

        // Verify sort order: P0, P1, P2 (old), P2 (new), null
        var sortedNodes = response.Nodes.ToList();
        Assert.That(sortedNodes[0].Issue.Title, Is.EqualTo("P0 New"), "P0 should be first");
        Assert.That(sortedNodes[1].Issue.Title, Is.EqualTo("P1 Mid"), "P1 should be second");
        Assert.That(sortedNodes[2].Issue.Title, Is.EqualTo("P2 Old"), "P2 Old should be third (older)");
        Assert.That(sortedNodes[3].Issue.Title, Is.EqualTo("P2 New"), "P2 New should be fourth (newer)");
        Assert.That(sortedNodes[4].Issue.Title, Is.EqualTo("No Priority Old"), "Unprioritized should be last");
    }

    [Test]
    public async Task GetTaskGraph_NonActionableIssuesNotSorted()
    {
        // Arrange - Mix of actionable and non-actionable issues
        var issues = new[]
        {
            CreateIssue("issue-1", "Non-actionable P0", priority: 0, createdAt: DateTime.UtcNow.AddDays(-10)),
            CreateIssue("issue-2", "Actionable P2", priority: 2, createdAt: DateTime.UtcNow.AddDays(-5)),
            CreateIssue("issue-3", "Actionable P1", priority: 1, createdAt: DateTime.UtcNow.AddDays(-3)),
            CreateIssue("issue-4", "Non-actionable P3", priority: 3, createdAt: DateTime.UtcNow.AddDays(-1))
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

        // Add the test issues to the mock fleece service
        foreach (var node in taskGraph.Nodes)
        {
            await _factory.MockFleeceService.CreateIssueAsync(
                _testProject.LocalPath,
                node.Issue.Title,
                node.Issue.Type,
                node.Issue.Description,
                node.Issue.Priority,
                node.Issue.ExecutionMode,
                node.Issue.Status
            );
        }

        // Act
        var response = await _client.GetFromJsonAsync<TaskGraphResponse>($"/api/graph/{_testProject.Id}/taskgraph/data");

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Nodes, Has.Count.EqualTo(4));

        // Verify actionable issues are sorted
        var actionableNodes = response.Nodes.Where(n => n.IsActionable).ToList();
        Assert.That(actionableNodes[0].Issue.Title, Is.EqualTo("Actionable P1"));
        Assert.That(actionableNodes[1].Issue.Title, Is.EqualTo("Actionable P2"));

        // Verify non-actionable maintain original positions
        Assert.That(response.Nodes[0].Issue.Title, Is.EqualTo("Non-actionable P0"));
        Assert.That(response.Nodes[3].Issue.Title, Is.EqualTo("Non-actionable P3"));
    }

    [Test]
    public async Task GetTaskGraph_GroupsPreserved_WithSortingWithinGroups()
    {
        // Arrange - Issues in different lanes (groups)
        var issues = new[]
        {
            // Group 1 (Lane 0)
            CreateIssue("issue-1", "G1 P2 New", priority: 2, createdAt: DateTime.UtcNow.AddDays(-2)),
            CreateIssue("issue-2", "G1 P1 Old", priority: 1, createdAt: DateTime.UtcNow.AddDays(-10)),
            CreateIssue("issue-3", "G1 P2 Old", priority: 2, createdAt: DateTime.UtcNow.AddDays(-8)),
            // Group 2 (Lane 1)
            CreateIssue("issue-4", "G2 P0", priority: 0, createdAt: DateTime.UtcNow.AddDays(-5)),
            CreateIssue("issue-5", "G2 No Priority", priority: null, createdAt: DateTime.UtcNow.AddDays(-3))
        };

        var taskGraph = new TaskGraph
        {
            TotalLanes = 2,
            Nodes = new List<TaskGraphNode>
            {
                new TaskGraphNode { Issue = issues[0], Lane = 0, Row = 0, IsActionable = true },
                new TaskGraphNode { Issue = issues[1], Lane = 0, Row = 1, IsActionable = true },
                new TaskGraphNode { Issue = issues[2], Lane = 0, Row = 2, IsActionable = true },
                new TaskGraphNode { Issue = issues[3], Lane = 1, Row = 0, IsActionable = true },
                new TaskGraphNode { Issue = issues[4], Lane = 1, Row = 1, IsActionable = true }
            }
        };

        // Add the test issues to the mock fleece service
        foreach (var node in taskGraph.Nodes)
        {
            await _factory.MockFleeceService.CreateIssueAsync(
                _testProject.LocalPath,
                node.Issue.Title,
                node.Issue.Type,
                node.Issue.Description,
                node.Issue.Priority,
                node.Issue.ExecutionMode,
                node.Issue.Status
            );
        }

        // Act
        var response = await _client.GetFromJsonAsync<TaskGraphResponse>($"/api/graph/{_testProject.Id}/taskgraph/data");

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.Nodes, Has.Count.EqualTo(5));

        // Verify Group 1 sorting
        var group1Nodes = response.Nodes.Where(n => n.Lane == 0).OrderBy(n => n.Row).ToList();
        Assert.That(group1Nodes[0].Issue.Title, Is.EqualTo("G1 P1 Old"), "G1: P1 should be first");
        Assert.That(group1Nodes[1].Issue.Title, Is.EqualTo("G1 P2 Old"), "G1: P2 Old should be second");
        Assert.That(group1Nodes[2].Issue.Title, Is.EqualTo("G1 P2 New"), "G1: P2 New should be third");

        // Verify Group 2 sorting
        var group2Nodes = response.Nodes.Where(n => n.Lane == 1).OrderBy(n => n.Row).ToList();
        Assert.That(group2Nodes[0].Issue.Title, Is.EqualTo("G2 P0"), "G2: P0 should be first");
        Assert.That(group2Nodes[1].Issue.Title, Is.EqualTo("G2 No Priority"), "G2: Unprioritized should be last");
    }

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

    public void Dispose()
    {
        _client?.Dispose();
        _factory?.Dispose();
    }
}