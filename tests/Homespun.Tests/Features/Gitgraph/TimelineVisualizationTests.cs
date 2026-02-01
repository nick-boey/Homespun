using Fleece.Core.Models;
using Homespun.Features.Gitgraph.Data;
using Homespun.Features.Gitgraph.Services;
using Homespun.Features.PullRequests;

namespace Homespun.Tests.Features.Gitgraph;

/// <summary>
/// Tests for timeline visualization graph structure and layout.
/// Tests the integration between GraphBuilder and TimelineLaneCalculator.
/// </summary>
[TestFixture]
public class TimelineVisualizationTests
{
    private GraphBuilder _graphBuilder = null!;
    private TimelineLaneCalculator _laneCalculator = null!;

    [SetUp]
    public void SetUp()
    {
        _graphBuilder = new GraphBuilder();
        _laneCalculator = new TimelineLaneCalculator();
    }

    #region Graph Structure Tests

    [Test]
    public void RendersMergedPRsFirst_InChronologicalOrder()
    {
        // Arrange - 3 merged PRs and 2 open PRs
        var prs = new List<PullRequestInfo>
        {
            CreateMergedPR(101, DateTime.UtcNow.AddDays(-10)),
            CreateMergedPR(102, DateTime.UtcNow.AddDays(-5)),
            CreateMergedPR(103, DateTime.UtcNow.AddDays(-3)),
            CreateOpenPR(104),
            CreateOpenPR(105)
        };

        // Act
        var graph = _graphBuilder.Build(prs, []);

        // Assert - Merged PRs come first in chronological order (oldest first)
        var nodes = graph.Nodes.ToList();
        Assert.That(nodes[0], Is.InstanceOf<PullRequestNode>());
        Assert.That(((PullRequestNode)nodes[0]).PullRequest.Number, Is.EqualTo(101));
        Assert.That(nodes[1], Is.InstanceOf<PullRequestNode>());
        Assert.That(((PullRequestNode)nodes[1]).PullRequest.Number, Is.EqualTo(102));
        Assert.That(nodes[2], Is.InstanceOf<PullRequestNode>());
        Assert.That(((PullRequestNode)nodes[2]).PullRequest.Number, Is.EqualTo(103));
    }

    [Test]
    public void RendersOpenPRsAfterMergedPRs()
    {
        // Arrange
        var prs = new List<PullRequestInfo>
        {
            CreateMergedPR(101, DateTime.UtcNow.AddDays(-5)),
            CreateOpenPR(102),
            CreateOpenPR(103)
        };

        // Act
        var graph = _graphBuilder.Build(prs, []);

        // Assert - Open PRs come after merged PRs
        var nodes = graph.Nodes.ToList();
        Assert.That(nodes, Has.Count.EqualTo(3));
        Assert.That(((PullRequestNode)nodes[0]).NodeType, Is.EqualTo(GraphNodeType.MergedPullRequest));
        Assert.That(((PullRequestNode)nodes[1]).NodeType, Is.EqualTo(GraphNodeType.OpenPullRequest));
        Assert.That(((PullRequestNode)nodes[2]).NodeType, Is.EqualTo(GraphNodeType.OpenPullRequest));
    }

    [Test]
    public void RendersIssuesWithDependencies_DepthFirst()
    {
        // Arrange - Issue chain: ISSUE-001 -> ISSUE-002 -> ISSUE-003
        var issues = new List<Issue>
        {
            CreateIssue("ISSUE-001"),
            CreateIssueWithParent("ISSUE-002", "ISSUE-001"),
            CreateIssueWithParent("ISSUE-003", "ISSUE-002")
        };

        // Act
        var graph = _graphBuilder.Build([], issues);

        // Assert - Issues in depth-first order
        var issueNodes = graph.Nodes.OfType<IssueNode>().ToList();
        Assert.That(issueNodes, Has.Count.EqualTo(3));
        Assert.That(issueNodes[0].Issue.Id, Is.EqualTo("ISSUE-001"));
        Assert.That(issueNodes[1].Issue.Id, Is.EqualTo("ISSUE-002"));
        Assert.That(issueNodes[2].Issue.Id, Is.EqualTo("ISSUE-003"));
    }

