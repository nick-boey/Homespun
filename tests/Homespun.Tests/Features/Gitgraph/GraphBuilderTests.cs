using Fleece.Core.Models;
using Homespun.Features.Gitgraph.Services;

namespace Homespun.Tests.Features.Gitgraph;

[TestFixture]
public class GraphBuilderTests
{
    private GraphBuilder _builder = null!;

    [SetUp]
    public void SetUp()
    {
        _builder = new GraphBuilder();
    }

    #region Phase 1: Closed/Merged PRs

    [Test]
    public void Build_MergedPRs_OrderedByMergeDateOldestFirst()
    {
        // Arrange
        var prs = new List<PullRequestInfo>
        {
            CreateMergedPR(1, DateTime.UtcNow.AddDays(-1)),  // Most recent
            CreateMergedPR(2, DateTime.UtcNow.AddDays(-3)),  // Oldest
            CreateMergedPR(3, DateTime.UtcNow.AddDays(-2))   // Middle
        };

        // Act
        var graph = _builder.Build(prs, []);

        // Assert - Should be ordered: PR2 (oldest), PR3, PR1 (newest)
        var prNodes = graph.Nodes.OfType<PullRequestNode>().ToList();
        Assert.That(prNodes, Has.Count.EqualTo(3));
        Assert.That(prNodes[0].PullRequest.Number, Is.EqualTo(2));
        Assert.That(prNodes[1].PullRequest.Number, Is.EqualTo(3));
        Assert.That(prNodes[2].PullRequest.Number, Is.EqualTo(1));
    }

    [Test]
    public void Build_MergedPRs_AreOnMainBranch()
    {
        // Arrange
        var prs = new List<PullRequestInfo>
        {
            CreateMergedPR(1, DateTime.UtcNow)
        };

        // Act
        var graph = _builder.Build(prs, []);

        // Assert
        var prNode = graph.Nodes.OfType<PullRequestNode>().Single();
        Assert.That(prNode.BranchName, Is.EqualTo("main"));
        Assert.That(prNode.NodeType, Is.EqualTo(GraphNodeType.MergedPullRequest));
    }

    [Test]
    public void Build_MergedPRs_HaveNonPositiveTimeDimension()
    {
        // Arrange - Merged PRs should be in the past (time dimension <= 0)
        var prs = new List<PullRequestInfo>
        {
            CreateMergedPR(1, DateTime.UtcNow.AddDays(-1)),
            CreateMergedPR(2, DateTime.UtcNow)
        };

        // Act
        var graph = _builder.Build(prs, []);

        // Assert - All merged PRs should have non-positive time dimension
        var prNodes = graph.Nodes.OfType<PullRequestNode>().ToList();
        Assert.That(prNodes.All(n => n.TimeDimension <= 0), Is.True);
    }

    [Test]
    public void Build_ClosedNotMergedPRs_PlacedByCloseDate()
    {
        // Arrange
        var prs = new List<PullRequestInfo>
        {
            CreateMergedPR(1, DateTime.UtcNow.AddDays(-3)),  // Merged first
            CreateClosedPR(2, DateTime.UtcNow.AddDays(-2)),  // Closed second
            CreateMergedPR(3, DateTime.UtcNow.AddDays(-1))   // Merged last
        };

        // Act
        var graph = _builder.Build(prs, []);

        // Assert - Closed PR should be between the two merged PRs by date
        var prNodes = graph.Nodes.OfType<PullRequestNode>().ToList();
        Assert.That(prNodes[0].PullRequest.Number, Is.EqualTo(1)); // Oldest merged
        Assert.That(prNodes[1].PullRequest.Number, Is.EqualTo(2)); // Closed
        Assert.That(prNodes[2].PullRequest.Number, Is.EqualTo(3)); // Most recent merged
    }

    [Test]
    public void Build_ClosedNotMergedPRs_HaveClosedPullRequestType()
    {
        // Arrange
        var prs = new List<PullRequestInfo>
        {
            CreateClosedPR(1, DateTime.UtcNow)
        };

        // Act
        var graph = _builder.Build(prs, []);

        // Assert
        var prNode = graph.Nodes.OfType<PullRequestNode>().Single();
        Assert.That(prNode.NodeType, Is.EqualTo(GraphNodeType.ClosedPullRequest));
    }

    #endregion

    #region Phase 2: Open PRs

