using Fleece.Core.Models;
using Homespun.Features.AgentOrchestration.Services;
using Homespun.Features.Fleece.Services;
using Homespun.Features.GitHub;
using Microsoft.Extensions.Logging;
using Moq;
using AgentStartRequest = Homespun.Features.AgentOrchestration.Services.AgentStartRequest;

namespace Homespun.Tests.Features.AgentOrchestration;

[TestFixture]
public class BaseBranchResolutionTests
{
    private Mock<IProjectFleeceService> _mockFleeceService = null!;
    private Mock<IGitHubService> _mockGitHubService = null!;
    private Mock<ILogger<BaseBranchResolver>> _mockLogger = null!;
    private BaseBranchResolver _resolver = null!;

    [SetUp]
    public void SetUp()
    {
        _mockFleeceService = new Mock<IProjectFleeceService>();
        _mockGitHubService = new Mock<IGitHubService>();
        _mockLogger = new Mock<ILogger<BaseBranchResolver>>();

        _resolver = new BaseBranchResolver(
            _mockFleeceService.Object,
            _mockGitHubService.Object,
            _mockLogger.Object);
    }

    private AgentStartRequest CreateTestRequest(string issueId = "issue123", string? baseBranch = null)
    {
        var ts = DateTimeOffset.UtcNow;
        return new AgentStartRequest
        {
            IssueId = issueId,
            ProjectId = "proj123",
            ProjectLocalPath = "/path/to/project",
            ProjectDefaultBranch = "main",
            Issue = new Issue
            {
                Id = issueId,
                Title = "Test Issue",
                Status = IssueStatus.Progress,
                Type = IssueType.Task,
                LastUpdate = ts
            },
            PromptName = "prompt123",
            BaseBranch = baseBranch,
            Model = "sonnet",
            BranchName = $"task/test-issue+{issueId}"
        };
    }

    private Issue CreateIssue(string id, string title, IssueStatus status = IssueStatus.Open, List<ParentIssueRef>? parentIssues = null)
    {
        return new Issue
        {
            Id = id,
            Title = title,
            Status = status,
            Type = IssueType.Task,
            LastUpdate = DateTimeOffset.UtcNow,
            ParentIssues = parentIssues ?? []
        };
    }

    #region No Prior Sibling Tests

