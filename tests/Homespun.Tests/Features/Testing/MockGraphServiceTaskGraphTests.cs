using Fleece.Core.Models;
using Homespun.Features.ClaudeCode.Services;
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
