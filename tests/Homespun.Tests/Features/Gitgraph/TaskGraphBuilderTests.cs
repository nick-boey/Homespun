using Fleece.Core.Models;
using Homespun.Features.Gitgraph.Data;
using Homespun.Features.Gitgraph.Services;

namespace Homespun.Tests.Features.Gitgraph;

[TestFixture]
public class TaskGraphBuilderTests
{
    private TaskGraphBuilder _builder = null!;

    [SetUp]
    public void SetUp()
    {
        _builder = new TaskGraphBuilder();
    }

    #region Basic Graph Building

    [Test]
    public void Build_SingleNode_CreatesGraph()
    {
        // Arrange
        var issue = CreateIssue("bd-001");
        var taskGraph = new TaskGraph
        {
            Nodes = [CreateTaskGraphNode(issue, lane: 0, row: 0, isActionable: true)],
            TotalLanes = 1
        };

        // Act
        var graph = _builder.Build(taskGraph);

        // Assert
        Assert.That(graph.Nodes, Has.Count.EqualTo(1));
        var node = graph.Nodes[0] as TaskGraphIssueNode;
        Assert.That(node, Is.Not.Null);
        Assert.That(node!.Issue.Id, Is.EqualTo("bd-001"));
        Assert.That(node.Lane, Is.EqualTo(0));
        Assert.That(node.IsActionable, Is.True);
    }

    [Test]
    public void Build_ActionableNodes_HaveLaneZero()
    {
        // Arrange
        var issue1 = CreateIssue("bd-001");
        var issue2 = CreateIssue("bd-002");
        var taskGraph = new TaskGraph
        {
            Nodes = [
                CreateTaskGraphNode(issue1, lane: 0, row: 0, isActionable: true),
                CreateTaskGraphNode(issue2, lane: 0, row: 1, isActionable: true)
            ],
            TotalLanes = 1
        };

        // Act
        var graph = _builder.Build(taskGraph);

        // Assert
        var nodes = graph.Nodes.OfType<TaskGraphIssueNode>().ToList();
        Assert.That(nodes, Has.Count.EqualTo(2));
        Assert.That(nodes.All(n => n.Lane == 0), Is.True);
        Assert.That(nodes.All(n => n.IsActionable), Is.True);
    }

    [Test]
    public void Build_BlockedNodes_HaveHigherLanes()
    {
        // Arrange: bd-001 blocks bd-002, so bd-001 is at lane 1, bd-002 at lane 0
        var issue1 = CreateIssue("bd-001");
        var issue2 = CreateIssueWithParent("bd-002", "bd-001");
        var taskGraph = new TaskGraph
        {
            Nodes = [
                CreateTaskGraphNode(issue1, lane: 1, row: 0, isActionable: false),
                CreateTaskGraphNode(issue2, lane: 0, row: 1, isActionable: true)
            ],
            TotalLanes = 2
        };

        // Act
        var graph = _builder.Build(taskGraph);

        // Assert
        var nodes = graph.Nodes.OfType<TaskGraphIssueNode>().ToList();
        var bd001 = nodes.First(n => n.Issue.Id == "bd-001");
        var bd002 = nodes.First(n => n.Issue.Id == "bd-002");

        Assert.That(bd001.Lane, Is.EqualTo(1)); // Parent/blocking issue at higher lane
        Assert.That(bd002.Lane, Is.EqualTo(0)); // Actionable issue at lane 0
        Assert.That(bd001.IsActionable, Is.False);
        Assert.That(bd002.IsActionable, Is.True);
    }

    #endregion

    #region Parent Connections

