using Homespun.Features.Fleece.Services;
using Homespun.Features.Git;
using Homespun.Features.PullRequests.Data;
using Homespun.Shared.Models.PullRequests;
using Homespun.Shared.Models.Projects;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Fleece;

[TestFixture]
public class IssueBranchResolverServiceTests
{
    private Mock<IDataStore> _mockDataStore = null!;
    private Mock<IGitCloneService> _mockGitCloneService = null!;
    private Mock<ILogger<IssueBranchResolverService>> _mockLogger = null!;
    private IssueBranchResolverService _service = null!;

    private const string ProjectId = "project-1";
    private const string IssueId = "abc123";
    private const string RepoPath = "/home/user/.homespun/src/repo/main";

    [SetUp]
    public void SetUp()
    {
        _mockDataStore = new Mock<IDataStore>();
        _mockGitCloneService = new Mock<IGitCloneService>();
        _mockLogger = new Mock<ILogger<IssueBranchResolverService>>();

        // Setup default project
        var project = new Project
        {
            Id = ProjectId,
            Name = "Test Project",
            LocalPath = RepoPath,
            DefaultBranch = "main"
        };
        _mockDataStore.Setup(d => d.GetProject(ProjectId)).Returns(project);

        _service = new IssueBranchResolverService(
            _mockDataStore.Object,
            _mockGitCloneService.Object,
            _mockLogger.Object);
    }