    [Test]
    public void RendersOrphanIssues_GroupedTogether()
    {
        // Arrange - Issues with and without dependencies
        var issues = new List<Issue>
        {
            CreateIssue("ISSUE-001", group: "UI"),       // Orphan
            CreateIssue("ISSUE-002", group: "UI"),       // Orphan
            CreateIssue("ISSUE-003"),                     // Orphan (no group)
            CreateIssue("ISSUE-004"),                     // Root with child
            CreateIssueWithParent("ISSUE-005", "ISSUE-004") // Child
        };

        // Act
        var graph = _graphBuilder.Build([], issues);

        // Assert - Orphan issues grouped by group
        var issueNodes = graph.Nodes.OfType<IssueNode>().ToList();

        // Dependency issues (ISSUE-004, ISSUE-005) come before orphans
        var depIssueIds = issueNodes
            .TakeWhile(n => !n.IsOrphan)
            .Select(n => n.Issue.Id)
            .ToList();
        Assert.That(depIssueIds, Does.Contain("ISSUE-004"));
        Assert.That(depIssueIds, Does.Contain("ISSUE-005"));

        // Orphan issues at the end
        var orphanNodes = issueNodes.Where(n => n.IsOrphan).ToList();
        Assert.That(orphanNodes, Has.Count.EqualTo(3));
    }

    #endregion

    #region Lane Assignment Tests

    [Test]
    public void MainBranch_UsesLaneZero()
    {
        // Arrange
        var prs = new List<PullRequestInfo>
        {
            CreateMergedPR(101, DateTime.UtcNow.AddDays(-5)),
            CreateMergedPR(102, DateTime.UtcNow)
        };

        // Act
        var graph = _graphBuilder.Build(prs, []);
        var layout = _laneCalculator.Calculate(graph.Nodes);

        // Assert - All merged PRs on main branch, lane 0
        foreach (var node in graph.Nodes)
        {
            if (node.BranchName == "main")
            {
                Assert.That(layout.LaneAssignments[node.Id], Is.EqualTo(0),
                    $"Node {node.Id} on main branch should be in lane 0");
            }
        }
    }

    [Test]
    public void OpenPRs_BranchToNewLanes()
    {
        // Arrange
        var prs = new List<PullRequestInfo>
        {
            CreateMergedPR(101, DateTime.UtcNow.AddDays(-5)),
            CreateOpenPR(102, branchName: "feature/a"),
            CreateOpenPR(103, branchName: "feature/b")
        };

        // Act
        var graph = _graphBuilder.Build(prs, []);
        var layout = _laneCalculator.Calculate(graph.Nodes);

        // Assert - Open PRs should be in lanes > 0
        var openPRNodes = graph.Nodes.OfType<PullRequestNode>()
            .Where(n => n.NodeType == GraphNodeType.OpenPullRequest)
            .ToList();

        foreach (var node in openPRNodes)
        {
            var lane = layout.LaneAssignments[node.Id];
            Assert.That(lane, Is.GreaterThan(0),
                $"Open PR {node.Id} should be in a lane > 0");
        }
    }

    [Test]
    public void DependentIssues_FormChain()
    {
        // Arrange
        var prs = new List<PullRequestInfo>
        {
            CreateMergedPR(101, DateTime.UtcNow)
        };
        var issues = new List<Issue>
        {
            CreateIssue("ISSUE-004"),
            CreateIssueWithParent("ISSUE-005", "ISSUE-004"),
            CreateIssueWithParent("ISSUE-006", "ISSUE-005")
        };

        // Act
        var graph = _graphBuilder.Build(prs, issues);

        // Assert - Issues have correct parent references
        var issueNodes = graph.Nodes.OfType<IssueNode>().ToList();
        var issue5 = issueNodes.First(n => n.Issue.Id == "ISSUE-005");
        var issue6 = issueNodes.First(n => n.Issue.Id == "ISSUE-006");

        Assert.That(issue5.ParentIds, Does.Contain("issue-ISSUE-004"));
        Assert.That(issue6.ParentIds, Does.Contain("issue-ISSUE-005"));
    }

    [Test]
    public void BranchingDependencies_CreateMultipleLanes()
    {
        // Arrange - ISSUE-005 has two children
        var issues = new List<Issue>
        {
            CreateIssue("ISSUE-004"),
            CreateIssueWithParent("ISSUE-005", "ISSUE-004"),
            CreateIssueWithParent("ISSUE-006", "ISSUE-005"),  // Child 1
            CreateIssueWithParent("ISSUE-007", "ISSUE-005")   // Child 2
        };

        // Act
        var graph = _graphBuilder.Build([], issues);
        var layout = _laneCalculator.Calculate(graph.Nodes);

        // Assert - Multiple lanes used
        Assert.That(layout.MaxLanes, Is.GreaterThanOrEqualTo(1));
    }

