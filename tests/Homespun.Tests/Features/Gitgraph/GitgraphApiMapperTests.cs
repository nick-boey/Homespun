using Fleece.Core.Models;
using Homespun.Features.Gitgraph.Data;
using Homespun.Features.Gitgraph.Services;

namespace Homespun.Tests.Features.Gitgraph;

[TestFixture]
public class GitgraphApiMapperTests
{
    private GitgraphApiMapper _mapper = null!;

    [SetUp]
    public void SetUp()
    {
        _mapper = new GitgraphApiMapper();
    }

    [Test]
    public void ToJson_IssueNode_UsesOriginalIdAsHash()
    {
        // Arrange
        var issue = CreateIssue("ISSUE-001", "Test issue");
        var node = new IssueNode(issue, [], timeDimension: 1);
        var graph = new Graph([node], new Dictionary<string, GraphBranch>());

        // Act
        var result = _mapper.ToJson(graph);

        // Assert - Hash should be the original node ID, not a sequential number
        Assert.That(result.Commits, Has.Count.EqualTo(1));
        Assert.That(result.Commits[0].Hash, Is.EqualTo("issue-ISSUE-001"));
    }

    [Test]
    public void ToJson_ParentIdsPreserved()
    {
        // Arrange
        var parentIssue = CreateIssue("ISSUE-001", "Parent issue");
        var childIssue = CreateIssue("ISSUE-002", "Child issue");
        var parentNode = new IssueNode(parentIssue, [], timeDimension: 1);
        var childNode = new IssueNode(childIssue, ["issue-ISSUE-001"], timeDimension: 2);
        var graph = new Graph([parentNode, childNode], new Dictionary<string, GraphBranch>());

        // Act
        var result = _mapper.ToJson(graph);

        // Assert - ParentIds should reference the original node IDs (which now match Hash)
        var child = result.Commits.First(c => c.IssueId == "ISSUE-002");
        Assert.That(child.ParentIds, Has.Count.EqualTo(1));
        Assert.That(child.ParentIds[0], Is.EqualTo("issue-ISSUE-001"));

        // And that parent ID matches the parent's Hash
        var parent = result.Commits.First(c => c.IssueId == "ISSUE-001");
        Assert.That(parent.Hash, Is.EqualTo(child.ParentIds[0]));
    }

    [Test]
    public void ToJson_MixedPrAndIssueNodes_AllKeepOriginalIds()
    {
        // Arrange
        var pr = new PullRequestInfo
        {
            Number = 42,
            Title = "Test PR",
            Status = PullRequestStatus.Merged,
            BranchName = "feature/test",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            MergedAt = DateTime.UtcNow
        };
        var prNode = new PullRequestNode(pr, timeDimension: -1);

        var issue = CreateIssue("ISSUE-001", "Test issue");
        var issueNode = new IssueNode(issue, [], timeDimension: 1);

        var graph = new Graph([prNode, issueNode], new Dictionary<string, GraphBranch>());

        // Act
        var result = _mapper.ToJson(graph);

        // Assert - Both PR and issue nodes should use their original IDs
        Assert.That(result.Commits, Has.Count.EqualTo(2));
        var prCommit = result.Commits.First(c => c.PullRequestNumber == 42);
        var issueCommit = result.Commits.First(c => c.IssueId == "ISSUE-001");
        Assert.That(prCommit.Hash, Is.EqualTo("pr-42"));
        Assert.That(issueCommit.Hash, Is.EqualTo("issue-ISSUE-001"));
    }

    [Test]
    public void ToJson_Branches_PreservesParentCommitId()
    {
        // Arrange
        var branches = new Dictionary<string, GraphBranch>
        {
            ["feature"] = new GraphBranch
            {
                Name = "feature",
                Color = "#3b82f6",
                ParentBranch = "main",
                ParentCommitId = "pr-10"
            }
        };
        var graph = new Graph([], branches);

        // Act
        var result = _mapper.ToJson(graph);

        // Assert
        Assert.That(result.Branches, Has.Count.EqualTo(1));
        Assert.That(result.Branches[0].ParentCommitId, Is.EqualTo("pr-10"));
        Assert.That(result.Branches[0].ParentBranch, Is.EqualTo("main"));
    }

    [Test]
    public void ToJson_EmptyGraph_ReturnsEmptyData()
    {
        // Arrange
        var graph = new Graph([], new Dictionary<string, GraphBranch>());

        // Act
        var result = _mapper.ToJson(graph);

        // Assert
        Assert.That(result.Commits, Is.Empty);
        Assert.That(result.Branches, Is.Empty);
        Assert.That(result.MainBranchName, Is.EqualTo("main"));
    }

    private static Issue CreateIssue(string id, string title) => new()
    {
        Id = id,
        Title = title,
        Status = IssueStatus.Open,
        Type = IssueType.Task,
        CreatedAt = DateTimeOffset.UtcNow,
        LastUpdate = DateTimeOffset.UtcNow
    };
}
