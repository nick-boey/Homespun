using Homespun.Features.Gitgraph.Data;
using Homespun.Features.Gitgraph.Services;

namespace Homespun.Tests.Features.Gitgraph;

[TestFixture]
public class TimelineLaneCalculatorTests
{
    private TimelineLaneCalculator _calculator = null!;

    [SetUp]
    public void SetUp()
    {
        _calculator = new TimelineLaneCalculator();
    }

    #region Basic Layout Tests

    [Test]
    public void Calculate_EmptyNodes_ReturnsEmptyLayout()
    {
        // Act
        var layout = _calculator.Calculate([]);

        // Assert
        Assert.That(layout.LaneAssignments, Is.Empty);
        Assert.That(layout.MaxLanes, Is.EqualTo(1));
        Assert.That(layout.RowInfos, Is.Empty);
    }

    [Test]
    public void Calculate_SingleMainNode_UsesLaneZero()
    {
        // Arrange
        var nodes = new List<IGraphNode>
        {
            CreateNode("node-1", "main")
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert
        Assert.That(layout.LaneAssignments["node-1"], Is.EqualTo(0));
        Assert.That(layout.MaxLanes, Is.EqualTo(1));
        Assert.That(layout.RowInfos, Has.Count.EqualTo(1));
        Assert.That(layout.RowInfos[0].NodeLane, Is.EqualTo(0));
    }

    [Test]
    public void Calculate_LinearMainBranch_AllInLaneZero()
    {
        // Arrange - Three nodes in main branch
        var nodes = new List<IGraphNode>
        {
            CreateNode("node-1", "main"),
            CreateNode("node-2", "main", parentIds: ["node-1"]),
            CreateNode("node-3", "main", parentIds: ["node-2"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - All nodes should be in lane 0
        Assert.That(layout.LaneAssignments["node-1"], Is.EqualTo(0));
        Assert.That(layout.LaneAssignments["node-2"], Is.EqualTo(0));
        Assert.That(layout.LaneAssignments["node-3"], Is.EqualTo(0));
        Assert.That(layout.MaxLanes, Is.EqualTo(1));
    }

    #endregion

    #region Branch Tests

    [Test]
    public void Calculate_SingleBranch_UsesLaneOne()
    {
        // Arrange - Main branch + one side branch
        var nodes = new List<IGraphNode>
        {
            CreateNode("pr-1", "main"),
            CreateNode("pr-2", "feature/test", parentIds: ["pr-1"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert
        Assert.That(layout.LaneAssignments["pr-1"], Is.EqualTo(0));
        Assert.That(layout.LaneAssignments["pr-2"], Is.EqualTo(1));
        Assert.That(layout.MaxLanes, Is.EqualTo(2));
    }

    [Test]
    public void Calculate_MultipleBranches_UsesDifferentLanes()
    {
        // Arrange - Main branch + two side branches where one ends before the other starts
        // This tests lane reuse behavior: feature/a ends, then feature/b reuses its lane
        var nodes = new List<IGraphNode>
        {
            CreateNode("pr-1", "main"),
            CreateNode("pr-2", "feature/a", parentIds: ["pr-1"]),
            CreateNode("pr-2b", "feature/a", parentIds: ["pr-2"]),
            CreateNode("pr-3", "feature/b", parentIds: ["pr-1"]),
            CreateNode("pr-3b", "feature/b", parentIds: ["pr-3"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - feature/a uses lane 1, then ends, feature/b reuses lane 1
        Assert.That(layout.LaneAssignments["pr-1"], Is.EqualTo(0));
        Assert.That(layout.LaneAssignments["pr-2"], Is.EqualTo(1));
        Assert.That(layout.LaneAssignments["pr-2b"], Is.EqualTo(1));
        Assert.That(layout.LaneAssignments["pr-3"], Is.EqualTo(1)); // Reuses lane 1
        Assert.That(layout.LaneAssignments["pr-3b"], Is.EqualTo(1));
        Assert.That(layout.MaxLanes, Is.EqualTo(2)); // Only used 2 lanes total
    }

    [Test]
    public void Calculate_BranchWithMultipleNodes_StaysInSameLane()
    {
        // Arrange - Main + branch with multiple commits
        var nodes = new List<IGraphNode>
        {
            CreateNode("pr-1", "main"),
            CreateNode("issue-1", "issue-bd-001", parentIds: ["pr-1"]),
            CreateNode("issue-2", "issue-bd-001", parentIds: ["issue-1"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - Both issue nodes should be in the same lane
        Assert.That(layout.LaneAssignments["pr-1"], Is.EqualTo(0));
        Assert.That(layout.LaneAssignments["issue-1"], Is.EqualTo(1));
        Assert.That(layout.LaneAssignments["issue-2"], Is.EqualTo(1));
        Assert.That(layout.MaxLanes, Is.EqualTo(2));
    }

    #endregion

    #region Connector Tests

    [Test]
    public void Calculate_BranchStart_HasConnectorFromParentLane()
    {
        // Arrange
        var nodes = new List<IGraphNode>
        {
            CreateNode("pr-1", "main"),
            CreateNode("pr-2", "feature/test", parentIds: ["pr-1"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - Branch node should have connector from main (lane 0)
        var branchRowInfo = layout.RowInfos[1];
        Assert.That(branchRowInfo.ConnectorFromLane, Is.EqualTo(0));
        Assert.That(branchRowInfo.NodeLane, Is.EqualTo(1));
    }

    [Test]
    public void Calculate_MainBranchNodes_NoConnector()
    {
        // Arrange
        var nodes = new List<IGraphNode>
        {
            CreateNode("pr-1", "main"),
            CreateNode("pr-2", "main", parentIds: ["pr-1"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - Main branch nodes should not have connectors
        Assert.That(layout.RowInfos[0].ConnectorFromLane, Is.Null);
        Assert.That(layout.RowInfos[1].ConnectorFromLane, Is.Null);
    }

    [Test]
    public void Calculate_ContinuingBranch_NoConnector()
    {
        // Arrange - Branch with two nodes
        var nodes = new List<IGraphNode>
        {
            CreateNode("pr-1", "main"),
            CreateNode("issue-1", "issue-bd-001", parentIds: ["pr-1"]),
            CreateNode("issue-2", "issue-bd-001", parentIds: ["issue-1"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - Second issue should not have a connector (continues in same lane)
        Assert.That(layout.RowInfos[1].ConnectorFromLane, Is.EqualTo(0)); // First branch node
        Assert.That(layout.RowInfos[2].ConnectorFromLane, Is.Null); // Continuation
    }

    #endregion

    #region Active Lanes Tests

    [Test]
    public void Calculate_ActiveLanes_IncludesAllActiveBranches()
    {
        // Arrange - Main + two branches with interleaved nodes
        // to test that both lanes are active simultaneously
        var nodes = new List<IGraphNode>
        {
            CreateNode("pr-1", "main"),
            CreateNode("pr-2", "feature/a", parentIds: ["pr-1"]),
            CreateNode("pr-3", "feature/b", parentIds: ["pr-1"]), // Starts while feature/a still active
            CreateNode("pr-2b", "feature/a", parentIds: ["pr-2"]),
            CreateNode("pr-3b", "feature/b", parentIds: ["pr-3"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - Both feature branches should get different lanes since they overlap
        Assert.That(layout.LaneAssignments["pr-2"], Is.EqualTo(1)); // feature/a
        Assert.That(layout.LaneAssignments["pr-3"], Is.EqualTo(2)); // feature/b (can't reuse lane 1 since feature/a not done)

        // Assert - Row with pr-2b should have lanes 0, 1, 2 all active
        var row3Info = layout.RowInfos[3]; // pr-2b
        Assert.That(row3Info.ActiveLanes, Does.Contain(0)); // Main
        Assert.That(row3Info.ActiveLanes, Does.Contain(1)); // feature/a
        Assert.That(row3Info.ActiveLanes, Does.Contain(2)); // feature/b
    }

    [Test]
    public void Calculate_LaneReuse_ReleasedLaneCanBeReused()
    {
        // Arrange - Main + branch that ends, then new branch
        var nodes = new List<IGraphNode>
        {
            CreateNode("pr-1", "main"),
            CreateNode("issue-1", "branch-a", parentIds: ["pr-1"]), // Uses lane 1
            // branch-a ends here
            CreateNode("issue-2", "branch-b", parentIds: ["pr-1"]) // Should reuse lane 1
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - Lane 1 should be reused after branch-a ends
        Assert.That(layout.LaneAssignments["issue-1"], Is.EqualTo(1));
        Assert.That(layout.LaneAssignments["issue-2"], Is.EqualTo(1)); // Reuses lane 1
        Assert.That(layout.MaxLanes, Is.EqualTo(2));
    }

    #endregion

    #region Row Info Tests

    [Test]
    public void Calculate_RowInfos_MatchNodeOrder()
    {
        // Arrange
        var nodes = new List<IGraphNode>
        {
            CreateNode("node-1", "main"),
            CreateNode("node-2", "main", parentIds: ["node-1"]),
            CreateNode("node-3", "feature", parentIds: ["node-2"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - Row infos should match node order
        Assert.That(layout.RowInfos, Has.Count.EqualTo(3));
        Assert.That(layout.RowInfos[0].NodeId, Is.EqualTo("node-1"));
        Assert.That(layout.RowInfos[1].NodeId, Is.EqualTo("node-2"));
        Assert.That(layout.RowInfos[2].NodeId, Is.EqualTo("node-3"));
    }

    [Test]
    public void Calculate_RowInfos_ContainCorrectNodeLanes()
    {
        // Arrange
        var nodes = new List<IGraphNode>
        {
            CreateNode("pr-1", "main"),
            CreateNode("pr-2", "feature", parentIds: ["pr-1"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert
        Assert.That(layout.RowInfos[0].NodeLane, Is.EqualTo(0));
        Assert.That(layout.RowInfos[1].NodeLane, Is.EqualTo(1));
    }

    #endregion

    #region Issue Dependency Tests

    [Test]
    public void Calculate_IssueDependencyChain_ChildrenGetNewLanes()
    {
        // Arrange - Issue chain where each is a separate branch
        // bd-001 -> bd-002 -> bd-003 (each on different branch)
        // Children branch to new lanes to visualize the dependency tree depth
        var nodes = new List<IGraphNode>
        {
            CreateNode("pr-1", "main"),
            CreateNode("issue-bd-001", "issue-bd-001", parentIds: ["pr-1"]),
            CreateNode("issue-bd-002", "issue-bd-002", parentIds: ["issue-bd-001"]),
            CreateNode("issue-bd-003", "issue-bd-003", parentIds: ["issue-bd-002"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - Each child issue gets a new lane to show dependency depth
        Assert.That(layout.LaneAssignments["issue-bd-001"], Is.EqualTo(1));
        // bd-002 gets lane 2 because it's a child of bd-001
        Assert.That(layout.LaneAssignments["issue-bd-002"], Is.EqualTo(2));
        // bd-003 gets lane 3 because it's a child of bd-002
        Assert.That(layout.LaneAssignments["issue-bd-003"], Is.EqualTo(3));

        // Verify connectors come from parent lanes
        Assert.That(layout.RowInfos[1].ConnectorFromLane, Is.EqualTo(0)); // From main
        Assert.That(layout.RowInfos[2].ConnectorFromLane, Is.EqualTo(1)); // From bd-001
        Assert.That(layout.RowInfos[3].ConnectorFromLane, Is.EqualTo(2)); // From bd-002
    }

    [Test]
    public void Calculate_OrphanIssues_ChainedInSameBranch()
    {
        // Arrange - Orphan issues chained on same branch
        var nodes = new List<IGraphNode>
        {
            CreateNode("pr-1", "main"),
            CreateNode("issue-1", "orphan-issues", parentIds: ["pr-1"]),
            CreateNode("issue-2", "orphan-issues", parentIds: ["issue-1"]),
            CreateNode("issue-3", "orphan-issues", parentIds: ["issue-2"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - All orphans should be in the same lane
        Assert.That(layout.LaneAssignments["issue-1"], Is.EqualTo(1));
        Assert.That(layout.LaneAssignments["issue-2"], Is.EqualTo(1));
        Assert.That(layout.LaneAssignments["issue-3"], Is.EqualTo(1));
    }

    #endregion

    #region Sibling Lane Reuse Tests

    [Test]
    public void Calculate_SiblingsWithCompletedSubtrees_ReuseLanes()
    {
        // Arrange - Parent with multiple children where first child's subtree completes
        // before second child is processed. Second child should be able to reuse lane.
        // Structure:
        //   main
        //     └── parent (lane 1)
        //           ├── child-a (lane 2)
        //           │     └── grandchild-a (lane 3)
        //           └── child-b (should reuse lane 2 since child-a's subtree is done)
        var nodes = new List<IGraphNode>
        {
            CreateNode("main-1", "main"),
            CreateNode("parent", "parent-branch", parentIds: ["main-1"]),
            CreateNode("child-a", "child-a-branch", parentIds: ["parent"]),
            CreateNode("grandchild-a", "grandchild-a-branch", parentIds: ["child-a"]),
            // child-a's subtree is now complete
            CreateNode("child-b", "child-b-branch", parentIds: ["parent"]) // Should reuse lane 2
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - child-b should reuse lane 2 since child-a and grandchild-a are done
        Assert.That(layout.LaneAssignments["parent"], Is.EqualTo(1));
        Assert.That(layout.LaneAssignments["child-a"], Is.EqualTo(2));
        Assert.That(layout.LaneAssignments["grandchild-a"], Is.EqualTo(3));
        Assert.That(layout.LaneAssignments["child-b"], Is.EqualTo(2), "child-b should reuse lane 2 after child-a's subtree is complete");
    }

    [Test]
    public void Calculate_DeepHierarchy_ReleasesLanesAfterCompletion()
    {
        // Arrange - Deep hierarchy where lanes should be released progressively
        // main -> A -> B -> C (each on different branches)
        // Then D branches from main - should be able to reuse lane 1
        var nodes = new List<IGraphNode>
        {
            CreateNode("main-1", "main"),
            CreateNode("A", "branch-A", parentIds: ["main-1"]),
            CreateNode("B", "branch-B", parentIds: ["A"]),
            CreateNode("C", "branch-C", parentIds: ["B"]),
            // A, B, C are all done now
            CreateNode("D", "branch-D", parentIds: ["main-1"]) // Should reuse lane 1
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - D should reuse lane 1 since A (and its descendants) are complete
        Assert.That(layout.LaneAssignments["A"], Is.EqualTo(1));
        Assert.That(layout.LaneAssignments["B"], Is.EqualTo(2));
        Assert.That(layout.LaneAssignments["C"], Is.EqualTo(3));
        Assert.That(layout.LaneAssignments["D"], Is.EqualTo(1), "D should reuse lane 1 after A's subtree is complete");
    }

    [Test]
    public void Calculate_MultipleSiblings_ReuseReleasedLanes()
    {
        // Arrange - Three siblings from same parent, each should reuse lanes when possible
        // main
        //   └── parent
        //         ├── sibling-1 (lane 2) - ends
        //         ├── sibling-2 (lane 2, reused) - ends
        //         └── sibling-3 (lane 2, reused)
        var nodes = new List<IGraphNode>
        {
            CreateNode("main-1", "main"),
            CreateNode("parent", "parent-branch", parentIds: ["main-1"]),
            CreateNode("sibling-1", "sibling-1-branch", parentIds: ["parent"]),
            CreateNode("sibling-2", "sibling-2-branch", parentIds: ["parent"]),
            CreateNode("sibling-3", "sibling-3-branch", parentIds: ["parent"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - siblings should use the same lane since each completes before the next
        Assert.That(layout.LaneAssignments["parent"], Is.EqualTo(1));
        // First sibling must use lane 2
        Assert.That(layout.LaneAssignments["sibling-1"], Is.EqualTo(2));
        // Subsequent siblings can reuse lane 2
        Assert.That(layout.LaneAssignments["sibling-2"], Is.EqualTo(2));
        Assert.That(layout.LaneAssignments["sibling-3"], Is.EqualTo(2));
        Assert.That(layout.MaxLanes, Is.EqualTo(3), "Should only need 3 lanes total");
    }

    #endregion

    #region Vertical Line Termination Tests

    [Test]
    public void Calculate_ActiveLanes_ReleasedAfterLastChildProcessed()
    {
        // Arrange - Parent with one child. After child is processed,
        // parent's lane should be released (not in activeLanes for subsequent rows)
        var nodes = new List<IGraphNode>
        {
            CreateNode("main-1", "main"),
            CreateNode("parent", "parent-branch", parentIds: ["main-1"]),
            CreateNode("child", "child-branch", parentIds: ["parent"]),
            CreateNode("main-2", "main", parentIds: ["main-1"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - After child is processed (row 2), parent's lane should be released
        // Row 3 (main-2) should NOT have lane 1 active
        var row3Info = layout.RowInfos[3]; // main-2
        Assert.That(row3Info.ActiveLanes, Does.Not.Contain(1), "Lane 1 (parent) should be released after child completes");
        Assert.That(row3Info.ActiveLanes, Does.Not.Contain(2), "Lane 2 (child) should be released after processing");
        Assert.That(row3Info.ActiveLanes, Does.Contain(0), "Main lane should always be active");
    }

    [Test]
    public void Calculate_ActiveLanes_NotReleasedWhileChildrenPending()
    {
        // Arrange - Parent with two children. Parent's lane should remain active until both children processed.
        // Add a main-2 node after the children to verify lanes are released for subsequent rows.
        var nodes = new List<IGraphNode>
        {
            CreateNode("main-1", "main"),
            CreateNode("parent", "parent-branch", parentIds: ["main-1"]),
            CreateNode("child-1", "child-1-branch", parentIds: ["parent"]),
            CreateNode("child-2", "child-2-branch", parentIds: ["parent"]),
            CreateNode("main-2", "main", parentIds: ["main-1"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - After child-1 is processed (row 2), parent's lane should still be active
        // because child-2 is still pending
        var row2Info = layout.RowInfos[2]; // child-1
        Assert.That(row2Info.ActiveLanes, Does.Contain(1), "Parent's lane 1 should still be active while child-2 is pending");

        // Row 3 (child-2): activeLanes captured BEFORE releases, so lane 1 is still there
        var row3Info = layout.RowInfos[3]; // child-2
        Assert.That(row3Info.ActiveLanes, Does.Contain(1), "Parent's lane 1 is still in activeLanes for its own row");

        // Row 4 (main-2): Now parent's lane should be released because both children are processed
        var row4Info = layout.RowInfos[4]; // main-2
        Assert.That(row4Info.ActiveLanes, Does.Not.Contain(1), "Parent's lane 1 should be released for subsequent rows");
        Assert.That(row4Info.ActiveLanes, Does.Not.Contain(2), "Child's lane 2 should be released for subsequent rows");
    }

    [Test]
    public void Calculate_ComplexHierarchy_LanesReleasedCorrectly()
    {
        // Arrange - Complex hierarchy to verify proper lane release
        //   main
        //     ├── issue-a (lane 1)
        //     │     ├── child-a1 (lane 2)
        //     │     └── child-a2 (lane 2, reused after child-a1)
        //     └── issue-b (lane 1, reused after issue-a subtree)
        var nodes = new List<IGraphNode>
        {
            CreateNode("main-1", "main"),
            CreateNode("issue-a", "issue-a-branch", parentIds: ["main-1"]),
            CreateNode("child-a1", "child-a1-branch", parentIds: ["issue-a"]),
            CreateNode("child-a2", "child-a2-branch", parentIds: ["issue-a"]),
            CreateNode("issue-b", "issue-b-branch", parentIds: ["main-1"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert
        Assert.That(layout.LaneAssignments["issue-a"], Is.EqualTo(1));
        Assert.That(layout.LaneAssignments["child-a1"], Is.EqualTo(2));
        Assert.That(layout.LaneAssignments["child-a2"], Is.EqualTo(2)); // Reuses lane after child-a1
        Assert.That(layout.LaneAssignments["issue-b"], Is.EqualTo(1)); // Reuses lane after issue-a subtree

        // Verify active lanes at each row
        // Row 0 (main-1): lanes 0
        Assert.That(layout.RowInfos[0].ActiveLanes, Is.EquivalentTo(new[] { 0 }));

        // Row 1 (issue-a): lanes 0, 1 (issue-a active with children pending)
        Assert.That(layout.RowInfos[1].ActiveLanes, Is.EquivalentTo(new[] { 0, 1 }));

        // Row 2 (child-a1): lanes 0, 1, 2 (issue-a has another child, child-a1 is active)
        Assert.That(layout.RowInfos[2].ActiveLanes, Is.EquivalentTo(new[] { 0, 1, 2 }));

        // Row 3 (child-a2): lanes 0, 1, 2 (captured before releases - this row still shows the node)
        // After this row, lanes 1 and 2 will be released because issue-a's subtree is complete
        Assert.That(layout.RowInfos[3].ActiveLanes, Is.EquivalentTo(new[] { 0, 1, 2 }));

        // Row 4 (issue-b): lanes 0, 1 (issue-b reuses lane 1)
        Assert.That(layout.RowInfos[4].ActiveLanes, Is.EquivalentTo(new[] { 0, 1 }));
    }

    #endregion

    #region Helper Methods

    private static TestGraphNode CreateNode(
        string id,
        string branchName,
        List<string>? parentIds = null)
    {
        return new TestGraphNode
        {
            Id = id,
            BranchName = branchName,
            ParentIds = parentIds ?? []
        };
    }

    /// <summary>
    /// Test implementation of IGraphNode for testing purposes.
    /// </summary>
    private class TestGraphNode : IGraphNode
    {
        public required string Id { get; init; }
        public string Title => $"Test Node {Id}";
        public GraphNodeType NodeType => Id.StartsWith("issue-") ? GraphNodeType.Issue : GraphNodeType.MergedPullRequest;
        public GraphNodeStatus Status => GraphNodeStatus.Completed;
        public IReadOnlyList<string> ParentIds { get; init; } = [];
        public required string BranchName { get; init; }
        public DateTime SortDate => DateTime.UtcNow;
        public int TimeDimension => 0;
        public string? Url => null;
        public string? Color => "#6b7280";
        public string? Tag => null;
        public int? PullRequestNumber => Id.StartsWith("pr-") ? int.Parse(Id.Replace("pr-", "")) : null;
        public string? IssueId => Id.StartsWith("issue-") ? Id.Replace("issue-", "") : null;
    }

    #endregion
}