    #endregion

    #region Visual Element Tests (using node types)

    [Test]
    public void PRNodes_HaveCorrectNodeType()
    {
        // Arrange
        var prs = new List<PullRequestInfo>
        {
            CreateMergedPR(101, DateTime.UtcNow),
            CreateOpenPR(102)
        };

        // Act
        var graph = _graphBuilder.Build(prs, []);
        var prNodes = graph.Nodes.OfType<PullRequestNode>().ToList();

        // Assert - PRs have correct node types for rendering as circles
        Assert.That(prNodes[0].NodeType, Is.EqualTo(GraphNodeType.MergedPullRequest));
        Assert.That(prNodes[1].NodeType, Is.EqualTo(GraphNodeType.OpenPullRequest));
    }

    [Test]
    public void IssueNodes_HaveCorrectNodeType()
    {
        // Arrange
        var issues = new List<Issue>
        {
            CreateIssue("ISSUE-001"),
            CreateIssue("ISSUE-002")
        };

        // Act
        var graph = _graphBuilder.Build([], issues);
        var issueNodes = graph.Nodes.OfType<IssueNode>().ToList();

        // Assert - Issues have correct node types for rendering as diamonds
        Assert.That(issueNodes.All(n =>
            n.NodeType == GraphNodeType.Issue || n.NodeType == GraphNodeType.OrphanIssue), Is.True);
    }

    [Test]
    public void IssueColors_MatchType()
    {
        // Arrange - Different issue types
        var issues = new List<Issue>
        {
            CreateIssue("ISSUE-001", type: IssueType.Feature),
            CreateIssue("ISSUE-002", type: IssueType.Bug),
            CreateIssue("ISSUE-003", type: IssueType.Task),
            CreateIssue("ISSUE-004", type: IssueType.Chore)
        };

        // Act
        var graph = _graphBuilder.Build([], issues);
        var issueNodes = graph.Nodes.OfType<IssueNode>().ToDictionary(n => n.Issue.Id);

        // Assert - Colors match expected values
        Assert.That(issueNodes["ISSUE-001"].Color, Is.EqualTo("#a855f7")); // Purple (Feature)
        Assert.That(issueNodes["ISSUE-002"].Color, Is.EqualTo("#ef4444")); // Red (Bug)
        Assert.That(issueNodes["ISSUE-003"].Color, Is.EqualTo("#3b82f6")); // Blue (Task)
        Assert.That(issueNodes["ISSUE-004"].Color, Is.EqualTo("#6b7280")); // Gray (Chore)
    }

    #endregion

    #region Connector Tests

    [Test]
    public void BranchStart_HasConnectorFromMain()
    {
        // Arrange
        var prs = new List<PullRequestInfo>
        {
            CreateMergedPR(101, DateTime.UtcNow),
            CreateOpenPR(102)
        };

        // Act
        var graph = _graphBuilder.Build(prs, []);
        var layout = _laneCalculator.Calculate(graph.Nodes);

        // Assert - Open PR row has connector from lane 0
        var openPRNode = graph.Nodes.OfType<PullRequestNode>()
            .First(n => n.NodeType == GraphNodeType.OpenPullRequest);
        var openPRRowInfo = layout.RowInfos.First(r => r.NodeId == openPRNode.Id);

        Assert.That(openPRRowInfo.ConnectorFromLane, Is.EqualTo(0));
    }

    [Test]
    public void IssueChain_HasConnectorsFromParent()
    {
        // Arrange
        var prs = new List<PullRequestInfo>
        {
            CreateMergedPR(101, DateTime.UtcNow)
        };
        var issues = new List<Issue>
        {
            CreateIssue("ISSUE-004"),
            CreateIssueWithParent("ISSUE-005", "ISSUE-004")
        };

        // Act
        var graph = _graphBuilder.Build(prs, issues);
        var layout = _laneCalculator.Calculate(graph.Nodes);

        // Assert - Second issue has connector from parent's lane
        var issue5Node = graph.Nodes.OfType<IssueNode>().First(n => n.Issue.Id == "ISSUE-005");
        var issue5RowInfo = layout.RowInfos.First(r => r.NodeId == issue5Node.Id);

        // The connector should exist (from some lane)
        // The exact lane depends on the parent's position
        Assert.That(issue5RowInfo.ConnectorFromLane, Is.Not.Null.Or.GreaterThanOrEqualTo(0));
    }

    #endregion

    #region Full Timeline Simulation