    [Test]
    public void Build_OpenPRs_HaveTimeDimensionOne()
    {
        // Arrange
        var prs = new List<PullRequestInfo>
        {
            CreateOpenPR(1, PullRequestStatus.InProgress),
            CreateOpenPR(2, PullRequestStatus.ReadyForReview)
        };

        // Act
        var graph = _builder.Build(prs, []);

        // Assert
        Assert.That(graph.Nodes.All(n => n.TimeDimension == 1), Is.True);
    }

    [Test]
    public void Build_OpenPRs_OrderedByCreatedDate()
    {
        // Arrange
        var prs = new List<PullRequestInfo>
        {
            CreateOpenPR(1, PullRequestStatus.InProgress, DateTime.UtcNow.AddDays(-1)),  // Most recent
            CreateOpenPR(2, PullRequestStatus.InProgress, DateTime.UtcNow.AddDays(-3)),  // Oldest
            CreateOpenPR(3, PullRequestStatus.InProgress, DateTime.UtcNow.AddDays(-2))   // Middle
        };

        // Act
        var graph = _builder.Build(prs, []);

        // Assert - Ordered by created date (oldest first)
        var prNodes = graph.Nodes.OfType<PullRequestNode>().ToList();
        Assert.That(prNodes[0].PullRequest.Number, Is.EqualTo(2)); // Oldest
        Assert.That(prNodes[1].PullRequest.Number, Is.EqualTo(3)); // Middle
        Assert.That(prNodes[2].PullRequest.Number, Is.EqualTo(1)); // Most recent
    }

    [Test]
    public void Build_OpenPRs_HaveOpenPullRequestType()
    {
        // Arrange
        var prs = new List<PullRequestInfo>
        {
            CreateOpenPR(1, PullRequestStatus.InProgress)
        };

        // Act
        var graph = _builder.Build(prs, []);

        // Assert
        var prNode = graph.Nodes.OfType<PullRequestNode>().Single();
        Assert.That(prNode.NodeType, Is.EqualTo(GraphNodeType.OpenPullRequest));
    }

    [Test]
    public void Build_OpenPRs_ComeAfterClosedPRs()
    {
        // Arrange
        var prs = new List<PullRequestInfo>
        {
            CreateMergedPR(1, DateTime.UtcNow.AddDays(-1)),
            CreateOpenPR(2, PullRequestStatus.InProgress, DateTime.UtcNow.AddDays(-10))  // Created before merge
        };

        // Act
        var graph = _builder.Build(prs, []);

        // Assert - Open PR should come after merged PR regardless of dates
        var prNodes = graph.Nodes.OfType<PullRequestNode>().ToList();
        Assert.That(prNodes[0].PullRequest.Number, Is.EqualTo(1)); // Merged
        Assert.That(prNodes[1].PullRequest.Number, Is.EqualTo(2)); // Open
    }

    [Test]
    public void Build_OpenPRs_UseTheirBranchName()
    {
        // Arrange
        var pr = CreateOpenPR(1, PullRequestStatus.InProgress);
        pr.BranchName = "feature/my-feature";

        // Act
        var graph = _builder.Build([pr], []);

        // Assert
        var prNode = graph.Nodes.OfType<PullRequestNode>().Single();
        Assert.That(prNode.BranchName, Is.EqualTo("feature/my-feature"));
    }

    #endregion

    #region Phase 3: Issues with Dependencies

    [Test]
    public void Build_Issues_OrphanHasNoParentConnections()
    {
        // Arrange
        var prs = new List<PullRequestInfo>
        {
            CreateMergedPR(1, DateTime.UtcNow.AddDays(-2)),
            CreateMergedPR(2, DateTime.UtcNow)  // Latest merged
        };
        var issues = new List<Issue> { CreateIssue("bd-001") };

        // Act
        var graph = _builder.Build(prs, issues);

        // Assert - Orphan issues have NO parent connections (flat list)
        var issueNode = graph.Nodes.OfType<IssueNode>().First();
        Assert.That(issueNode.ParentIds, Is.Empty);
        Assert.That(issueNode.IsOrphan, Is.True);
    }

