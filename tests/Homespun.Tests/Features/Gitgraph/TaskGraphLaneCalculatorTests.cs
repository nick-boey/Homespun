using Homespun.Shared.Models.Gitgraph;

namespace Homespun.Tests.Features.Gitgraph;

[TestFixture]
public class TaskGraphLaneCalculatorTests
{
    private TaskGraphLaneCalculator _calculator = null!;

    [SetUp]
    public void SetUp()
    {
        _calculator = new TaskGraphLaneCalculator();
    }

    #region Basic Lane Calculation

    [Test]
    public void Calculate_EmptyNodes_ReturnsEmptyLayout()
    {
        // Arrange
        var nodes = new List<IGraphNode>();

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert
        Assert.That(layout.LaneAssignments, Is.Empty);
        Assert.That(layout.MaxLanes, Is.EqualTo(1));
        Assert.That(layout.RowInfos, Is.Empty);
    }

    [Test]
    public void Calculate_SingleNode_AssignsCorrectLane()
    {
        // Arrange
        var nodes = new List<IGraphNode>
        {
            new TestGraphNode("issue-bd-001", taskGraphLane: 0)
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert
        Assert.That(layout.LaneAssignments["issue-bd-001"], Is.EqualTo(0));
        Assert.That(layout.MaxLanes, Is.EqualTo(1));
        Assert.That(layout.RowInfos, Has.Count.EqualTo(1));
        Assert.That(layout.RowInfos[0].NodeLane, Is.EqualTo(0));
    }

    [Test]
    public void Calculate_MultipleNodes_AssignsCorrectLanes()
    {
        // Arrange
        var nodes = new List<IGraphNode>
        {
            new TestGraphNode("issue-bd-001", taskGraphLane: 0),
            new TestGraphNode("issue-bd-002", taskGraphLane: 1),
            new TestGraphNode("issue-bd-003", taskGraphLane: 2)
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert
        Assert.That(layout.LaneAssignments["issue-bd-001"], Is.EqualTo(0));
        Assert.That(layout.LaneAssignments["issue-bd-002"], Is.EqualTo(1));
        Assert.That(layout.LaneAssignments["issue-bd-003"], Is.EqualTo(2));
        Assert.That(layout.MaxLanes, Is.EqualTo(3));
    }

    #endregion

    #region Connector Calculation

    [Test]
    public void Calculate_NodeWithParentInDifferentLane_HasConnector()
    {
        // Arrange: bd-002 has parent bd-001, they're in different lanes
        var nodes = new List<IGraphNode>
        {
            new TestGraphNode("issue-bd-001", taskGraphLane: 1),
            new TestGraphNode("issue-bd-002", taskGraphLane: 0, parentIds: new[] { "issue-bd-001" })
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert
        var bd002RowInfo = layout.RowInfos[1];
        Assert.That(bd002RowInfo.ConnectorFromLane, Is.EqualTo(1)); // Connector from lane 1 (bd-001)
    }

    [Test]
    public void Calculate_NodeWithParentInSameLane_NoConnector()
    {
        // Arrange: Both nodes in same lane (unlikely in task graph but possible)
        var nodes = new List<IGraphNode>
        {
            new TestGraphNode("issue-bd-001", taskGraphLane: 0),
            new TestGraphNode("issue-bd-002", taskGraphLane: 0, parentIds: new[] { "issue-bd-001" })
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert
        var bd002RowInfo = layout.RowInfos[1];
        Assert.That(bd002RowInfo.ConnectorFromLane, Is.Null); // No connector when same lane
    }

    [Test]
    public void Calculate_NodeWithNoParent_NoConnector()
    {
        // Arrange
        var nodes = new List<IGraphNode>
        {
            new TestGraphNode("issue-bd-001", taskGraphLane: 0)
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert
        var rowInfo = layout.RowInfos[0];
        Assert.That(rowInfo.ConnectorFromLane, Is.Null);
    }

    #endregion

    #region Active Lanes

    [Test]
    public void Calculate_ActiveLanes_IncludesLaneZero()
    {
        // Arrange
        var nodes = new List<IGraphNode>
        {
            new TestGraphNode("issue-bd-001", taskGraphLane: 1)
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - Lane 0 should always be active
        Assert.That(layout.RowInfos[0].ActiveLanes, Contains.Item(0));
        Assert.That(layout.RowInfos[0].ActiveLanes, Contains.Item(1)); // The node's lane
    }

    [Test]
    public void Calculate_ActiveLanes_IncludesAllNodesAtOrBeforeRow()
    {
        // Arrange
        var nodes = new List<IGraphNode>
        {
            new TestGraphNode("issue-bd-001", taskGraphLane: 0),
            new TestGraphNode("issue-bd-002", taskGraphLane: 1),
            new TestGraphNode("issue-bd-003", taskGraphLane: 2)
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - Row 2 (bd-003) should have lanes 0, 1, 2 active
        var row2Info = layout.RowInfos[2];
        Assert.That(row2Info.ActiveLanes, Contains.Item(0));
        Assert.That(row2Info.ActiveLanes, Contains.Item(1));
        Assert.That(row2Info.ActiveLanes, Contains.Item(2));
    }

    #endregion

    #region First/Last in Lane

    [Test]
    public void Calculate_FirstInLane_MarkedCorrectly()
    {
        // Arrange
        var nodes = new List<IGraphNode>
        {
            new TestGraphNode("issue-bd-001", taskGraphLane: 0),
            new TestGraphNode("issue-bd-002", taskGraphLane: 1) // First in lane 1
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert
        Assert.That(layout.RowInfos[0].IsFirstRowInLane, Is.False); // Lane 0 is never marked as first
        Assert.That(layout.RowInfos[1].IsFirstRowInLane, Is.True);  // First node in lane 1
    }

    [Test]
    public void Calculate_LastInLane_MarkedCorrectly()
    {
        // Arrange
        var nodes = new List<IGraphNode>
        {
            new TestGraphNode("issue-bd-001", taskGraphLane: 1),
            new TestGraphNode("issue-bd-002", taskGraphLane: 1) // Last in lane 1
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert
        Assert.That(layout.RowInfos[0].IsLastRowInLane, Is.False); // Not the last
        Assert.That(layout.RowInfos[1].IsLastRowInLane, Is.True);  // Last node in lane 1
    }

    [Test]
    public void Calculate_SingleNodeInLane_IsFirstAndLast()
    {
        // Arrange
        var nodes = new List<IGraphNode>
        {
            new TestGraphNode("issue-bd-001", taskGraphLane: 1) // Only node in lane 1
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert
        Assert.That(layout.RowInfos[0].IsFirstRowInLane, Is.True);
        Assert.That(layout.RowInfos[0].IsLastRowInLane, Is.True);
    }

    [Test]
    public void Calculate_LaneZero_NeverFirstOrLast()
    {
        // Arrange
        var nodes = new List<IGraphNode>
        {
            new TestGraphNode("issue-bd-001", taskGraphLane: 0),
            new TestGraphNode("issue-bd-002", taskGraphLane: 0)
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - Lane 0 nodes should never be marked as first/last (it's the main lane)
        Assert.That(layout.RowInfos[0].IsFirstRowInLane, Is.False);
        Assert.That(layout.RowInfos[0].IsLastRowInLane, Is.False);
        Assert.That(layout.RowInfos[1].IsFirstRowInLane, Is.False);
        Assert.That(layout.RowInfos[1].IsLastRowInLane, Is.False);
    }

    #endregion

    #region Node Without TaskGraphLane

    [Test]
    public void Calculate_NodeWithoutTaskGraphLane_DefaultsToZero()
    {
        // Arrange - Node without TaskGraphLane property
        var nodes = new List<IGraphNode>
        {
            new TestGraphNode("issue-bd-001", taskGraphLane: null)
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert
        Assert.That(layout.LaneAssignments["issue-bd-001"], Is.EqualTo(0));
    }

    #endregion

    #region Helper Classes

    private class TestGraphNode : IGraphNode
    {
        public TestGraphNode(string id, int? taskGraphLane = 0, string[]? parentIds = null)
        {
            Id = id;
            TaskGraphLane = taskGraphLane;
            ParentIds = parentIds ?? [];
        }

        public string Id { get; }
        public string Title => Id;
        public GraphNodeType NodeType => GraphNodeType.Issue;
        public GraphNodeStatus Status => GraphNodeStatus.Open;
        public IReadOnlyList<string> ParentIds { get; }
        public string BranchName => Id;
        public DateTime SortDate => DateTime.UtcNow;
        public int TimeDimension => 2;
        public string? Url => null;
        public string? Color => "#3b82f6";
        public string? Tag => "Task";
        public int? PullRequestNumber => null;
        public string? IssueId => Id.Replace("issue-", "");
        public bool? HasDescription => true;
        public int? TaskGraphLane { get; }
        public int? TaskGraphRow => null;
        public bool? IsActionable => TaskGraphLane == 0;
    }

    #endregion
}