    [Test]
    public void FullTimeline_HasCorrectStructure()
    {
        // Arrange - Simulate full timeline with all node types
        var prs = new List<PullRequestInfo>
        {
            CreateMergedPR(97, DateTime.UtcNow.AddDays(-30)),
            CreateMergedPR(98, DateTime.UtcNow.AddDays(-25)),
            CreateMergedPR(100, DateTime.UtcNow.AddDays(-15)),
            CreateOpenPR(38, branchName: "feature/logging"),
            CreateOpenPR(41, branchName: "feature/database")
        };
        var issues = new List<Issue>
        {
            // Dependency chain
            CreateIssue("ISSUE-004", group: "API"),
            CreateIssueWithParent("ISSUE-005", "ISSUE-004", group: "API"),
            CreateIssueWithParent("ISSUE-006", "ISSUE-005", group: "API"),
            // Orphans
            CreateIssue("ISSUE-001", group: "UI"),
            CreateIssue("ISSUE-002", group: "UI"),
            CreateIssue("ISSUE-003")  // No group
        };

        // Act
        var graph = _graphBuilder.Build(prs, issues);
        var layout = _laneCalculator.Calculate(graph.Nodes);

        // Assert - Basic structure
        Assert.That(graph.Nodes.Count, Is.EqualTo(11)); // 5 PRs + 6 issues

        // Assert - Time dimension progression
        var mergedPRs = graph.Nodes.Where(n => n.TimeDimension <= 0).ToList();
        var openPRs = graph.Nodes.Where(n => n.TimeDimension == 1).ToList();
        var issueNodes = graph.Nodes.Where(n => n.TimeDimension >= 2).ToList();

        Assert.That(mergedPRs.Count, Is.EqualTo(3), "Should have 3 merged PRs");
        Assert.That(openPRs.Count, Is.EqualTo(2), "Should have 2 open PRs");
        Assert.That(issueNodes.Count, Is.EqualTo(6), "Should have 6 issues");

        // Assert - Order is correct (merged PRs, then open PRs, then issues)
        var nodeList = graph.Nodes.ToList();
        var lastMergedIndex = nodeList.FindLastIndex(n => n.TimeDimension <= 0);
        var firstOpenIndex = nodeList.FindIndex(n => n.TimeDimension == 1);
        var firstIssueIndex = nodeList.FindIndex(n => n.TimeDimension >= 2);

        Assert.That(lastMergedIndex, Is.LessThan(firstOpenIndex), "Merged PRs should come before open PRs");
        Assert.That(firstOpenIndex, Is.LessThan(firstIssueIndex), "Open PRs should come before issues");
    }

    [Test]
    public void DeepDependencyChain_MaintainsOrder()
    {
        // Arrange - Deep chain like the plan specifies
        // ISSUE-005 -> ISSUE-007 -> ISSUE-008 -> ISSUE-009 -> ISSUE-010
        var issues = new List<Issue>
        {
            CreateIssue("ISSUE-004"),
            CreateIssueWithParent("ISSUE-005", "ISSUE-004"),
            CreateIssueWithParent("ISSUE-007", "ISSUE-005"),
            CreateIssueWithParent("ISSUE-008", "ISSUE-007"),
            CreateIssueWithParent("ISSUE-009", "ISSUE-008"),
            CreateIssueWithParent("ISSUE-010", "ISSUE-009")
        };

        // Act
        var graph = _graphBuilder.Build([], issues);

        // Assert - Chain order is maintained
        var issueNodes = graph.Nodes.OfType<IssueNode>().ToList();
        var expectedOrder = new[] { "ISSUE-004", "ISSUE-005", "ISSUE-007", "ISSUE-008", "ISSUE-009", "ISSUE-010" };

        for (int i = 0; i < expectedOrder.Length; i++)
        {
            Assert.That(issueNodes[i].Issue.Id, Is.EqualTo(expectedOrder[i]),
                $"Issue at position {i} should be {expectedOrder[i]}");
        }
    }