    [Test]
    public void Build_ChildNode_ReferencesParent()
    {
        // Arrange: bd-001 blocks bd-002
        var issue1 = CreateIssue("bd-001");
        var issue2 = CreateIssueWithParent("bd-002", "bd-001");
        var taskGraph = new TaskGraph
        {
            Nodes = [
                CreateTaskGraphNode(issue1, lane: 1, row: 0, isActionable: false),
                CreateTaskGraphNode(issue2, lane: 0, row: 1, isActionable: true)
            ],
            TotalLanes = 2
        };

        // Act
        var graph = _builder.Build(taskGraph);

        // Assert
        var bd002 = graph.Nodes.OfType<TaskGraphIssueNode>().First(n => n.Issue.Id == "bd-002");
        Assert.That(bd002.ParentIds, Contains.Item("issue-bd-001"));
    }

    [Test]
    public void Build_MultipleParents_AllReferenced()
    {
        // Arrange: bd-003 is blocked by both bd-001 and bd-002
        var issue1 = CreateIssue("bd-001");
        var issue2 = CreateIssue("bd-002");
        var issue3 = CreateIssueWithParents("bd-003", ["bd-001", "bd-002"]);
        var taskGraph = new TaskGraph
        {
            Nodes = [
                CreateTaskGraphNode(issue1, lane: 1, row: 0, isActionable: false),
                CreateTaskGraphNode(issue2, lane: 2, row: 1, isActionable: false),
                CreateTaskGraphNode(issue3, lane: 0, row: 2, isActionable: true)
            ],
            TotalLanes = 3
        };

        // Act
        var graph = _builder.Build(taskGraph);

        // Assert
        var bd003 = graph.Nodes.OfType<TaskGraphIssueNode>().First(n => n.Issue.Id == "bd-003");
        Assert.That(bd003.ParentIds, Contains.Item("issue-bd-001"));
        Assert.That(bd003.ParentIds, Contains.Item("issue-bd-002"));
    }

    #endregion

    #region Node Ordering

    [Test]
    public void Build_NodesOrderedByRowThenId()
    {
        // Arrange
        var issue1 = CreateIssue("bd-001");
        var issue2 = CreateIssue("bd-002");
        var issue3 = CreateIssue("bd-003");
        var taskGraph = new TaskGraph
        {
            Nodes = [
                CreateTaskGraphNode(issue1, lane: 1, row: 0, isActionable: false),
                CreateTaskGraphNode(issue2, lane: 0, row: 1, isActionable: true),
                CreateTaskGraphNode(issue3, lane: 2, row: 1, isActionable: false)
            ],
            TotalLanes = 3
        };

        // Act
        var graph = _builder.Build(taskGraph);

        // Assert - Ordered by row first, then issue ID within row
        var nodes = graph.Nodes.OfType<TaskGraphIssueNode>().ToList();
        Assert.That(nodes[0].Issue.Id, Is.EqualTo("bd-001")); // Row 0
        Assert.That(nodes[1].Issue.Id, Is.EqualTo("bd-002")); // Row 1, ID bd-002
        Assert.That(nodes[2].Issue.Id, Is.EqualTo("bd-003")); // Row 1, ID bd-003
    }

    #endregion

    #region Branch Creation

    [Test]
    public void Build_CreatesMainBranch()
    {
        // Arrange
        var issue = CreateIssue("bd-001");
        var taskGraph = new TaskGraph
        {
            Nodes = [CreateTaskGraphNode(issue, lane: 0, row: 0, isActionable: true)],
            TotalLanes = 1
        };

        // Act
        var graph = _builder.Build(taskGraph);

        // Assert
        Assert.That(graph.Branches.ContainsKey("main"), Is.True);
        Assert.That(graph.MainBranchName, Is.EqualTo("main"));
    }

    [Test]
    public void Build_CreatesBranchForEachIssue()
    {
        // Arrange
        var issue1 = CreateIssue("bd-001");
        var issue2 = CreateIssue("bd-002");
        var taskGraph = new TaskGraph
        {
            Nodes = [
                CreateTaskGraphNode(issue1, lane: 0, row: 0, isActionable: true),
                CreateTaskGraphNode(issue2, lane: 0, row: 1, isActionable: true)
            ],
            TotalLanes = 1
        };

        // Act
        var graph = _builder.Build(taskGraph);

        // Assert
        Assert.That(graph.Branches.ContainsKey("issue-bd-001"), Is.True);
        Assert.That(graph.Branches.ContainsKey("issue-bd-002"), Is.True);
    }

