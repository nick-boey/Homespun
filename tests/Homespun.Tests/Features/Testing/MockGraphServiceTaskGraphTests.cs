using Fleece.Core.Models;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Fleece.Services;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.Testing.Services;
using Homespun.Shared.Models.Projects;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Testing;

[TestFixture]
public class MockGraphServiceTaskGraphTests
{
    private MockGraphService _service = null!;

    [SetUp]
    public void SetUp()
    {
        var dataStore = new Mock<IDataStore>();
        var fleeceService = new Mock<IFleeceService>();
        var sessionStore = new Mock<IClaudeSessionStore>();
        var logger = new Mock<ILogger<MockGraphService>>();

        // Set up project lookup
        var project = new Project
        {
            Id = "demo-project",
            Name = "Demo Project",
            LocalPath = "/tmp/demo-project",
            DefaultBranch = "main"
        };
        dataStore.Setup(d => d.GetProject("demo-project")).Returns(project);

        // Set up issues matching the seeded data
        var now = DateTimeOffset.UtcNow;
        var issues = new List<Issue>
        {
            new() { Id = "ISSUE-001", Title = "Add dark mode support", Type = IssueType.Feature, Status = IssueStatus.Open, Priority = 2, CreatedAt = now.AddDays(-14), LastUpdate = now.AddDays(-2) },
            new() { Id = "ISSUE-002", Title = "Improve mobile responsiveness", Type = IssueType.Task, Status = IssueStatus.Open, Priority = 3, CreatedAt = now.AddDays(-12), LastUpdate = now.AddDays(-1) },
            new() { Id = "ISSUE-003", Title = "Fix login timeout bug", Type = IssueType.Bug, Status = IssueStatus.Progress, Priority = 1, CreatedAt = now.AddDays(-7), LastUpdate = now.AddHours(-6) },
            new() { Id = "ISSUE-004", Title = "Design API schema", Type = IssueType.Task, Status = IssueStatus.Open, Priority = 2, CreatedAt = now.AddDays(-10), LastUpdate = now.AddDays(-3) },
            new() { Id = "ISSUE-005", Title = "Implement API endpoints", Type = IssueType.Task, Status = IssueStatus.Open, Priority = 2, ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-004", SortOrder = "0" }], CreatedAt = now.AddDays(-9), LastUpdate = now.AddDays(-2) },
            new() { Id = "ISSUE-006", Title = "Write API documentation", Type = IssueType.Chore, Status = IssueStatus.Open, Priority = 3, ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-005", SortOrder = "0" }], CreatedAt = now.AddDays(-8), LastUpdate = now.AddDays(-1) },
            new() { Id = "ISSUE-007", Title = "Implement GET endpoints", Type = IssueType.Task, Status = IssueStatus.Open, Priority = 2, ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-005", SortOrder = "0" }], CreatedAt = now.AddDays(-7), LastUpdate = now.AddDays(-1) },
            new() { Id = "ISSUE-008", Title = "Implement POST endpoints", Type = IssueType.Task, Status = IssueStatus.Open, Priority = 2, ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-007", SortOrder = "0" }], CreatedAt = now.AddDays(-6), LastUpdate = now.AddDays(-1) },
            new() { Id = "ISSUE-009", Title = "Implement PUT/PATCH endpoints", Type = IssueType.Task, Status = IssueStatus.Open, Priority = 2, ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-008", SortOrder = "0" }], CreatedAt = now.AddDays(-5), LastUpdate = now.AddDays(-1) },
            new() { Id = "ISSUE-010", Title = "Implement DELETE endpoints", Type = IssueType.Task, Status = IssueStatus.Open, Priority = 2, ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-009", SortOrder = "0" }], CreatedAt = now.AddDays(-4), LastUpdate = now.AddDays(-1) },
            new() { Id = "ISSUE-011", Title = "Add request validation", Type = IssueType.Task, Status = IssueStatus.Open, Priority = 3, ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-008", SortOrder = "0" }], CreatedAt = now.AddDays(-5), LastUpdate = now.AddDays(-2) },
            new() { Id = "ISSUE-012", Title = "Add rate limiting", Type = IssueType.Task, Status = IssueStatus.Open, Priority = 3, ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-007", SortOrder = "0" }], CreatedAt = now.AddDays(-6), LastUpdate = now.AddDays(-3) },
            new() { Id = "ISSUE-013", Title = "Set up API monitoring", Type = IssueType.Chore, Status = IssueStatus.Open, Priority = 4, ParentIssues = [new ParentIssueRef { ParentIssue = "ISSUE-005", SortOrder = "0" }], CreatedAt = now.AddDays(-7), LastUpdate = now.AddDays(-2) },
        };
        fleeceService.Setup(f => f.ListIssuesAsync("/tmp/demo-project")).ReturnsAsync(issues);

        _service = new MockGraphService(dataStore.Object, fleeceService.Object, sessionStore.Object, logger.Object);
    }

    [Test]
    public async Task BuildTaskGraphAsync_ReturnsTaskGraph()
    {
        var taskGraph = await _service.BuildTaskGraphAsync("demo-project");

        Assert.That(taskGraph, Is.Not.Null);
        Assert.That(taskGraph, Is.InstanceOf<TaskGraph>());
    }

    [Test]
    public async Task BuildTaskGraphAsync_CorrectNodeCount()
    {
        var taskGraph = await _service.BuildTaskGraphAsync("demo-project");

        Assert.That(taskGraph, Is.Not.Null);
        Assert.That(taskGraph!.Nodes, Has.Count.EqualTo(13));
    }

    [Test]
    public async Task BuildTaskGraphAsync_NextIssuesCorrect()
    {
        var taskGraph = await _service.BuildTaskGraphAsync("demo-project");

        Assert.That(taskGraph, Is.Not.Null);

        var actionableNodes = taskGraph!.Nodes
            .Where(n => n.IsActionable)
            .Select(n => n.Issue.Id)
            .ToList();

        // ISSUE-003 is Progress status, Fleece.Core does not mark it as actionable
        // In Fleece.Core v1.4.0, issues with incomplete parents are NOT actionable
        // ISSUE-006 has parent ISSUE-005 which is Open, so it's NOT actionable
        Assert.That(actionableNodes, Does.Contain("ISSUE-001"));
        Assert.That(actionableNodes, Does.Contain("ISSUE-002"));
        Assert.That(actionableNodes, Does.Not.Contain("ISSUE-006"), "ISSUE-006 has incomplete parent ISSUE-005");
        Assert.That(actionableNodes, Does.Contain("ISSUE-010"));
    }

    [Test]
    public async Task BuildTaskGraphAsync_OrphansAtLaneZero()
    {
        var taskGraph = await _service.BuildTaskGraphAsync("demo-project");

        Assert.That(taskGraph, Is.Not.Null);

        var issue001 = taskGraph!.Nodes.Single(n => n.Issue.Id == "ISSUE-001");
        var issue002 = taskGraph.Nodes.Single(n => n.Issue.Id == "ISSUE-002");
        var issue003 = taskGraph.Nodes.Single(n => n.Issue.Id == "ISSUE-003");

        Assert.That(issue001.Lane, Is.EqualTo(0));
        Assert.That(issue002.Lane, Is.EqualTo(0));
        Assert.That(issue003.Lane, Is.EqualTo(0));
    }

    [Test]
    public async Task BuildTaskGraphTextAsync_ReturnsNonEmptyText()
    {
        var text = await _service.BuildTaskGraphTextAsync("demo-project");

        Assert.That(text, Is.Not.Null);
        Assert.That(text, Is.Not.Empty);
        Assert.That(text, Does.Contain("ISSUE-001"));
        Assert.That(text, Does.Contain("ISSUE-010"));
    }
}
