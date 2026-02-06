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
        // With full-subtree release, lanes stay active through entire subtrees.
        var nodes = new List<IGraphNode>
        {
            CreateNode("pr-1", "main"),
            CreateNode("issue-bd-001", "issue-bd-001", parentIds: ["pr-1"]),
            CreateNode("issue-bd-002", "issue-bd-002", parentIds: ["issue-bd-001"]),
            CreateNode("issue-bd-003", "issue-bd-003", parentIds: ["issue-bd-002"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - Each issue needs a new lane since parent lanes stay active through subtrees
        Assert.That(layout.LaneAssignments["issue-bd-001"], Is.EqualTo(1));
        Assert.That(layout.LaneAssignments["issue-bd-002"], Is.EqualTo(2));
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
        // before second child is processed. Second child should be able to reuse lane
        // since child-a's subtree is complete.
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

        // Assert - child-b should reuse lane 2 since child-a's subtree is complete
        // (child-a has no more unprocessed children after grandchild-a)
        Assert.That(layout.LaneAssignments["parent"], Is.EqualTo(1));
        Assert.That(layout.LaneAssignments["child-a"], Is.EqualTo(2));
        Assert.That(layout.LaneAssignments["grandchild-a"], Is.EqualTo(3));
        Assert.That(layout.LaneAssignments["child-b"], Is.EqualTo(2), "child-b should reuse lane 2 after child-a's subtree is complete");
    }

    [Test]
    public void Calculate_DeepHierarchy_ReleasesLanesAfterCompletion()
    {
        // Arrange - Deep hierarchy where lanes stay active through entire subtrees
        // main -> A -> B -> C (each on different branches)
        // With full-subtree release, A's lane stays active until C is processed.
        var nodes = new List<IGraphNode>
        {
            CreateNode("main-1", "main"),
            CreateNode("A", "branch-A", parentIds: ["main-1"]),
            CreateNode("B", "branch-B", parentIds: ["A"]),
            CreateNode("C", "branch-C", parentIds: ["B"]),
            // A, B, C are all done now
            CreateNode("D", "branch-D", parentIds: ["main-1"]) // Should reuse lane 1 after subtree completes
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - Each node needs a new lane while subtree is active
        Assert.That(layout.LaneAssignments["A"], Is.EqualTo(1));
        Assert.That(layout.LaneAssignments["B"], Is.EqualTo(2));
        Assert.That(layout.LaneAssignments["C"], Is.EqualTo(3));
        // D reuses lane 1 after the entire A->B->C subtree is complete
        Assert.That(layout.LaneAssignments["D"], Is.EqualTo(1), "D should reuse lane 1 after subtree completes");
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

    #region Lane Endpoint Tracking Tests

    [Test]
    public void Calculate_SingleNodeBranch_IsFirstAndLastInLane()
    {
        // Arrange - Single node on a side branch (one node, should be both first and last)
        var nodes = new List<IGraphNode>
        {
            CreateNode("main-1", "main"),
            CreateNode("branch-1", "feature-branch", parentIds: ["main-1"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - The branch node should be both first and last in its lane
        var branchRow = layout.RowInfos[1];
        Assert.That(branchRow.IsFirstRowInLane, Is.True, "Single node should be first in lane");
        Assert.That(branchRow.IsLastRowInLane, Is.True, "Single node should be last in lane");
    }

    [Test]
    public void Calculate_MultinodeHierarchy_FirstAndLastFlagsCorrect()
    {
        // Arrange - Branch with multiple nodes
        var nodes = new List<IGraphNode>
        {
            CreateNode("main-1", "main"),
            CreateNode("branch-1", "feature-branch", parentIds: ["main-1"]),
            CreateNode("branch-2", "feature-branch", parentIds: ["branch-1"]),
            CreateNode("branch-3", "feature-branch", parentIds: ["branch-2"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - First node should have IsFirstRowInLane=true, last should have IsLastRowInLane=true
        Assert.That(layout.RowInfos[1].IsFirstRowInLane, Is.True, "First branch node should be first in lane");
        Assert.That(layout.RowInfos[1].IsLastRowInLane, Is.False, "First branch node should not be last in lane");

        Assert.That(layout.RowInfos[2].IsFirstRowInLane, Is.False, "Middle node should not be first in lane");
        Assert.That(layout.RowInfos[2].IsLastRowInLane, Is.False, "Middle node should not be last in lane");

        Assert.That(layout.RowInfos[3].IsFirstRowInLane, Is.False, "Last branch node should not be first in lane");
        Assert.That(layout.RowInfos[3].IsLastRowInLane, Is.True, "Last branch node should be last in lane");
    }

    [Test]
    public void Calculate_MainBranch_NeverFirstOrLast()
    {
        // Arrange - Main branch continues forever conceptually
        var nodes = new List<IGraphNode>
        {
            CreateNode("main-1", "main"),
            CreateNode("main-2", "main", parentIds: ["main-1"]),
            CreateNode("main-3", "main", parentIds: ["main-2"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - Main branch (lane 0) should never have first/last flags
        // because it conceptually continues forever
        Assert.That(layout.RowInfos[0].IsFirstRowInLane, Is.False, "Main lane should never be first");
        Assert.That(layout.RowInfos[0].IsLastRowInLane, Is.False, "Main lane should never be last");
        Assert.That(layout.RowInfos[1].IsFirstRowInLane, Is.False, "Main lane should never be first");
        Assert.That(layout.RowInfos[1].IsLastRowInLane, Is.False, "Main lane should never be last");
        Assert.That(layout.RowInfos[2].IsFirstRowInLane, Is.False, "Main lane should never be first");
        Assert.That(layout.RowInfos[2].IsLastRowInLane, Is.False, "Main lane should never be last");
    }

    [Test]
    public void Calculate_BranchWithConnector_HasIsFirstRowInLane()
    {
        // Arrange - Branch node with connector from main
        var nodes = new List<IGraphNode>
        {
            CreateNode("main-1", "main"),
            CreateNode("branch-1", "feature-branch", parentIds: ["main-1"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - Branch node has both connector AND is first in lane
        var branchRow = layout.RowInfos[1];
        Assert.That(branchRow.ConnectorFromLane, Is.EqualTo(0), "Should have connector from main");
        Assert.That(branchRow.IsFirstRowInLane, Is.True, "Should be first in lane");
    }

    [Test]
    public void Calculate_LaneReuse_NewBranchIsFirstInLane()
    {
        // Arrange - First branch completes, second branch reuses the lane
        var nodes = new List<IGraphNode>
        {
            CreateNode("main-1", "main"),
            CreateNode("branch-a", "branch-a", parentIds: ["main-1"]),  // Uses lane 1, completes
            CreateNode("branch-b", "branch-b", parentIds: ["main-1"])   // Reuses lane 1
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - Both branches should be first in their lane (even though same lane number)
        Assert.That(layout.RowInfos[1].IsFirstRowInLane, Is.True, "branch-a should be first in lane 1");
        Assert.That(layout.RowInfos[1].IsLastRowInLane, Is.True, "branch-a should be last in lane 1");
        Assert.That(layout.RowInfos[2].IsFirstRowInLane, Is.True, "branch-b should be first in lane 1 after reuse");
        Assert.That(layout.RowInfos[2].IsLastRowInLane, Is.True, "branch-b should be last in lane 1");
    }

    #endregion

    #region Pass-Through Lane Ending Tests

    [Test]
    public void Calculate_PassThroughLanes_EndAtCorrectRow()
    {
        // Arrange - Parent with child where parent's lane passes through child row
        // After child is processed, parent's lane should end
        // main -> parent -> child -> orphan (after subtree)
        var nodes = new List<IGraphNode>
        {
            CreateNode("main-1", "main"),
            CreateNode("parent", "parent-branch", parentIds: ["main-1"]),
            CreateNode("child", "child-branch", parentIds: ["parent"]),
            CreateNode("orphan", "orphan-branch", parentIds: ["main-1"])  // After subtree
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - At child row, parent's lane (1) should be in LanesEndingAtThisRow
        // because the parent's subtree completes at this row
        var childRow = layout.RowInfos[2]; // child is at index 2
        Assert.That(childRow.LanesEndingAtThisRow, Does.Contain(1), "Parent's lane 1 should end at child row");
        Assert.That(childRow.LanesEndingAtThisRow, Does.Contain(2), "Child's lane 2 should also end at this row");

        // Orphan row should not have any pass-through lanes ending (only lane 0 is active)
        var orphanRow = layout.RowInfos[3];
        Assert.That(orphanRow.LanesEndingAtThisRow, Is.Empty.Or.EqualTo(new HashSet<int> { 1 }), "Orphan row should not have pass-through lanes ending");
    }

    [Test]
    public void Calculate_LanesEndingAtThisRow_EmptyForMainBranch()
    {
        // Arrange - Main branch only
        var nodes = new List<IGraphNode>
        {
            CreateNode("main-1", "main"),
            CreateNode("main-2", "main", parentIds: ["main-1"]),
            CreateNode("main-3", "main", parentIds: ["main-2"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - Main branch (lane 0) never ends
        foreach (var row in layout.RowInfos)
        {
            Assert.That(row.LanesEndingAtThisRow, Does.Not.Contain(0), "Lane 0 (main) should never be in LanesEndingAtThisRow");
        }
    }

    [Test]
    public void Calculate_LanesEndingAtThisRow_SingleNodeBranch()
    {
        // Arrange - Single node branch ends at its own row
        var nodes = new List<IGraphNode>
        {
            CreateNode("main-1", "main"),
            CreateNode("branch-1", "feature-branch", parentIds: ["main-1"]),
            CreateNode("main-2", "main", parentIds: ["main-1"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - The single branch node's lane should end at its row
        var branchRow = layout.RowInfos[1];
        Assert.That(branchRow.LanesEndingAtThisRow, Does.Contain(1), "Single node branch lane should end at its row");
    }

    [Test]
    public void Calculate_LanesEndingAtThisRow_DeepHierarchy()
    {
        // Arrange - Deep hierarchy: main -> A -> B -> C
        // With full-subtree release, lanes stay active through entire subtrees:
        // All lanes (1, 2, 3) end at C's row when the entire subtree completes
        var nodes = new List<IGraphNode>
        {
            CreateNode("main-1", "main"),
            CreateNode("A", "branch-A", parentIds: ["main-1"]),
            CreateNode("B", "branch-B", parentIds: ["A"]),
            CreateNode("C", "branch-C", parentIds: ["B"]),
            CreateNode("main-2", "main", parentIds: ["main-1"])  // After subtree
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - Lane assignments with full-subtree release
        Assert.That(layout.LaneAssignments["A"], Is.EqualTo(1));
        Assert.That(layout.LaneAssignments["B"], Is.EqualTo(2));
        Assert.That(layout.LaneAssignments["C"], Is.EqualTo(3));

        // At B's row, no lanes should end yet (A's subtree continues with C)
        var bRow = layout.RowInfos[2]; // B is at index 2
        Assert.That(bRow.LanesEndingAtThisRow, Does.Not.Contain(1), "Lane 1 (A) should not end at B's row");

        // At C's row, all lanes (1, 2, 3) should end as subtree completes
        var cRow = layout.RowInfos[3]; // C is at index 3
        Assert.That(cRow.LanesEndingAtThisRow, Does.Contain(1), "Lane 1 (A) should end at C's row");
        Assert.That(cRow.LanesEndingAtThisRow, Does.Contain(2), "Lane 2 (B) should end at C's row");
        Assert.That(cRow.LanesEndingAtThisRow, Does.Contain(3), "Lane 3 (C) should end at C's row");

        // main-2 row should not have any lanes ending
        var main2Row = layout.RowInfos[4];
        Assert.That(main2Row.LanesEndingAtThisRow, Is.Empty, "No lanes should end at main-2 row");
    }

    #endregion

    #region Reserved Lanes Tests

    [Test]
    public void Calculate_ReservedLanes_EmptyWhenDirectChildrenPending()
    {
        // Arrange - Parent with children still pending
        var nodes = new List<IGraphNode>
        {
            CreateNode("main-1", "main"),
            CreateNode("parent", "parent-branch", parentIds: ["main-1"]),
            CreateNode("child", "child-branch", parentIds: ["parent"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - At parent row, no reserved lanes (parent just started, has children pending)
        var parentRow = layout.RowInfos[1];
        Assert.That(parentRow.ReservedLanes, Does.Not.Contain(1), "Parent's lane should not be reserved while child is pending");
    }

    [Test]
    public void Calculate_ReservedLanes_SetWhenOnlyDescendantsRemain()
    {
        // Arrange - Parent -> Child -> Grandchild
        // At grandchild row, parent's lane should be reserved (child processed, but subtree continues)
        var nodes = new List<IGraphNode>
        {
            CreateNode("main-1", "main"),
            CreateNode("parent", "parent-branch", parentIds: ["main-1"]),
            CreateNode("child", "child-branch", parentIds: ["parent"]),
            CreateNode("grandchild", "grandchild-branch", parentIds: ["child"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - At grandchild row, parent's lane (1) should be reserved
        // because parent's direct child (child) is processed, but subtree continues
        var grandchildRow = layout.RowInfos[3];
        Assert.That(grandchildRow.ReservedLanes, Does.Contain(1), "Parent's lane should be reserved when only grandchildren remain");

        // Child's lane (2) should also be reserved at grandchild row
        // because child's direct child (grandchild) is being processed now
        // Actually, at the moment grandchild is captured, child still has unprocessed children
        // So child's lane should NOT be reserved
        Assert.That(grandchildRow.ReservedLanes, Does.Not.Contain(2), "Child's lane should not be reserved while grandchild is processing");
    }

    [Test]
    public void Calculate_ReservedLanes_NotSetForMainBranch()
    {
        // Arrange - Main branch should never be reserved
        var nodes = new List<IGraphNode>
        {
            CreateNode("main-1", "main"),
            CreateNode("parent", "parent-branch", parentIds: ["main-1"]),
            CreateNode("child", "child-branch", parentIds: ["parent"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert - Main branch (lane 0) is never reserved
        foreach (var row in layout.RowInfos)
        {
            Assert.That(row.ReservedLanes, Does.Not.Contain(0), "Main branch should never be reserved");
        }
    }

    [Test]
    public void Calculate_ReservedLanes_DeepHierarchy()
    {
        // Arrange - main -> A -> B -> C -> D
        // At D's row, A and B should be reserved, C should not be
        var nodes = new List<IGraphNode>
        {
            CreateNode("main-1", "main"),
            CreateNode("A", "branch-A", parentIds: ["main-1"]),
            CreateNode("B", "branch-B", parentIds: ["A"]),
            CreateNode("C", "branch-C", parentIds: ["B"]),
            CreateNode("D", "branch-D", parentIds: ["C"])
        };

        // Act
        var layout = _calculator.Calculate(nodes);

        // Assert lane assignments
        Assert.That(layout.LaneAssignments["A"], Is.EqualTo(1));
        Assert.That(layout.LaneAssignments["B"], Is.EqualTo(2));
        Assert.That(layout.LaneAssignments["C"], Is.EqualTo(3));
        Assert.That(layout.LaneAssignments["D"], Is.EqualTo(4));

        // At C's row, A's lane should be reserved (only has grandchildren now)
        var cRow = layout.RowInfos[3];
        Assert.That(cRow.ReservedLanes, Does.Contain(1), "A's lane should be reserved at C's row");
        Assert.That(cRow.ReservedLanes, Does.Not.Contain(2), "B's lane should not be reserved at C's row");

        // At D's row, A's and B's lanes should be reserved
        var dRow = layout.RowInfos[4];
        Assert.That(dRow.ReservedLanes, Does.Contain(1), "A's lane should be reserved at D's row");
        Assert.That(dRow.ReservedLanes, Does.Contain(2), "B's lane should be reserved at D's row");
        Assert.That(dRow.ReservedLanes, Does.Not.Contain(3), "C's lane should not be reserved at D's row");
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
        public bool? HasDescription => null;
    }

    #endregion
}