    #endregion

    #region PR Status Integration

    [Test]
    public void Build_WithPrStatus_UsesStatusColor()
    {
        // Arrange
        var issue = CreateIssue("bd-001");
        var taskGraph = new TaskGraph
        {
            Nodes = [CreateTaskGraphNode(issue, lane: 0, row: 0, isActionable: true)],
            TotalLanes = 1
        };
        var prStatuses = new Dictionary<string, PullRequestStatus>
        {
            ["bd-001"] = PullRequestStatus.ReadyForReview
        };

        // Act
        var graph = _builder.Build(taskGraph, prStatuses);

        // Assert
        var node = graph.Nodes.OfType<TaskGraphIssueNode>().First();
        Assert.That(node.LinkedPrStatus, Is.EqualTo(PullRequestStatus.ReadyForReview));
        Assert.That(node.Color, Is.EqualTo("#eab308")); // Yellow for ReadyForReview
    }

    [Test]
    public void Build_WithoutPrStatus_UsesBugColorForBugs()
    {
        // Arrange
        var issue = CreateIssue("bd-001", type: IssueType.Bug);
        var taskGraph = new TaskGraph
        {
            Nodes = [CreateTaskGraphNode(issue, lane: 0, row: 0, isActionable: true)],
            TotalLanes = 1
        };

        // Act
        var graph = _builder.Build(taskGraph);

        // Assert
        var node = graph.Nodes.OfType<TaskGraphIssueNode>().First();
        Assert.That(node.Color, Is.EqualTo("#ef4444")); // Red for bugs
    }

    #endregion

    #region Empty Graph

    [Test]
    public void Build_EmptyTaskGraph_ReturnsEmptyGraph()
    {
        // Arrange
        var taskGraph = new TaskGraph
        {
            Nodes = [],
            TotalLanes = 0
        };

        // Act
        var graph = _builder.Build(taskGraph);

        // Assert
        Assert.That(graph.Nodes, Is.Empty);
        Assert.That(graph.Branches, Contains.Key("main")); // Main branch always exists
    }

    #endregion

    #region Helper Methods

    private static TaskGraphNode CreateTaskGraphNode(Issue issue, int lane, int row, bool isActionable)
    {
        return new TaskGraphNode
        {
            Issue = issue,
            Lane = lane,
            Row = row,
            IsActionable = isActionable
        };
    }

    private static Issue CreateIssue(string id, int? priority = null, IssueType type = IssueType.Task) => new()
    {
        Id = id,
        Title = $"Issue {id}",
        Status = IssueStatus.Open,
        Type = type,
        Priority = priority,
        CreatedAt = DateTimeOffset.UtcNow,
        LastUpdate = DateTimeOffset.UtcNow
    };

    private static Issue CreateIssueWithParent(string id, string parentId) => new()
    {
        Id = id,
        Title = $"Issue {id}",
        Status = IssueStatus.Open,
        Type = IssueType.Task,
        ParentIssues = [new ParentIssueRef { ParentIssue = parentId, SortOrder = "0" }],
        CreatedAt = DateTimeOffset.UtcNow,
        LastUpdate = DateTimeOffset.UtcNow
    };

    private static Issue CreateIssueWithParents(string id, List<string> parentIds) => new()
    {
        Id = id,
        Title = $"Issue {id}",
        Status = IssueStatus.Open,
        Type = IssueType.Task,
        ParentIssues = parentIds.Select((pid, idx) => new ParentIssueRef { ParentIssue = pid, SortOrder = idx.ToString() }).ToList(),
        CreatedAt = DateTimeOffset.UtcNow,
        LastUpdate = DateTimeOffset.UtcNow
    };

    #endregion
}
