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
    public void Calculate_SingleNode_AssignsLaneZero()
    {
        // Arrange - single node with no relationships
        var nodes = new List<IGraphNode>
        {
            new TestGraphNode("issue-bd-001")
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - leaf node should be lane 0
        Assert.That(layout.LaneAssignments["issue-bd-001"], Is.EqualTo(0));
        Assert.That(layout.MaxLanes, Is.EqualTo(1));
        Assert.That(layout.RowInfos, Has.Count.EqualTo(1));
        Assert.That(layout.RowInfos[0].NodeLane, Is.EqualTo(0));
    }

    [Test]
    public void Calculate_MultipleUnrelatedNodes_AllLaneZero()
    {
        // Arrange - three independent nodes
        var nodes = new List<IGraphNode>
        {
            new TestGraphNode("issue-bd-001"),
            new TestGraphNode("issue-bd-002"),
            new TestGraphNode("issue-bd-003")
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - all leaf nodes should be lane 0
        Assert.That(layout.LaneAssignments["issue-bd-001"], Is.EqualTo(0));
        Assert.That(layout.LaneAssignments["issue-bd-002"], Is.EqualTo(0));
        Assert.That(layout.LaneAssignments["issue-bd-003"], Is.EqualTo(0));
        Assert.That(layout.MaxLanes, Is.EqualTo(1));
    }

    [Test]
    public void Calculate_ParentChildPair_ChildLaneZeroParentLaneOne()
    {
        // Arrange: bd-002 is a sub-task of bd-001 (bd-002 has parent bd-001)
        // bd-001 is the container/parent issue, bd-002 is the actionable leaf
        var nodes = new List<IGraphNode>
        {
            new TestGraphNode("issue-bd-001"),
            new TestGraphNode("issue-bd-002", parentIds: ["issue-bd-001"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - leaf child at lane 0, parent at lane 1
        Assert.That(layout.LaneAssignments["issue-bd-002"], Is.EqualTo(0));
        Assert.That(layout.LaneAssignments["issue-bd-001"], Is.EqualTo(1));
        Assert.That(layout.MaxLanes, Is.EqualTo(2));
    }

    [Test]
    public void Calculate_Chain_ThreeLanes()
    {
        // Arrange: A -> B -> C (C is child of B, B is child of A)
        // A is root parent, B is intermediate, C is leaf
        var nodes = new List<IGraphNode>
        {
            new TestGraphNode("issue-A"),
            new TestGraphNode("issue-B", parentIds: ["issue-A"]),
            new TestGraphNode("issue-C", parentIds: ["issue-B"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - C is leaf (lane 0), B has child C (lane 1), A has child B (lane 2)
        Assert.That(layout.LaneAssignments["issue-C"], Is.EqualTo(0));
        Assert.That(layout.LaneAssignments["issue-B"], Is.EqualTo(1));
        Assert.That(layout.LaneAssignments["issue-A"], Is.EqualTo(2));
        Assert.That(layout.MaxLanes, Is.EqualTo(3));
    }

    [Test]
    public void Calculate_TreeStructure_ParentAtMaxChildLanePlusOne()
    {
        // Arrange: A is parent of B and C (both are leaf children)
        var nodes = new List<IGraphNode>
        {
            new TestGraphNode("issue-A"),
            new TestGraphNode("issue-B", parentIds: ["issue-A"]),
            new TestGraphNode("issue-C", parentIds: ["issue-A"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - Both children at lane 0, parent at lane 1
        Assert.That(layout.LaneAssignments["issue-B"], Is.EqualTo(0));
        Assert.That(layout.LaneAssignments["issue-C"], Is.EqualTo(0));
        Assert.That(layout.LaneAssignments["issue-A"], Is.EqualTo(1));
        Assert.That(layout.MaxLanes, Is.EqualTo(2));
    }

    [Test]
    public void Calculate_Diamond_CorrectLanes()
    {
        // Arrange: Diamond shape
        //   A (root parent)
        //  / \
        // B   C (intermediate)
        //  \ /
        //   D (leaf)
        // D is child of both B and C, B and C are children of A
        var nodes = new List<IGraphNode>
        {
            new TestGraphNode("issue-A"),
            new TestGraphNode("issue-B", parentIds: ["issue-A"]),
            new TestGraphNode("issue-C", parentIds: ["issue-A"]),
            new TestGraphNode("issue-D", parentIds: ["issue-B", "issue-C"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - D is leaf (lane 0), B and C have child D (lane 1), A has children B,C (lane 2)
        Assert.That(layout.LaneAssignments["issue-D"], Is.EqualTo(0));
        Assert.That(layout.LaneAssignments["issue-B"], Is.EqualTo(1));
        Assert.That(layout.LaneAssignments["issue-C"], Is.EqualTo(1));
        Assert.That(layout.LaneAssignments["issue-A"], Is.EqualTo(2));
        Assert.That(layout.MaxLanes, Is.EqualTo(3));
    }

    [Test]
    public void Calculate_ParentNotInGraph_ChildTreatedAsLeaf()
    {
        // Arrange - node references a parent that isn't in the current graph
        var nodes = new List<IGraphNode>
        {
            new TestGraphNode("issue-bd-002", parentIds: ["issue-bd-001"]) // bd-001 not in graph
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - parent not in graph, so this node is effectively a leaf
        Assert.That(layout.LaneAssignments["issue-bd-002"], Is.EqualTo(0));
    }

    #endregion

    #region Connector Calculation

    [Test]
    public void Calculate_NodeWithParentInDifferentLane_HasConnector()
    {
        // Arrange: bd-002 is child of bd-001
        // bd-002 (leaf) at lane 0, bd-001 (parent) at lane 1
        var nodes = new List<IGraphNode>
        {
            new TestGraphNode("issue-bd-001"),
            new TestGraphNode("issue-bd-002", parentIds: ["issue-bd-001"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - bd-002 should have connector from bd-001's lane (1)
        var bd002RowInfo = layout.RowInfos.First(r => r.NodeId == "issue-bd-002");
        Assert.That(bd002RowInfo.ConnectorFromLane, Is.EqualTo(1));
    }

    [Test]
    public void Calculate_UnrelatedNodes_NoConnector()
    {
        // Arrange: Two unrelated nodes both at lane 0
        var nodes = new List<IGraphNode>
        {
            new TestGraphNode("issue-bd-001"),
            new TestGraphNode("issue-bd-002")
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert
        Assert.That(layout.RowInfos[0].ConnectorFromLane, Is.Null);
        Assert.That(layout.RowInfos[1].ConnectorFromLane, Is.Null);
    }

    [Test]
    public void Calculate_NodeWithNoParent_NoConnector()
    {
        // Arrange
        var nodes = new List<IGraphNode>
        {
            new TestGraphNode("issue-bd-001")
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
        // Arrange - parent node at lane 1 (has a child)
        var nodes = new List<IGraphNode>
        {
            new TestGraphNode("issue-bd-001"),
            new TestGraphNode("issue-bd-002", parentIds: ["issue-bd-001"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - Lane 0 should always be active
        foreach (var rowInfo in layout.RowInfos)
        {
            Assert.That(rowInfo.ActiveLanes, Contains.Item(0));
        }
    }

    [Test]
    public void Calculate_ActiveLanes_IncludesNodeLanes()
    {
        // Arrange - chain: C -> B -> A gives lanes 0, 1, 2
        var nodes = new List<IGraphNode>
        {
            new TestGraphNode("issue-A"),
            new TestGraphNode("issue-B", parentIds: ["issue-A"]),
            new TestGraphNode("issue-C", parentIds: ["issue-B"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - last row should have all lanes active
        var lastRowInfo = layout.RowInfos[^1];
        Assert.That(lastRowInfo.ActiveLanes, Contains.Item(0));
    }

    #endregion

    #region First/Last in Lane

    [Test]
    public void Calculate_FirstInLane_MarkedCorrectly()
    {
        // Arrange - parent-child: parent at lane 1, child at lane 0
        var nodes = new List<IGraphNode>
        {
            new TestGraphNode("issue-bd-001"),
            new TestGraphNode("issue-bd-002", parentIds: ["issue-bd-001"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - find the node at lane 1 (parent)
        var lane1Row = layout.RowInfos.First(r => r.NodeLane == 1);
        Assert.That(lane1Row.IsFirstRowInLane, Is.True); // First (and only) node in lane 1
    }

    [Test]
    public void Calculate_LastInLane_MarkedCorrectly()
    {
        // Arrange - two children of one parent: both at lane 0, parent at lane 1
        // But we also need two nodes at the SAME higher lane to test first/last
        // Chain: D -> C -> B, D -> C -> A gives A,B at lane 0, C at lane 1, no wait...
        // Let's use: A is parent of B and C. D is parent of A.
        // B,C at lane 0, A at lane 1, D at lane 2
        // But we want two nodes at lane 1: A parent of B, E parent of C, D parent of A and E
        var nodes = new List<IGraphNode>
        {
            new TestGraphNode("issue-D"),
            new TestGraphNode("issue-A", parentIds: ["issue-D"]),
            new TestGraphNode("issue-E", parentIds: ["issue-D"]),
            new TestGraphNode("issue-B", parentIds: ["issue-A"]),
            new TestGraphNode("issue-C", parentIds: ["issue-E"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - B,C at lane 0; A,E at lane 1; D at lane 2
        Assert.That(layout.LaneAssignments["issue-B"], Is.EqualTo(0));
        Assert.That(layout.LaneAssignments["issue-C"], Is.EqualTo(0));
        Assert.That(layout.LaneAssignments["issue-A"], Is.EqualTo(1));
        Assert.That(layout.LaneAssignments["issue-E"], Is.EqualTo(1));
        Assert.That(layout.LaneAssignments["issue-D"], Is.EqualTo(2));

        // Find row infos for lane 1 nodes
        var lane1Rows = layout.RowInfos.Where(r => r.NodeLane == 1).ToList();
        Assert.That(lane1Rows, Has.Count.EqualTo(2));
        Assert.That(lane1Rows[0].IsFirstRowInLane, Is.True);
        Assert.That(lane1Rows[0].IsLastRowInLane, Is.False);
        Assert.That(lane1Rows[1].IsFirstRowInLane, Is.False);
        Assert.That(lane1Rows[1].IsLastRowInLane, Is.True);
    }

    [Test]
    public void Calculate_SingleNodeInLane_IsFirstAndLast()
    {
        // Arrange - parent at lane 1, single node
        var nodes = new List<IGraphNode>
        {
            new TestGraphNode("issue-bd-001"),
            new TestGraphNode("issue-bd-002", parentIds: ["issue-bd-001"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - single node in lane 1 is both first and last
        var lane1Row = layout.RowInfos.First(r => r.NodeLane == 1);
        Assert.That(lane1Row.IsFirstRowInLane, Is.True);
        Assert.That(lane1Row.IsLastRowInLane, Is.True);
    }

    [Test]
    public void Calculate_LaneZero_NeverFirstOrLast()
    {
        // Arrange - two independent leaf nodes
        var nodes = new List<IGraphNode>
        {
            new TestGraphNode("issue-bd-001"),
            new TestGraphNode("issue-bd-002")
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

    #region Mixed Hierarchies

    [Test]
    public void Calculate_MixedDepthBranches_CorrectLanes()
    {
        // Arrange: A has children B and C. B has child D.
        // D is at lane 0, C is at lane 0, B has child D so lane 1, A has children B(1) and C(0) so lane 2
        var nodes = new List<IGraphNode>
        {
            new TestGraphNode("issue-A"),
            new TestGraphNode("issue-B", parentIds: ["issue-A"]),
            new TestGraphNode("issue-C", parentIds: ["issue-A"]),
            new TestGraphNode("issue-D", parentIds: ["issue-B"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert
        Assert.That(layout.LaneAssignments["issue-D"], Is.EqualTo(0)); // leaf
        Assert.That(layout.LaneAssignments["issue-C"], Is.EqualTo(0)); // leaf
        Assert.That(layout.LaneAssignments["issue-B"], Is.EqualTo(1)); // parent of D
        Assert.That(layout.LaneAssignments["issue-A"], Is.EqualTo(2)); // parent of B(1),C(0) → max(1,0)+1=2
        Assert.That(layout.MaxLanes, Is.EqualTo(3));
    }

    #endregion

    #region Pre-computed Lanes

    [Test]
    public void Calculate_PrecomputedLanes_UsedDirectly()
    {
        // Arrange: Nodes with pre-computed lanes should bypass parent-child algorithm
        // Without pre-computed lanes, A->B->C would give C=0, B=1, A=2
        // With pre-computed lanes, we can assign any values
        var nodes = new List<IGraphNode>
        {
            new TestGraphNode("issue-A", taskGraphLane: 3),
            new TestGraphNode("issue-B", parentIds: ["issue-A"], taskGraphLane: 2),
            new TestGraphNode("issue-C", parentIds: ["issue-B"], taskGraphLane: 1)
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - pre-computed values used as-is
        Assert.That(layout.LaneAssignments["issue-A"], Is.EqualTo(3));
        Assert.That(layout.LaneAssignments["issue-B"], Is.EqualTo(2));
        Assert.That(layout.LaneAssignments["issue-C"], Is.EqualTo(1));
    }

    [Test]
    public void Calculate_PrecomputedLanes_SiblingLeafNotAtZero()
    {
        // Arrange: Simulates the real scenario where ISSUE-011 (leaf) should be lane 1, not 0
        // Parent at lane 2 has two children: one with sub-children (lane 1) and one leaf (also lane 1)
        // Without pre-computed lanes, both leaves would get lane 0 (wrong)
        var nodes = new List<IGraphNode>
        {
            new TestGraphNode("issue-parent", taskGraphLane: 2),
            new TestGraphNode("issue-deep-child", parentIds: ["issue-parent"], taskGraphLane: 1),
            new TestGraphNode("issue-leaf-sibling", parentIds: ["issue-parent"], taskGraphLane: 1),
            new TestGraphNode("issue-deepest", parentIds: ["issue-deep-child"], taskGraphLane: 0)
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - leaf sibling at lane 1 (not lane 0)
        Assert.That(layout.LaneAssignments["issue-leaf-sibling"], Is.EqualTo(1));
        Assert.That(layout.LaneAssignments["issue-deepest"], Is.EqualTo(0));
        Assert.That(layout.LaneAssignments["issue-parent"], Is.EqualTo(2));
    }

    [Test]
    public void Calculate_PrecomputedLanes_ConnectorsStillWork()
    {
        // Arrange: Pre-computed lanes with parent-child connectors
        var nodes = new List<IGraphNode>
        {
            new TestGraphNode("issue-root", taskGraphLane: 3),
            new TestGraphNode("issue-child", parentIds: ["issue-root"], taskGraphLane: 1)
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - connector from parent lane (3) to child lane (1)
        var childRow = layout.RowInfos.First(r => r.NodeId == "issue-child");
        Assert.That(childRow.ConnectorFromLane, Is.EqualTo(3));
    }

    [Test]
    public void Calculate_NoPrecomputedLanes_FallsBackToParentChildAlgorithm()
    {
        // Arrange: No TaskGraphLane set, should use existing bottom-up algorithm
        var nodes = new List<IGraphNode>
        {
            new TestGraphNode("issue-A"),
            new TestGraphNode("issue-B", parentIds: ["issue-A"]),
            new TestGraphNode("issue-C", parentIds: ["issue-B"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - original algorithm: leaf=0, parent=max(child)+1
        Assert.That(layout.LaneAssignments["issue-C"], Is.EqualTo(0));
        Assert.That(layout.LaneAssignments["issue-B"], Is.EqualTo(1));
        Assert.That(layout.LaneAssignments["issue-A"], Is.EqualTo(2));
    }

    #endregion

    #region Helper Classes

    private class TestGraphNode : IGraphNode
    {
        public TestGraphNode(string id, string[]? parentIds = null, int? taskGraphLane = null)
        {
            Id = id;
            ParentIds = parentIds ?? [];
            TaskGraphLane = taskGraphLane;
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
    }

    #endregion
}