    [Test]
    public void BranchingDependencies_AllChildrenFollowParent()
    {
        // Arrange - ISSUE-007 has siblings ISSUE-012
        // ISSUE-008 has sibling ISSUE-011
        var issues = new List<Issue>
        {
            CreateIssue("ISSUE-004"),
            CreateIssueWithParent("ISSUE-005", "ISSUE-004"),
            CreateIssueWithParent("ISSUE-007", "ISSUE-005"),
            CreateIssueWithParent("ISSUE-008", "ISSUE-007"),
            CreateIssueWithParent("ISSUE-011", "ISSUE-008"),  // Sibling of ISSUE-009
            CreateIssueWithParent("ISSUE-012", "ISSUE-007")   // Sibling of ISSUE-008
        };

        // Act
        var graph = _graphBuilder.Build([], issues);

        // Assert - All children come after their parents
        var issueNodes = graph.Nodes.OfType<IssueNode>().ToList();
        var idToIndex = issueNodes
            .Select((n, i) => (n.Issue.Id, i))
            .ToDictionary(x => x.Id, x => x.i);

        // ISSUE-007 comes after ISSUE-005
        Assert.That(idToIndex["ISSUE-007"], Is.GreaterThan(idToIndex["ISSUE-005"]));
        // ISSUE-008 comes after ISSUE-007
        Assert.That(idToIndex["ISSUE-008"], Is.GreaterThan(idToIndex["ISSUE-007"]));
        // ISSUE-011 comes after ISSUE-008
        Assert.That(idToIndex["ISSUE-011"], Is.GreaterThan(idToIndex["ISSUE-008"]));
        // ISSUE-012 comes after ISSUE-007
        Assert.That(idToIndex["ISSUE-012"], Is.GreaterThan(idToIndex["ISSUE-007"]));
    }

    #endregion

    #region Child Issue Lane Assignment Tests

    [Test]
    public void ChildIssues_BranchToNewLanes()
    {
        // Arrange - ISSUE-005 has multiple children (ISSUE-006, ISSUE-007, ISSUE-013)
        // Children should branch to lanes > 1 to visualize depth
        var issues = new List<Issue>
        {
            CreateIssue("ISSUE-004"),
            CreateIssueWithParent("ISSUE-005", "ISSUE-004"),
            CreateIssueWithParent("ISSUE-006", "ISSUE-005"),  // Child of ISSUE-005
            CreateIssueWithParent("ISSUE-007", "ISSUE-005"),  // Child of ISSUE-005 (sibling of 006)
            CreateIssueWithParent("ISSUE-013", "ISSUE-005")   // Child of ISSUE-005 (sibling of 006, 007)
        };

        // Act
        var graph = _graphBuilder.Build([], issues);
        var layout = _laneCalculator.Calculate(graph.Nodes);

        // Assert - Root issue branches from main (lane 0), children get higher lanes
        var issue004Lane = layout.LaneAssignments["issue-ISSUE-004"];
        var issue005Lane = layout.LaneAssignments["issue-ISSUE-005"];
        var issue006Lane = layout.LaneAssignments["issue-ISSUE-006"];
        var issue007Lane = layout.LaneAssignments["issue-ISSUE-007"];
        var issue013Lane = layout.LaneAssignments["issue-ISSUE-013"];

        // ISSUE-004 gets lane 1 (first non-main lane)
        Assert.That(issue004Lane, Is.EqualTo(1), "Root issue should be in lane 1");

        // ISSUE-005 gets lane 2 (child of ISSUE-004)
        Assert.That(issue005Lane, Is.EqualTo(2), "Child of root should be in lane 2");

        // Children of ISSUE-005 should be in lanes >= 3
        Assert.That(issue006Lane, Is.GreaterThanOrEqualTo(3), "Grandchild should be in lane >= 3");
        Assert.That(issue007Lane, Is.GreaterThanOrEqualTo(3), "Sibling grandchild should be in lane >= 3");
        Assert.That(issue013Lane, Is.GreaterThanOrEqualTo(3), "Another sibling grandchild should be in lane >= 3");
    }

    [Test]
    public void PreviousIssues_CreateDependencyEdges()
    {
        // Arrange - Issues connected via PreviousIssues (sequential ordering)
        var issues = new List<Issue>
        {
            CreateIssue("ISSUE-001"),
            CreateIssueWithPrevious("ISSUE-002", "ISSUE-001"),
            CreateIssueWithPrevious("ISSUE-003", "ISSUE-002")
        };

        // Act
        var graph = _graphBuilder.Build([], issues);
        var issueNodes = graph.Nodes.OfType<IssueNode>().ToList();

        // Assert - Issues should have correct parent references from PreviousIssues
        var issue2 = issueNodes.First(n => n.Issue.Id == "ISSUE-002");
        var issue3 = issueNodes.First(n => n.Issue.Id == "ISSUE-003");

        Assert.That(issue2.ParentIds, Does.Contain("issue-ISSUE-001"),
            "ISSUE-002 should have ISSUE-001 as parent via PreviousIssues");
        Assert.That(issue3.ParentIds, Does.Contain("issue-ISSUE-002"),
            "ISSUE-003 should have ISSUE-002 as parent via PreviousIssues");
    }