    [Test]
    public void Build_Issues_NextStatusConnectsToLatestMergedPR()
    {
        // Arrange - "Open" status issues should be orphans (no connection to PRs)
        var prs = new List<PullRequestInfo>
        {
            CreateMergedPR(1, DateTime.UtcNow.AddDays(-2)),
            CreateMergedPR(2, DateTime.UtcNow)  // Latest merged
        };
        var issues = new List<Issue>
        {
            new()
            {
                Id = "bd-001",
                Title = "Issue bd-001",
                Status = IssueStatus.Open, // "Open" status
                Type = IssueType.Task,
                CreatedAt = DateTimeOffset.UtcNow,
                LastUpdate = DateTimeOffset.UtcNow
            }
        };

        // Act
        var graph = _builder.Build(prs, issues);

        // Assert - "Open" status issues are orphans (no parent connections)
        var issueNode = graph.Nodes.OfType<IssueNode>().First();
        Assert.That(issueNode.ParentIds, Is.Empty);
        Assert.That(issueNode.IsOrphan, Is.True);
    }

    [Test]
    public void Build_Issues_RespectDependencyOrder_DepthFirst()
    {
        // Arrange: bd-001 blocks bd-002, bd-002 blocks bd-003
        // (bd-001 must complete before bd-002, bd-002 must complete before bd-003)
        var issues = new List<Issue>
        {
            CreateIssue("bd-001"),
            CreateIssueWithParent("bd-002", "bd-001"),
            CreateIssueWithParent("bd-003", "bd-002")
        };

        // Act
        var graph = _builder.Build([], issues);

        // Assert - DFS order: bd-001 -> bd-002 -> bd-003
        var issueNodes = graph.Nodes.OfType<IssueNode>().ToList();
        Assert.That(issueNodes[0].Issue.Id, Is.EqualTo("bd-001"));
        Assert.That(issueNodes[1].Issue.Id, Is.EqualTo("bd-002"));
        Assert.That(issueNodes[2].Issue.Id, Is.EqualTo("bd-003"));
    }

    [Test]
    public void Build_Issues_MultipleParents_WaitForAllParents()
    {
        // Arrange: bd-003 is blocked by both bd-001 and bd-002
        var issues = new List<Issue>
        {
            CreateIssue("bd-001"),
            CreateIssue("bd-002"),
            CreateIssueWithParents("bd-003", ["bd-001", "bd-002"])
        };

        // Act
        var graph = _builder.Build([], issues);

        // Assert - bd-003 should come after both bd-001 and bd-002
        var issueNodes = graph.Nodes.OfType<IssueNode>().ToList();
        var bd003Index = issueNodes.FindIndex(n => n.Issue.Id == "bd-003");
        var bd001Index = issueNodes.FindIndex(n => n.Issue.Id == "bd-001");
        var bd002Index = issueNodes.FindIndex(n => n.Issue.Id == "bd-002");

        Assert.That(bd003Index, Is.GreaterThan(bd001Index));
        Assert.That(bd003Index, Is.GreaterThan(bd002Index));
    }

    [Test]
    public void Build_Issues_MultipleParents_AllReferenced()
    {
        // Arrange: bd-003 is blocked by both bd-001 and bd-002
        var issues = new List<Issue>
        {
            CreateIssue("bd-001"),
            CreateIssue("bd-002"),
            CreateIssueWithParents("bd-003", ["bd-001", "bd-002"])
        };

        // Act
        var graph = _builder.Build([], issues);

        // Assert - bd-003 should reference both parents
        var node3 = graph.Nodes.OfType<IssueNode>().First(n => n.Issue.Id == "bd-003");
        Assert.That(node3.ParentIds, Contains.Item("issue-bd-001"));
        Assert.That(node3.ParentIds, Contains.Item("issue-bd-002"));
    }

