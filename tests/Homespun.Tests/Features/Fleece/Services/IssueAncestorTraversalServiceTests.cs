using Fleece.Core.Models;
using Homespun.Features.Fleece.Services;

namespace Homespun.Tests.Features.Fleece.Services;

[TestFixture]
public class IssueAncestorTraversalServiceTests
{
    private IssueAncestorTraversalService _service = null!;

    [SetUp]
    public void SetUp()
    {
        _service = new IssueAncestorTraversalService();
    }

    [Test]
    public void CollectVisible_EmptyInput_ReturnsEmpty()
    {
        var result = _service.CollectVisible([], new HashSet<string>());

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void CollectVisible_EmptySeeds_ReturnsEmpty()
    {
        var issues = new List<Issue>
        {
            CreateIssue("a", IssueStatus.Open),
            CreateIssue("b", IssueStatus.Closed)
        };

        var result = _service.CollectVisible(issues, new HashSet<string>());

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void CollectVisible_SingleSeedNoParents_ReturnsSeed()
    {
        var issues = new List<Issue>
        {
            CreateIssue("a", IssueStatus.Open)
        };

        var result = _service.CollectVisible(issues, new HashSet<string> { "a" });

        Assert.That(result.Select(i => i.Id), Is.EquivalentTo(new[] { "a" }));
    }

    [Test]
    public void CollectVisible_OpenWithClosedAncestor_IncludesAncestor()
    {
        // parent (closed) <- child (open, seed)
        var issues = new List<Issue>
        {
            CreateIssue("parent", IssueStatus.Closed),
            CreateIssueWithParent("child", "parent", IssueStatus.Open)
        };

        var result = _service.CollectVisible(issues, new HashSet<string> { "child" });

        Assert.That(result.Select(i => i.Id), Is.EquivalentTo(new[] { "child", "parent" }));
    }

    [Test]
    public void CollectVisible_ChainOfClosedGrandparents_AllIncluded()
    {
        // grandparent (closed) <- parent (closed) <- child (open, seed)
        var issues = new List<Issue>
        {
            CreateIssue("grandparent", IssueStatus.Closed),
            CreateIssueWithParent("parent", "grandparent", IssueStatus.Closed),
            CreateIssueWithParent("child", "parent", IssueStatus.Open)
        };

        var result = _service.CollectVisible(issues, new HashSet<string> { "child" });

        Assert.That(result.Select(i => i.Id), Is.EquivalentTo(new[] { "child", "parent", "grandparent" }));
    }

    [Test]
    public void CollectVisible_ClosedWithNoOpenDescendants_Excluded()
    {
        // No seed targets the closed issue.
        var issues = new List<Issue>
        {
            CreateIssue("orphan-closed", IssueStatus.Closed)
        };

        var result = _service.CollectVisible(issues, new HashSet<string>());

        Assert.That(result, Is.Empty);
    }

    [Test]
    public void CollectVisible_MultiParentDiamond_AllAncestorsIncluded()
    {
        // top
        //  |\
        //  | \
        // m1  m2
        //  \  /
        //   leaf (seed)
        var issues = new List<Issue>
        {
            CreateIssue("top", IssueStatus.Closed),
            CreateIssueWithParent("m1", "top", IssueStatus.Closed),
            CreateIssueWithParent("m2", "top", IssueStatus.Closed),
            CreateIssueWithParents("leaf", new[] { "m1", "m2" }, IssueStatus.Open)
        };

        var result = _service.CollectVisible(issues, new HashSet<string> { "leaf" });

        Assert.That(result.Select(i => i.Id), Is.EquivalentTo(new[] { "leaf", "m1", "m2", "top" }));
    }

    [Test]
    public void CollectVisible_CycleInParentChain_DoesNotLoopForever()
    {
        // a -> b -> a (cycle)
        var issues = new List<Issue>
        {
            CreateIssueWithParent("a", "b", IssueStatus.Open),
            CreateIssueWithParent("b", "a", IssueStatus.Closed)
        };

        var result = _service.CollectVisible(issues, new HashSet<string> { "a" });

        Assert.That(result.Select(i => i.Id), Is.EquivalentTo(new[] { "a", "b" }));
    }

    [Test]
    public void CollectVisible_MultipleSeeds_AllAncestorsCollected()
    {
        // p1 <- s1 (seed)
        // p2 <- s2 (seed)
        var issues = new List<Issue>
        {
            CreateIssue("p1", IssueStatus.Closed),
            CreateIssue("p2", IssueStatus.Closed),
            CreateIssueWithParent("s1", "p1", IssueStatus.Open),
            CreateIssueWithParent("s2", "p2", IssueStatus.Open)
        };

        var result = _service.CollectVisible(issues, new HashSet<string> { "s1", "s2" });

        Assert.That(result.Select(i => i.Id), Is.EquivalentTo(new[] { "s1", "s2", "p1", "p2" }));
    }

    [Test]
    public void CollectVisible_SeedWithUnknownParent_IgnoresMissing()
    {
        // The parent id doesn't resolve to any issue in the input. Should not throw.
        var issues = new List<Issue>
        {
            CreateIssueWithParent("a", "missing-parent", IssueStatus.Open)
        };

        var result = _service.CollectVisible(issues, new HashSet<string> { "a" });

        Assert.That(result.Select(i => i.Id), Is.EquivalentTo(new[] { "a" }));
    }

    [Test]
    public void CollectVisible_SeedNotInIssues_Skipped()
    {
        // Caller passed a seed id that has no corresponding issue. Skip cleanly.
        var issues = new List<Issue>
        {
            CreateIssue("a", IssueStatus.Open)
        };

        var result = _service.CollectVisible(issues, new HashSet<string> { "phantom" });

        Assert.That(result, Is.Empty);
    }

    private static Issue CreateIssue(string id, IssueStatus status) => new()
    {
        Id = id,
        Title = $"Issue {id}",
        Status = status,
        Type = IssueType.Task,
        CreatedAt = DateTimeOffset.UtcNow,
        LastUpdate = DateTimeOffset.UtcNow
    };

    private static Issue CreateIssueWithParent(string id, string parentId, IssueStatus status) => new()
    {
        Id = id,
        Title = $"Issue {id}",
        Status = status,
        Type = IssueType.Task,
        ParentIssues = [new ParentIssueRef { ParentIssue = parentId, SortOrder = "0" }],
        CreatedAt = DateTimeOffset.UtcNow,
        LastUpdate = DateTimeOffset.UtcNow
    };

    private static Issue CreateIssueWithParents(string id, IEnumerable<string> parentIds, IssueStatus status) => new()
    {
        Id = id,
        Title = $"Issue {id}",
        Status = status,
        Type = IssueType.Task,
        ParentIssues = parentIds.Select((pid, idx) => new ParentIssueRef { ParentIssue = pid, SortOrder = idx.ToString() }).ToList(),
        CreatedAt = DateTimeOffset.UtcNow,
        LastUpdate = DateTimeOffset.UtcNow
    };
}