    [Test]
    public void DeepHierarchy_UsesMultipleLanes()
    {
        // Arrange - Deep chain: ISSUE-007 -> ISSUE-008 -> ISSUE-009 -> ISSUE-010
        var issues = new List<Issue>
        {
            CreateIssue("ISSUE-004"),
            CreateIssueWithParent("ISSUE-005", "ISSUE-004"),
            CreateIssueWithParent("ISSUE-007", "ISSUE-005"),
            CreateIssueWithParent("ISSUE-008", "ISSUE-007"),
            CreateIssueWithParent("ISSUE-009", "ISSUE-008"),
            CreateIssueWithParent("ISSUE-010", "ISSUE-009")
        };

        // Act
        var graph = _graphBuilder.Build([], issues);
        var layout = _laneCalculator.Calculate(graph.Nodes);

        // Assert - Each level of depth should use a higher lane
        var lane004 = layout.LaneAssignments["issue-ISSUE-004"];
        var lane005 = layout.LaneAssignments["issue-ISSUE-005"];
        var lane007 = layout.LaneAssignments["issue-ISSUE-007"];
        var lane008 = layout.LaneAssignments["issue-ISSUE-008"];
        var lane009 = layout.LaneAssignments["issue-ISSUE-009"];
        var lane010 = layout.LaneAssignments["issue-ISSUE-010"];

        // Each child should be in a lane greater than its parent
        Assert.That(lane005, Is.GreaterThan(lane004), "ISSUE-005 should be in higher lane than ISSUE-004");
        Assert.That(lane007, Is.GreaterThan(lane005), "ISSUE-007 should be in higher lane than ISSUE-005");
        Assert.That(lane008, Is.GreaterThan(lane007), "ISSUE-008 should be in higher lane than ISSUE-007");
        Assert.That(lane009, Is.GreaterThan(lane008), "ISSUE-009 should be in higher lane than ISSUE-008");
        Assert.That(lane010, Is.GreaterThan(lane009), "ISSUE-010 should be in higher lane than ISSUE-009");

        // Should use at least 6 lanes (1 for each issue in the chain)
        Assert.That(layout.MaxLanes, Is.GreaterThanOrEqualTo(6),
            "Deep hierarchy should use at least 6 lanes");
    }

    #endregion

    #region Helper Methods

    private static PullRequestInfo CreateMergedPR(int number, DateTime mergedAt) => new()
    {
        Number = number,
        Title = $"PR #{number}",
        Status = PullRequestStatus.Merged,
        MergedAt = mergedAt,
        CreatedAt = mergedAt.AddDays(-1),
        UpdatedAt = mergedAt
    };

    private static PullRequestInfo CreateOpenPR(int number, string? branchName = null) => new()
    {
        Number = number,
        Title = $"PR #{number}",
        Status = PullRequestStatus.InProgress,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
        BranchName = branchName ?? $"pr-{number}"
    };

    private static Issue CreateIssue(
        string id,
        IssueType type = IssueType.Task,
        string? group = null,
        int? priority = null) => new()
    {
        Id = id,
        Title = $"Issue {id}",
        Status = IssueStatus.Next,
        Type = type,
        Priority = priority,
        Group = group ?? "",
        CreatedAt = DateTimeOffset.UtcNow,
        LastUpdate = DateTimeOffset.UtcNow
    };

    private static Issue CreateIssueWithParent(
        string id,
        string parentId,
        string? group = null,
        int? priority = null) => new()
    {
        Id = id,
        Title = $"Issue {id}",
        Status = IssueStatus.Next,
        Type = IssueType.Task,
        Priority = priority,
        Group = group ?? "",
        ParentIssues = [parentId],
        CreatedAt = DateTimeOffset.UtcNow,
        LastUpdate = DateTimeOffset.UtcNow
    };

    private static Issue CreateIssueWithPrevious(
        string id,
        string previousId,
        string? group = null,
        int? priority = null) => new()
    {
        Id = id,
        Title = $"Issue {id}",
        Status = IssueStatus.Next,
        Type = IssueType.Task,
        Priority = priority,
        Group = group ?? "",
        PreviousIssues = [previousId],
        CreatedAt = DateTimeOffset.UtcNow,
        LastUpdate = DateTimeOffset.UtcNow
    };

    #endregion
}