    [Test]
    public async Task ResolveIssueBranchAsync_WithLinkedPR_ReturnsPRBranchName()
    {
        // Arrange
        var expectedBranch = "feature/existing-work+abc123";
        var linkedPr = new PullRequest
        {
            Id = "pr-1",
            ProjectId = ProjectId,
            Title = "Existing PR",
            BranchName = expectedBranch,
            BeadsIssueId = IssueId
        };

        _mockDataStore.Setup(d => d.GetPullRequestsByProject(ProjectId))
            .Returns(new List<PullRequest> { linkedPr });

        // Act
        var result = await _service.ResolveIssueBranchAsync(ProjectId, IssueId);

        // Assert
        Assert.That(result, Is.EqualTo(expectedBranch));
        // Should not need to check clones since PR was found
        _mockGitCloneService.Verify(g => g.ListClonesAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task ResolveIssueBranchAsync_WithNoLinkedPR_ButMatchingClone_ReturnsCloneBranch()
    {
        // Arrange
        var expectedBranch = "task/old-work+abc123";

        _mockDataStore.Setup(d => d.GetPullRequestsByProject(ProjectId))
            .Returns(new List<PullRequest>()); // No PRs

        var clones = new List<CloneInfo>
        {
            new()
            {
                Path = "/home/user/.homespun/src/repo/.clones/task+old-work+abc123",
                Branch = $"refs/heads/{expectedBranch}",
                HeadCommit = "abc1234"
            }
        };
        _mockGitCloneService.Setup(g => g.ListClonesAsync(RepoPath))
            .ReturnsAsync(clones);

        // Act
        var result = await _service.ResolveIssueBranchAsync(ProjectId, IssueId);

        // Assert
        Assert.That(result, Is.EqualTo(expectedBranch));
    }

    [Test]
    public async Task ResolveIssueBranchAsync_WithNoPROrClone_ReturnsNull()
    {
        // Arrange
        _mockDataStore.Setup(d => d.GetPullRequestsByProject(ProjectId))
            .Returns(new List<PullRequest>()); // No PRs

        _mockGitCloneService.Setup(g => g.ListClonesAsync(RepoPath))
            .ReturnsAsync(new List<CloneInfo>()); // No clones

        // Act
        var result = await _service.ResolveIssueBranchAsync(ProjectId, IssueId);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ResolveIssueBranchAsync_PrefersLinkedPROverClone()
    {
        // Arrange - both PR and clone exist with the same issue ID but different branch names
        var prBranch = "feature/pr-branch+abc123";
        var cloneBranch = "task/clone-branch+abc123";

        var linkedPr = new PullRequest
        {
            Id = "pr-1",
            ProjectId = ProjectId,
            Title = "PR",
            BranchName = prBranch,
            BeadsIssueId = IssueId
        };

        _mockDataStore.Setup(d => d.GetPullRequestsByProject(ProjectId))
            .Returns(new List<PullRequest> { linkedPr });

        var clones = new List<CloneInfo>
        {
            new()
            {
                Path = "/home/user/.homespun/src/repo/.clones/task+clone-branch+abc123",
                Branch = $"refs/heads/{cloneBranch}",
                HeadCommit = "def5678"
            }
        };
        _mockGitCloneService.Setup(g => g.ListClonesAsync(RepoPath))
            .ReturnsAsync(clones);

        // Act
        var result = await _service.ResolveIssueBranchAsync(ProjectId, IssueId);

        // Assert - should return PR branch, not clone branch
        Assert.That(result, Is.EqualTo(prBranch));
        // Clone service should not be called since PR was found
        _mockGitCloneService.Verify(g => g.ListClonesAsync(It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task ResolveIssueBranchAsync_WithMultipleMatchingClones_ReturnsFirst()
    {
        // Arrange
        var firstBranch = "feature/first-work+abc123";
        var secondBranch = "task/second-work+abc123";

        _mockDataStore.Setup(d => d.GetPullRequestsByProject(ProjectId))
            .Returns(new List<PullRequest>()); // No PRs

        var clones = new List<CloneInfo>
        {
            new()
            {
                Path = "/home/user/.homespun/src/repo/.clones/feature+first-work+abc123",
                Branch = $"refs/heads/{firstBranch}",
                HeadCommit = "abc1234"
            },
            new()
            {
                Path = "/home/user/.homespun/src/repo/.clones/task+second-work+abc123",
                Branch = $"refs/heads/{secondBranch}",
                HeadCommit = "def5678"
            }
        };
        _mockGitCloneService.Setup(g => g.ListClonesAsync(RepoPath))
            .ReturnsAsync(clones);

        // Act
        var result = await _service.ResolveIssueBranchAsync(ProjectId, IssueId);

        // Assert - should return first matching clone
        Assert.That(result, Is.EqualTo(firstBranch));
    }

    [Test]
    public async Task ResolveIssueBranchAsync_WithProjectNotFound_ReturnsNull()
    {
        // Arrange
        _mockDataStore.Setup(d => d.GetProject(ProjectId)).Returns((Project?)null);

        // Act
        var result = await _service.ResolveIssueBranchAsync(ProjectId, IssueId);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ResolveIssueBranchAsync_IgnoresPRsWithoutMatchingIssueId()
    {
        // Arrange
        var unrelatedPr = new PullRequest
        {
            Id = "pr-1",
            ProjectId = ProjectId,
            Title = "Unrelated PR",
            BranchName = "feature/other-work+xyz789",
            BeadsIssueId = "xyz789" // Different issue ID
        };

        _mockDataStore.Setup(d => d.GetPullRequestsByProject(ProjectId))
            .Returns(new List<PullRequest> { unrelatedPr });

        _mockGitCloneService.Setup(g => g.ListClonesAsync(RepoPath))
            .ReturnsAsync(new List<CloneInfo>()); // No matching clones

        // Act
        var result = await _service.ResolveIssueBranchAsync(ProjectId, IssueId);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ResolveIssueBranchAsync_IgnoresClonesWithoutMatchingIssueId()
    {
        // Arrange
        _mockDataStore.Setup(d => d.GetPullRequestsByProject(ProjectId))
            .Returns(new List<PullRequest>()); // No PRs

        var clones = new List<CloneInfo>
        {
            new()
            {
                Path = "/home/user/.homespun/src/repo/.clones/feature+other+xyz789",
                Branch = "refs/heads/feature/other+xyz789", // Different issue ID
                HeadCommit = "abc1234"
            }
        };
        _mockGitCloneService.Setup(g => g.ListClonesAsync(RepoPath))
            .ReturnsAsync(clones);

        // Act
        var result = await _service.ResolveIssueBranchAsync(ProjectId, IssueId);

        // Assert
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task ResolveIssueBranchAsync_WithPRNullBranchName_FallsBackToClones()
    {
        // Arrange - PR exists but has null branch name
        var linkedPr = new PullRequest
        {
            Id = "pr-1",
            ProjectId = ProjectId,
            Title = "PR without branch",
            BranchName = null, // No branch name
            BeadsIssueId = IssueId
        };

        _mockDataStore.Setup(d => d.GetPullRequestsByProject(ProjectId))
            .Returns(new List<PullRequest> { linkedPr });

        var expectedBranch = "task/clone-branch+abc123";
        var clones = new List<CloneInfo>
        {
            new()
            {
                Path = "/home/user/.homespun/src/repo/.clones/task+clone-branch+abc123",
                Branch = $"refs/heads/{expectedBranch}",
                HeadCommit = "def5678"
            }
        };
        _mockGitCloneService.Setup(g => g.ListClonesAsync(RepoPath))
            .ReturnsAsync(clones);

        // Act
        var result = await _service.ResolveIssueBranchAsync(ProjectId, IssueId);

        // Assert - should fall back to clone since PR has no branch name
        Assert.That(result, Is.EqualTo(expectedBranch));
    }

    [Test]
    public async Task ResolveIssueBranchAsync_WithCloneNullBranch_SkipsClone()
    {
        // Arrange
        _mockDataStore.Setup(d => d.GetPullRequestsByProject(ProjectId))
            .Returns(new List<PullRequest>()); // No PRs

        var clones = new List<CloneInfo>
        {
            new()
            {
                Path = "/home/user/.homespun/src/repo/.clones/detached-head",
                Branch = null, // No branch (detached HEAD)
                HeadCommit = "abc1234"
            }
        };
        _mockGitCloneService.Setup(g => g.ListClonesAsync(RepoPath))
            .ReturnsAsync(clones);

        // Act
        var result = await _service.ResolveIssueBranchAsync(ProjectId, IssueId);

        // Assert
        Assert.That(result, Is.Null);
    }
}
