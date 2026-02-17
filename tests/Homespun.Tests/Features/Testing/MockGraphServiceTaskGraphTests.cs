using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Gitgraph.Data;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.Testing.Services;
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
        var sessionStore = new Mock<IClaudeSessionStore>();
        var logger = new Mock<ILogger<MockGraphService>>();

        _service = new MockGraphService(dataStore.Object, sessionStore.Object, logger.Object);
    }

    [Test]
    public async Task BuildTaskGraphAsync_ReturnsOnlyIssueNodes()
    {
        var graph = await _service.BuildTaskGraphAsync("demo-project");

        Assert.That(graph, Is.Not.Null);
        Assert.That(graph!.Nodes, Has.All.InstanceOf<TaskGraphIssueNode>());
    }

    [Test]
    public async Task BuildTaskGraphAsync_CorrectNodeCount()
    {
        var graph = await _service.BuildTaskGraphAsync("demo-project");

        Assert.That(graph, Is.Not.Null);
        Assert.That(graph!.Nodes, Has.Count.EqualTo(13));
    }

    [Test]
    public async Task BuildTaskGraphAsync_ActionableItemsAtLaneZero()
    {
        var graph = await _service.BuildTaskGraphAsync("demo-project");

        Assert.That(graph, Is.Not.Null);

        // ISSUE-010 is a leaf node (deepest in chain), should be actionable at lane 0
        var issue010 = graph!.Nodes.OfType<TaskGraphIssueNode>().Single(n => n.Issue.Id == "ISSUE-010");
        Assert.That(issue010.Lane, Is.EqualTo(0));
        Assert.That(issue010.IsActionable, Is.True);
    }

    [Test]
    public async Task BuildTaskGraphAsync_RootIssuesAtHighestLane()
    {
        var graph = await _service.BuildTaskGraphAsync("demo-project");

        Assert.That(graph, Is.Not.Null);

        var nodes = graph!.Nodes.OfType<TaskGraphIssueNode>().ToList();
        var issue004 = nodes.Single(n => n.Issue.Id == "ISSUE-004");
        var maxLane = nodes.Max(n => n.Lane);

        // ISSUE-004 is the root of the dependency chain and should have the highest lane
        Assert.That(issue004.Lane, Is.EqualTo(maxLane));
        Assert.That(issue004.Lane, Is.GreaterThan(0));
    }

    [Test]
    public async Task BuildTaskGraphAsync_OrphansAtLaneZero()
    {
        var graph = await _service.BuildTaskGraphAsync("demo-project");

        Assert.That(graph, Is.Not.Null);

        var nodes = graph!.Nodes.OfType<TaskGraphIssueNode>().ToList();

        // Orphans have no parent issues and no children, so they are actionable at lane 0
        var issue001 = nodes.Single(n => n.Issue.Id == "ISSUE-001");
        var issue002 = nodes.Single(n => n.Issue.Id == "ISSUE-002");
        var issue003 = nodes.Single(n => n.Issue.Id == "ISSUE-003");

        Assert.That(issue001.Lane, Is.EqualTo(0));
        Assert.That(issue002.Lane, Is.EqualTo(0));
        Assert.That(issue003.Lane, Is.EqualTo(0));
    }
}