    [Test]
    public async Task ResolveBaseBranchAsync_NoPriorSibling_UsesDefaultBranch()
    {
        // Arrange
        var request = CreateTestRequest();

        _mockFleeceService.Setup(x => x.GetBlockingIssuesAsync(
                request.ProjectLocalPath, request.IssueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BlockingIssuesResult([], []));

        _mockFleeceService.Setup(x => x.GetPriorSiblingAsync(
                request.ProjectLocalPath, request.IssueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Issue?)null);

        // Act
        var result = await _resolver.ResolveBaseBranchAsync(request);

        // Assert
        Assert.That(result.BaseBranch, Is.EqualTo("main"));
        Assert.That(result.Error, Is.Null);
    }

    #endregion

    #region Prior Sibling with PR Tests

    [Test]
    public async Task ResolveBaseBranchAsync_PriorSiblingWithOpenPr_UsesPrBranch()
    {
        // Arrange
        var request = CreateTestRequest();
        var priorSibling = CreateIssue("prior123", "Prior Issue", IssueStatus.Review);

        _mockFleeceService.Setup(x => x.GetBlockingIssuesAsync(
                request.ProjectLocalPath, request.IssueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BlockingIssuesResult([], []));

        _mockFleeceService.Setup(x => x.GetPriorSiblingAsync(
                request.ProjectLocalPath, request.IssueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(priorSibling);

        var priorPr = new Homespun.Shared.Models.PullRequests.PullRequest
        {
            ProjectId = request.ProjectId,
            Title = "Prior PR",
            BranchName = "task/prior-issue+prior123",
            Status = OpenPullRequestStatus.ReadyForReview
        };

        _mockGitHubService.Setup(x => x.GetPullRequestForIssueAsync(
                request.ProjectId, priorSibling.Id))
            .ReturnsAsync(priorPr);

        // Act
        var result = await _resolver.ResolveBaseBranchAsync(request);

        // Assert
        Assert.That(result.BaseBranch, Is.EqualTo("task/prior-issue+prior123"));
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public async Task ResolveBaseBranchAsync_PriorSiblingWithMergedPr_UsesDefaultBranch()
    {
        // Arrange
        var request = CreateTestRequest();
        // Prior sibling is complete (its PR was merged), so the PR has been removed from local tracking
        var priorSibling = CreateIssue("prior123", "Prior Issue", IssueStatus.Complete);

        _mockFleeceService.Setup(x => x.GetBlockingIssuesAsync(
                request.ProjectLocalPath, request.IssueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BlockingIssuesResult([], []));

        _mockFleeceService.Setup(x => x.GetPriorSiblingAsync(
                request.ProjectLocalPath, request.IssueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(priorSibling);

        // Merged PRs are removed from local tracking, so GetPullRequestForIssueAsync returns null
        _mockGitHubService.Setup(x => x.GetPullRequestForIssueAsync(
                request.ProjectId, priorSibling.Id))
            .ReturnsAsync((Homespun.Shared.Models.PullRequests.PullRequest?)null);

        // Act
        var result = await _resolver.ResolveBaseBranchAsync(request);

        // Assert
        Assert.That(result.BaseBranch, Is.EqualTo("main"));
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public async Task ResolveBaseBranchAsync_PriorSiblingWithNoPr_UsesDefaultBranch()
    {
        // Arrange
        var request = CreateTestRequest();
        var priorSibling = CreateIssue("prior123", "Prior Issue", IssueStatus.Progress);

        _mockFleeceService.Setup(x => x.GetBlockingIssuesAsync(
                request.ProjectLocalPath, request.IssueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BlockingIssuesResult([], []));

        _mockFleeceService.Setup(x => x.GetPriorSiblingAsync(
                request.ProjectLocalPath, request.IssueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(priorSibling);

        _mockGitHubService.Setup(x => x.GetPullRequestForIssueAsync(
                request.ProjectId, priorSibling.Id))
            .ReturnsAsync((Homespun.Shared.Models.PullRequests.PullRequest?)null);

        // Act
        var result = await _resolver.ResolveBaseBranchAsync(request);

        // Assert
        Assert.That(result.BaseBranch, Is.EqualTo("main"));
        Assert.That(result.Error, Is.Null);
    }

    #endregion

    #region Blocking Issues Tests

    [Test]
    public async Task ResolveBaseBranchAsync_IssueWithOpenChildren_ReturnsError()
    {
        // Arrange
        var request = CreateTestRequest();
        var openChild = CreateIssue("child123", "Child Issue", IssueStatus.Open);

        _mockFleeceService.Setup(x => x.GetBlockingIssuesAsync(
                request.ProjectLocalPath, request.IssueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BlockingIssuesResult([openChild], []));

        // Act
        var result = await _resolver.ResolveBaseBranchAsync(request);

        // Assert
        Assert.That(result.BaseBranch, Is.Null);
        Assert.That(result.Error, Does.Contain("blocked by open issues"));
        Assert.That(result.Error, Does.Contain("child123"));
    }

    [Test]
    public async Task ResolveBaseBranchAsync_IssueWithOpenPriorSiblings_ReturnsError()
    {
        // Arrange
        var request = CreateTestRequest();
        var openPriorSibling = CreateIssue("sibling123", "Sibling Issue", IssueStatus.Progress);

        _mockFleeceService.Setup(x => x.GetBlockingIssuesAsync(
                request.ProjectLocalPath, request.IssueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BlockingIssuesResult([], [openPriorSibling]));

        // Act
        var result = await _resolver.ResolveBaseBranchAsync(request);

        // Assert
        Assert.That(result.BaseBranch, Is.Null);
        Assert.That(result.Error, Does.Contain("blocked by open issues"));
        Assert.That(result.Error, Does.Contain("sibling123"));
    }

    [Test]
    public async Task ResolveBaseBranchAsync_IssueWithMultipleBlockingIssues_ListsAllInError()
    {
        // Arrange
        var request = CreateTestRequest();
        var openChild = CreateIssue("child123", "Child Issue", IssueStatus.Open);
        var openSibling = CreateIssue("sibling123", "Sibling Issue", IssueStatus.Progress);

        _mockFleeceService.Setup(x => x.GetBlockingIssuesAsync(
                request.ProjectLocalPath, request.IssueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BlockingIssuesResult([openChild], [openSibling]));

        // Act
        var result = await _resolver.ResolveBaseBranchAsync(request);

        // Assert
        Assert.That(result.BaseBranch, Is.Null);
        Assert.That(result.Error, Does.Contain("child123"));
        Assert.That(result.Error, Does.Contain("sibling123"));
    }

    #endregion

    #region Explicit BaseBranch Tests

    [Test]
    public async Task ResolveBaseBranchAsync_ExplicitBaseBranch_UsesThatBranch()
    {
        // Arrange
        var request = CreateTestRequest(baseBranch: "feature/custom-base");

        _mockFleeceService.Setup(x => x.GetBlockingIssuesAsync(
                request.ProjectLocalPath, request.IssueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BlockingIssuesResult([], []));

        // Act
        var result = await _resolver.ResolveBaseBranchAsync(request);

        // Assert
        Assert.That(result.BaseBranch, Is.EqualTo("feature/custom-base"));
        Assert.That(result.Error, Is.Null);

        // Verify that GetPriorSiblingAsync was not called since we have an explicit base branch
        _mockFleeceService.Verify(x => x.GetPriorSiblingAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task ResolveBaseBranchAsync_ExplicitBaseBranch_StillChecksBlockingIssues()
    {
        // Arrange
        var request = CreateTestRequest(baseBranch: "feature/custom-base");
        var openChild = CreateIssue("child123", "Child Issue", IssueStatus.Open);

        _mockFleeceService.Setup(x => x.GetBlockingIssuesAsync(
                request.ProjectLocalPath, request.IssueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BlockingIssuesResult([openChild], []));

        // Act
        var result = await _resolver.ResolveBaseBranchAsync(request);

        // Assert - Even with explicit base branch, blocking issues should block
        Assert.That(result.BaseBranch, Is.Null);
        Assert.That(result.Error, Does.Contain("blocked by open issues"));
    }

    #endregion

    #region Prior Sibling with Open PR but InDevelopment Status Tests

    [Test]
    public async Task ResolveBaseBranchAsync_PriorSiblingWithInDevelopmentPr_UsesPrBranch()
    {
        // Arrange
        var request = CreateTestRequest();
        var priorSibling = CreateIssue("prior123", "Prior Issue", IssueStatus.Progress);

        _mockFleeceService.Setup(x => x.GetBlockingIssuesAsync(
                request.ProjectLocalPath, request.IssueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BlockingIssuesResult([], []));

        _mockFleeceService.Setup(x => x.GetPriorSiblingAsync(
                request.ProjectLocalPath, request.IssueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(priorSibling);

        var priorPr = new Homespun.Shared.Models.PullRequests.PullRequest
        {
            ProjectId = request.ProjectId,
            Title = "Prior PR",
            BranchName = "task/prior-issue+prior123",
            Status = OpenPullRequestStatus.InDevelopment
        };

        _mockGitHubService.Setup(x => x.GetPullRequestForIssueAsync(
                request.ProjectId, priorSibling.Id))
            .ReturnsAsync(priorPr);

        // Act
        var result = await _resolver.ResolveBaseBranchAsync(request);

        // Assert - InDevelopment is still an open PR, so use its branch
        Assert.That(result.BaseBranch, Is.EqualTo("task/prior-issue+prior123"));
        Assert.That(result.Error, Is.Null);
    }

    [Test]
    public async Task ResolveBaseBranchAsync_PriorSiblingWithApprovedPr_UsesPrBranch()
    {
        // Arrange
        var request = CreateTestRequest();
        var priorSibling = CreateIssue("prior123", "Prior Issue", IssueStatus.Review);

        _mockFleeceService.Setup(x => x.GetBlockingIssuesAsync(
                request.ProjectLocalPath, request.IssueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BlockingIssuesResult([], []));

        _mockFleeceService.Setup(x => x.GetPriorSiblingAsync(
                request.ProjectLocalPath, request.IssueId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(priorSibling);

        var priorPr = new Homespun.Shared.Models.PullRequests.PullRequest
        {
            ProjectId = request.ProjectId,
            Title = "Prior PR",
            BranchName = "task/prior-issue+prior123",
            Status = OpenPullRequestStatus.Approved
        };

        _mockGitHubService.Setup(x => x.GetPullRequestForIssueAsync(
                request.ProjectId, priorSibling.Id))
            .ReturnsAsync(priorPr);

        // Act
        var result = await _resolver.ResolveBaseBranchAsync(request);

        // Assert - Approved is still an open PR (not yet merged), so use its branch
        Assert.That(result.BaseBranch, Is.EqualTo("task/prior-issue+prior123"));
        Assert.That(result.Error, Is.Null);
    }

    #endregion
}