    [Test]
    public void Build_Issues_HaveTimeDimensionTwoOrGreater()
    {
        // Arrange
        var issues = new List<Issue> { CreateIssue("bd-001") };

        // Act
        var graph = _builder.Build([], issues);

        // Assert - Issues are in the future (time dimension >= 2)
        var issueNode = graph.Nodes.OfType<IssueNode>().First();
        Assert.That(issueNode.TimeDimension, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public void Build_Issues_ComeAfterOpenPRs()
    {
        // Arrange
        var prs = new List<PullRequestInfo>
        {
            CreateOpenPR(1, PullRequestStatus.InProgress)
        };
        var issues = new List<Issue> { CreateIssue("bd-001") };

        // Act
        var graph = _builder.Build(prs, issues);

        // Assert - Issue should come after open PR
        var nodeList = graph.Nodes.ToList();
        var prIndex = nodeList.FindIndex(n => n is PullRequestNode);
        var issueIndex = nodeList.FindIndex(n => n is IssueNode);
        Assert.That(issueIndex, Is.GreaterThan(prIndex));
    }

    #endregion

    #region Phase 4: Orphan Issues

    [Test]
    public void Build_OrphanIssues_FlatListWithNoConnections()
    {
        // Arrange - Issues with no dependencies are orphans
        var issues = new List<Issue>
        {
            CreateIssue("bd-001"),
            CreateIssue("bd-002")
        };

        // Act
        var graph = _builder.Build([], issues);

        // Assert - Orphan issues should be a flat list (no parent connections)
        var orphanNodes = graph.Nodes.OfType<IssueNode>().ToList();
        Assert.That(orphanNodes, Has.Count.EqualTo(2));
        Assert.That(orphanNodes.All(n => n.NodeType == GraphNodeType.OrphanIssue), Is.True);
        // Flat list means NO parent connections
        Assert.That(orphanNodes.All(n => n.ParentIds.Count == 0), Is.True);
    }

    [Test]
    public void Build_OrphanIssues_OrderedByCreatedAtAscending()
    {
        // Arrange - Orphan issues are now sorted ONLY by CreatedAt (oldest first)
        var issues = new List<Issue>
        {
            CreateIssue("bd-001", priority: 3, createdAt: DateTime.UtcNow.AddDays(-1)),  // Newest
            CreateIssue("bd-002", priority: 1, createdAt: DateTime.UtcNow.AddDays(-2)),  // Middle
            CreateIssue("bd-003", priority: 1, createdAt: DateTime.UtcNow.AddDays(-3))   // Oldest
        };

        // Act
        var graph = _builder.Build([], issues);

        // Assert - Order by CreatedAt ascending (oldest to newest): bd-003, bd-002, bd-001
        var orphanNodes = graph.Nodes.OfType<IssueNode>().ToList();
        Assert.That(orphanNodes[0].Issue.Id, Is.EqualTo("bd-003"));
        Assert.That(orphanNodes[1].Issue.Id, Is.EqualTo("bd-002"));
        Assert.That(orphanNodes[2].Issue.Id, Is.EqualTo("bd-001"));
    }

    [Test]
    public void Build_OrphanIssues_PlacedAtEnd()
    {
        // Arrange - Mix of dependent issues and orphan issues
        var issues = new List<Issue>
        {
            CreateIssue("bd-001"),  // Will be root (has child)
            CreateIssueWithParent("bd-002", "bd-001"),  // Child of bd-001
            CreateIssue("bd-003")   // Orphan (no deps)
        };

        // Act
        var graph = _builder.Build([], issues);

        // Assert - Orphan (bd-003) should come after dependent issues (bd-001, bd-002)
        var issueNodes = graph.Nodes.OfType<IssueNode>().ToList();
        var bd003Index = issueNodes.FindIndex(n => n.Issue.Id == "bd-003");
        var bd001Index = issueNodes.FindIndex(n => n.Issue.Id == "bd-001");
        var bd002Index = issueNodes.FindIndex(n => n.Issue.Id == "bd-002");

        Assert.That(bd003Index, Is.GreaterThan(bd001Index));
        Assert.That(bd003Index, Is.GreaterThan(bd002Index));
    }

    [Test]
    public void Build_OrphanIssues_IgnoresGroupProperty()
    {
        // Arrange - Orphan issues with different groups (groups are now ignored)
        var issues = new List<Issue>
        {
            CreateIssueWithGroup("bd-001", "frontend", createdAt: DateTime.UtcNow.AddDays(-3)),
            CreateIssueWithGroup("bd-002", "backend", createdAt: DateTime.UtcNow.AddDays(-2)),
            CreateIssueWithGroup("bd-003", "frontend", createdAt: DateTime.UtcNow.AddDays(-1)),
        };

        // Act
        var graph = _builder.Build([], issues);

        // Assert - Orphan issues are now a flat list with no grouping
        var orphanNodes = graph.Nodes.OfType<IssueNode>().ToList();
        Assert.That(orphanNodes, Has.Count.EqualTo(3));

        // All orphan issues should have no parent connections (flat list)
        Assert.That(orphanNodes.All(n => n.ParentIds.Count == 0), Is.True);

        // Order should be by CreatedAt ascending (oldest first)
        Assert.That(orphanNodes[0].Issue.Id, Is.EqualTo("bd-001"));
        Assert.That(orphanNodes[1].Issue.Id, Is.EqualTo("bd-002"));
        Assert.That(orphanNodes[2].Issue.Id, Is.EqualTo("bd-003"));
    }

    [Test]
    public void Build_OrphanIssues_AllHaveSameTimeDimension()
    {
        // Arrange - Orphan issues all have the same time dimension (flat list)
        var issues = new List<Issue>
        {
            CreateIssueWithGroup("bd-001", "zebra", createdAt: DateTime.UtcNow.AddDays(-3)),
            CreateIssueWithGroup("bd-002", "alpha", createdAt: DateTime.UtcNow.AddDays(-2)),
            CreateIssueWithGroup("bd-003", "middle", createdAt: DateTime.UtcNow.AddDays(-1)),
        };

        // Act
        var graph = _builder.Build([], issues);

        // Assert - All orphan issues should have the same TimeDimension (3 = after connected issues)
        var orphanNodes = graph.Nodes.OfType<IssueNode>().ToList();

        Assert.That(orphanNodes.All(n => n.TimeDimension == 3), Is.True);
        // Order should be by CreatedAt ascending
        Assert.That(orphanNodes[0].Issue.Id, Is.EqualTo("bd-001"));
        Assert.That(orphanNodes[1].Issue.Id, Is.EqualTo("bd-002"));
        Assert.That(orphanNodes[2].Issue.Id, Is.EqualTo("bd-003"));
    }

    [Test]
    public void Build_OrphanIssues_IgnoresPriorityUseCreatedAtOnly()
    {
        // Arrange - Orphan issues are now ordered by CreatedAt only, priority is ignored
        var issues = new List<Issue>
        {
            CreateIssueWithGroup("bd-001", "frontend", priority: 3, createdAt: DateTime.UtcNow.AddDays(-1)),  // Newest
            CreateIssueWithGroup("bd-002", "frontend", priority: 1, createdAt: DateTime.UtcNow.AddDays(-2)),  // Middle
            CreateIssueWithGroup("bd-003", "frontend", priority: 1, createdAt: DateTime.UtcNow.AddDays(-3)),  // Oldest
        };

        // Act
        var graph = _builder.Build([], issues);

        // Assert - Order by CreatedAt ascending (oldest to newest): bd-003, bd-002, bd-001
        var orphanNodes = graph.Nodes.OfType<IssueNode>().ToList();
        var nodeOrder = orphanNodes.Select(n => n.Issue.Id).ToList();

        Assert.That(nodeOrder[0], Is.EqualTo("bd-003"));
        Assert.That(nodeOrder[1], Is.EqualTo("bd-002"));
        Assert.That(nodeOrder[2], Is.EqualTo("bd-001"));
    }

    [Test]
    public void Build_OrphanIssues_AllTreatedAsFlatList()
    {
        // Arrange - Mix of issues with and without groups (groups are now ignored)
        var issues = new List<Issue>
        {
            CreateIssueWithGroup("bd-001", "frontend", createdAt: DateTime.UtcNow.AddDays(-2)),
            CreateIssue("bd-002", createdAt: DateTime.UtcNow.AddDays(-1)),  // No group
        };

        // Act
        var graph = _builder.Build([], issues);

        // Assert - All orphan issues should be a flat list with no special branch grouping
        var orphanNodes = graph.Nodes.OfType<IssueNode>().ToList();
        Assert.That(orphanNodes, Has.Count.EqualTo(2));

        // All should have no parent connections (flat list)
        Assert.That(orphanNodes.All(n => n.ParentIds.Count == 0), Is.True);

        // Order should be by CreatedAt ascending (oldest first)
        Assert.That(orphanNodes[0].Issue.Id, Is.EqualTo("bd-001"));
        Assert.That(orphanNodes[1].Issue.Id, Is.EqualTo("bd-002"));
    }

    #endregion

    #region MaxPastPRs Filtering

    [Test]
    public void Build_WithMaxPastPRs_LimitsClosedPRs()
    {
        // Arrange - Create 10 merged PRs
        var prs = Enumerable.Range(1, 10)
            .Select(i => CreateMergedPR(i, DateTime.UtcNow.AddDays(-10 + i)))
            .ToList();

        // Act - Request only 5 most recent
        var graph = _builder.Build(prs, [], maxPastPRs: 5);

        // Assert - Should only have 5 PRs (the most recent ones: 6, 7, 8, 9, 10)
        var prNodes = graph.Nodes.OfType<PullRequestNode>().ToList();
        Assert.That(prNodes, Has.Count.EqualTo(5));
        Assert.That(prNodes[0].PullRequest.Number, Is.EqualTo(6)); // 6th oldest = oldest of the 5 most recent
        Assert.That(prNodes[4].PullRequest.Number, Is.EqualTo(10)); // Most recent
    }

    [Test]
    public void Build_WithMaxPastPRs_SetsHasMorePastPRsTrue()
    {
        // Arrange - Create 10 merged PRs
        var prs = Enumerable.Range(1, 10)
            .Select(i => CreateMergedPR(i, DateTime.UtcNow.AddDays(-10 + i)))
            .ToList();

        // Act - Request only 5
        var graph = _builder.Build(prs, [], maxPastPRs: 5);

        // Assert
        Assert.That(graph.HasMorePastPRs, Is.True);
        Assert.That(graph.TotalPastPRsShown, Is.EqualTo(5));
    }

    [Test]
    public void Build_WithMaxPastPRs_SetsHasMorePastPRsFalse_WhenAllShown()
    {
        // Arrange - Create 3 merged PRs
        var prs = Enumerable.Range(1, 3)
            .Select(i => CreateMergedPR(i, DateTime.UtcNow.AddDays(-3 + i)))
            .ToList();

        // Act - Request 5 but only 3 exist
        var graph = _builder.Build(prs, [], maxPastPRs: 5);

        // Assert
        Assert.That(graph.HasMorePastPRs, Is.False);
        Assert.That(graph.TotalPastPRsShown, Is.EqualTo(3));
    }

    [Test]
    public void Build_WithMaxPastPRsNull_ShowsAllPRs()
    {
        // Arrange - Create 10 merged PRs
        var prs = Enumerable.Range(1, 10)
            .Select(i => CreateMergedPR(i, DateTime.UtcNow.AddDays(-10 + i)))
            .ToList();

        // Act - No limit
        var graph = _builder.Build(prs, [], maxPastPRs: null);

        // Assert - Should show all 10
        var prNodes = graph.Nodes.OfType<PullRequestNode>().ToList();
        Assert.That(prNodes, Has.Count.EqualTo(10));
        Assert.That(graph.HasMorePastPRs, Is.False);
    }

    [Test]
    public void Build_WithMaxPastPRs_DoesNotAffectOpenPRs()
    {
        // Arrange - Create 10 merged and 3 open PRs
        var mergedPrs = Enumerable.Range(1, 10)
            .Select(i => CreateMergedPR(i, DateTime.UtcNow.AddDays(-10 + i)))
            .ToList();
        var openPrs = Enumerable.Range(11, 3)
            .Select(i => CreateOpenPR(i, PullRequestStatus.InProgress))
            .ToList();
        var allPrs = mergedPrs.Concat(openPrs).ToList();

        // Act - Limit to 5 past PRs
        var graph = _builder.Build(allPrs, [], maxPastPRs: 5);

        // Assert - Should have 5 merged + 3 open = 8 total PRs
        var prNodes = graph.Nodes.OfType<PullRequestNode>().ToList();
        Assert.That(prNodes, Has.Count.EqualTo(8));

        var mergedNodes = prNodes.Where(n => n.NodeType == GraphNodeType.MergedPullRequest).ToList();
        var openNodes = prNodes.Where(n => n.NodeType == GraphNodeType.OpenPullRequest).ToList();

        Assert.That(mergedNodes, Has.Count.EqualTo(5));
        Assert.That(openNodes, Has.Count.EqualTo(3));
    }

    [Test]
    public void Build_WithMaxPastPRs_IncludesBothMergedAndClosedInLimit()
    {
        // Arrange - Create 5 merged and 5 closed PRs
        var mergedPrs = Enumerable.Range(1, 5)
            .Select(i => CreateMergedPR(i, DateTime.UtcNow.AddDays(-10 + i)))
            .ToList();
        var closedPrs = Enumerable.Range(6, 5)
            .Select(i => CreateClosedPR(i, DateTime.UtcNow.AddDays(-5 + i)))
            .ToList();
        var allPrs = mergedPrs.Concat(closedPrs).ToList();

        // Act - Limit to 5 past PRs
        var graph = _builder.Build(allPrs, [], maxPastPRs: 5);

        // Assert - Should have 5 most recent (which includes both merged and closed)
        var prNodes = graph.Nodes.OfType<PullRequestNode>()
            .Where(n => n.NodeType == GraphNodeType.MergedPullRequest || n.NodeType == GraphNodeType.ClosedPullRequest)
            .ToList();
        Assert.That(prNodes, Has.Count.EqualTo(5));
        Assert.That(graph.HasMorePastPRs, Is.True);
    }

    #endregion

    #region Closed PR Branching

    [Test]
    public void Build_ClosedPRs_HaveParentIdPointingToPreviousMergedPR()
    {
        // Arrange - Merged PR followed by a closed PR
        var prs = new List<PullRequestInfo>
        {
            CreateMergedPR(1, DateTime.UtcNow.AddDays(-3)),  // Merged first
            CreateClosedPR(2, DateTime.UtcNow.AddDays(-2)),  // Closed second
        };

        // Act
        var graph = _builder.Build(prs, []);

        // Assert - Closed PR should have parent ID pointing to the merged PR
        var prNodes = graph.Nodes.OfType<PullRequestNode>().ToList();
        var closedNode = prNodes.First(n => n.PullRequest.Number == 2);
        Assert.That(closedNode.ParentIds, Has.Count.EqualTo(1));
        Assert.That(closedNode.ParentIds[0], Is.EqualTo("pr-1"));
    }

    [Test]
    public void Build_MergedPRs_HaveNoParentIds()
    {
        // Arrange - Multiple merged PRs
        var prs = new List<PullRequestInfo>
        {
            CreateMergedPR(1, DateTime.UtcNow.AddDays(-3)),
            CreateMergedPR(2, DateTime.UtcNow.AddDays(-2)),
            CreateMergedPR(3, DateTime.UtcNow.AddDays(-1))
        };

        // Act
        var graph = _builder.Build(prs, []);

        // Assert - All merged PRs should have no parent IDs (stay on main line)
        var prNodes = graph.Nodes.OfType<PullRequestNode>().ToList();
        Assert.That(prNodes.All(n => n.ParentIds.Count == 0), Is.True);
    }

    [Test]
    public void Build_ClosedPRs_UseTheirActualBranchName()
    {
        // Arrange - Closed PR with a specific branch name
        var pr = CreateClosedPR(1, DateTime.UtcNow);
        pr.BranchName = "feature/abandoned-feature";

        // Act
        var graph = _builder.Build([pr], []);

        // Assert - Closed PR should use its actual branch name (not "main")
        var prNode = graph.Nodes.OfType<PullRequestNode>().Single();
        Assert.That(prNode.BranchName, Is.EqualTo("feature/abandoned-feature"));
    }

    [Test]
    public void Build_MixedMergedAndClosedPRs_ClosedBranchFromPreviousMerged()
    {
        // Arrange - Mixed sequence: Merged, Closed, Merged, Closed
        var prs = new List<PullRequestInfo>
        {
            CreateMergedPR(1, DateTime.UtcNow.AddDays(-4)),  // Merged
            CreateClosedPR(2, DateTime.UtcNow.AddDays(-3)),  // Closed - should branch from PR 1
            CreateMergedPR(3, DateTime.UtcNow.AddDays(-2)),  // Merged
            CreateClosedPR(4, DateTime.UtcNow.AddDays(-1))   // Closed - should branch from PR 3
        };

        // Act
        var graph = _builder.Build(prs, []);

        // Assert
        var prNodes = graph.Nodes.OfType<PullRequestNode>().ToList();

        // PR 1 (merged) - no parent
        Assert.That(prNodes[0].ParentIds, Is.Empty);

        // PR 2 (closed) - parent is PR 1
        Assert.That(prNodes[1].ParentIds, Has.Count.EqualTo(1));
        Assert.That(prNodes[1].ParentIds[0], Is.EqualTo("pr-1"));

        // PR 3 (merged) - no parent
        Assert.That(prNodes[2].ParentIds, Is.Empty);

        // PR 4 (closed) - parent is PR 3
        Assert.That(prNodes[3].ParentIds, Has.Count.EqualTo(1));
        Assert.That(prNodes[3].ParentIds[0], Is.EqualTo("pr-3"));
    }

    [Test]
    public void Build_ClosedPRBeforeAnyMerged_HasNoParent()
    {
        // Arrange - Closed PR comes before any merged PR
        var prs = new List<PullRequestInfo>
        {
            CreateClosedPR(1, DateTime.UtcNow.AddDays(-2)),  // Closed first
            CreateMergedPR(2, DateTime.UtcNow.AddDays(-1))   // Merged second
        };

        // Act
        var graph = _builder.Build(prs, []);

        // Assert - First closed PR has no parent (nothing to branch from)
        var prNodes = graph.Nodes.OfType<PullRequestNode>().ToList();
        Assert.That(prNodes[0].ParentIds, Is.Empty);
    }

    #endregion

    #region Branch Creation

    [Test]
    public void Build_CreatesMainBranch()
    {
        // Arrange
        var prs = new List<PullRequestInfo> { CreateMergedPR(1, DateTime.UtcNow) };

        // Act
        var graph = _builder.Build(prs, []);

        // Assert
        Assert.That(graph.Branches.ContainsKey("main"), Is.True);
        Assert.That(graph.MainBranchName, Is.EqualTo("main"));
    }

    [Test]
    public void Build_CreatesBranchForOpenPRs()
    {
        // Arrange
        var pr = CreateOpenPR(1, PullRequestStatus.InProgress);
        pr.BranchName = "feature/test";

        // Act
        var graph = _builder.Build([pr], []);

        // Assert
        Assert.That(graph.Branches.ContainsKey("feature/test"), Is.True);
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

    private static PullRequestInfo CreateClosedPR(int number, DateTime closedAt) => new()
    {
        Number = number,
        Title = $"PR #{number}",
        Status = PullRequestStatus.Closed,
        ClosedAt = closedAt,
        CreatedAt = closedAt.AddDays(-1),
        UpdatedAt = closedAt
    };

    private static PullRequestInfo CreateOpenPR(int number, PullRequestStatus status, DateTime? createdAt = null) => new()
    {
        Number = number,
        Title = $"PR #{number}",
        Status = status,
        CreatedAt = createdAt ?? DateTime.UtcNow,
        UpdatedAt = createdAt ?? DateTime.UtcNow,
        BranchName = $"pr-{number}"
    };

    private static Issue CreateIssue(string id, int? priority = null, DateTime? createdAt = null) => new()
    {
        Id = id,
        Title = $"Issue {id}",
        Status = IssueStatus.Open, // Use Open status so issues are true orphans
        Type = IssueType.Task,
        Priority = priority,
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
        LastUpdate = createdAt ?? DateTimeOffset.UtcNow
    };

    private static Issue CreateIssueWithGroup(string id, string group, int? priority = null, DateTime? createdAt = null) => new()
    {
        Id = id,
        Title = $"Issue {id}",
        Status = IssueStatus.Open, // Use Open status so issues are true orphans
        Type = IssueType.Task,
        Priority = priority,
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
        LastUpdate = createdAt ?? DateTimeOffset.UtcNow
    };

    private static Issue CreateIssueWithParent(string id, string parentId, int? priority = null, DateTime? createdAt = null) => new()
    {
        Id = id,
        Title = $"Issue {id}",
        Status = IssueStatus.Open,
        Type = IssueType.Task,
        Priority = priority,
        ParentIssues = [new ParentIssueRef { ParentIssue = parentId, SortOrder = "0" }],
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
        LastUpdate = createdAt ?? DateTimeOffset.UtcNow
    };

    private static Issue CreateIssueWithParents(string id, List<string> parentIds, int? priority = null, DateTime? createdAt = null) => new()
    {
        Id = id,
        Title = $"Issue {id}",
        Status = IssueStatus.Open,
        Type = IssueType.Task,
        Priority = priority,
        ParentIssues = parentIds.Select((pid, idx) => new ParentIssueRef { ParentIssue = pid, SortOrder = idx.ToString() }).ToList(),
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
        LastUpdate = createdAt ?? DateTimeOffset.UtcNow
    };

    #endregion
}
