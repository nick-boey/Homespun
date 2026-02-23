using Homespun.Features.Gitgraph.Services;
using Homespun.Features.GitHub;
using Homespun.Features.Testing;
using Homespun.Shared.Models.GitHub;
using Homespun.Shared.Models.PullRequests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Homespun.Tests.Features.GitHub;

/// <summary>
/// Tests for the PRStatusResolver service.
/// Verifies that removed PRs are properly resolved to their final status (merged/closed)
/// and the graph cache is updated accordingly.
/// </summary>
[TestFixture]
public class PRStatusResolverTests
{
    private Mock<IGitHubService> _mockGitHubService = null!;
    private Mock<IGraphCacheService> _mockGraphCacheService = null!;
    private Mock<IIssuePrLinkingService> _mockIssuePrLinkingService = null!;
    private MockDataStore _dataStore = null!;
    private PRStatusResolver _resolver = null!;

    [SetUp]
    public void SetUp()
    {
        _mockGitHubService = new Mock<IGitHubService>();
        _mockGraphCacheService = new Mock<IGraphCacheService>();
        _mockIssuePrLinkingService = new Mock<IIssuePrLinkingService>();
        _dataStore = new MockDataStore();

        _resolver = new PRStatusResolver(
            _mockGitHubService.Object,
            _mockGraphCacheService.Object,
            _mockIssuePrLinkingService.Object,
            _dataStore,
            NullLogger<PRStatusResolver>.Instance);
    }

    [TearDown]
    public void TearDown()
    {
        _dataStore.Clear();
    }

    private async Task<Project> CreateTestProject()
    {
        var project = new Project
        {
            Name = "test-repo",
            LocalPath = "/test/path",
            GitHubOwner = "test-owner",
            GitHubRepo = "test-repo",
            DefaultBranch = "main"
        };

        await _dataStore.AddProjectAsync(project);
        return project;
    }

    #region ResolveClosedPRStatusesAsync Tests

    [Test]
    public async Task ResolveClosedPRStatusesAsync_FetchesStatusForEachRemovedPR()
    {
        // Arrange
        var project = await CreateTestProject();
        var removedPrs = new List<RemovedPrInfo>
        {
            new() { PullRequestId = "pr-1", GitHubPrNumber = 42 },
            new() { PullRequestId = "pr-2", GitHubPrNumber = 43 }
        };

        _mockGitHubService.Setup(s => s.GetPullRequestAsync(project.Id, 42))
            .ReturnsAsync(new PullRequestInfo
            {
                Number = 42,
                Title = "PR 42",
                Status = PullRequestStatus.Merged,
                MergedAt = DateTime.UtcNow
            });

        _mockGitHubService.Setup(s => s.GetPullRequestAsync(project.Id, 43))
            .ReturnsAsync(new PullRequestInfo
            {
                Number = 43,
                Title = "PR 43",
                Status = PullRequestStatus.Closed,
                ClosedAt = DateTime.UtcNow
            });

        // Act
        await _resolver.ResolveClosedPRStatusesAsync(project.Id, removedPrs);

        // Assert
        _mockGitHubService.Verify(s => s.GetPullRequestAsync(project.Id, 42), Times.Once);
        _mockGitHubService.Verify(s => s.GetPullRequestAsync(project.Id, 43), Times.Once);
    }

    [Test]
    public async Task ResolveClosedPRStatusesAsync_UpdatesCacheWithMergedStatus()
    {
        // Arrange
        var project = await CreateTestProject();
        var mergedAt = DateTime.UtcNow;
        var removedPrs = new List<RemovedPrInfo>
        {
            new() { PullRequestId = "pr-1", GitHubPrNumber = 42, BeadsIssueId = "issue-1" }
        };

        _mockGitHubService.Setup(s => s.GetPullRequestAsync(project.Id, 42))
            .ReturnsAsync(new PullRequestInfo
            {
                Number = 42,
                Title = "PR 42",
                Status = PullRequestStatus.Merged,
                MergedAt = mergedAt
            });

        // Act
        await _resolver.ResolveClosedPRStatusesAsync(project.Id, removedPrs);

        // Assert
        _mockGraphCacheService.Verify(
            s => s.UpdatePRStatusAsync(
                project.Id,
                project.LocalPath,
                42,
                PullRequestStatus.Merged,
                mergedAt,
                null,
                "issue-1"),
            Times.Once);
    }

    [Test]
    public async Task ResolveClosedPRStatusesAsync_UpdatesCacheWithClosedStatus()
    {
        // Arrange
        var project = await CreateTestProject();
        var closedAt = DateTime.UtcNow;
        var removedPrs = new List<RemovedPrInfo>
        {
            new() { PullRequestId = "pr-1", GitHubPrNumber = 42, BeadsIssueId = "issue-1" }
        };

        _mockGitHubService.Setup(s => s.GetPullRequestAsync(project.Id, 42))
            .ReturnsAsync(new PullRequestInfo
            {
                Number = 42,
                Title = "PR 42",
                Status = PullRequestStatus.Closed,
                ClosedAt = closedAt
            });

        // Act
        await _resolver.ResolveClosedPRStatusesAsync(project.Id, removedPrs);

        // Assert
        _mockGraphCacheService.Verify(
            s => s.UpdatePRStatusAsync(
                project.Id,
                project.LocalPath,
                42,
                PullRequestStatus.Closed,
                null,
                closedAt,
                "issue-1"),
            Times.Once);
    }

    [Test]
    public async Task ResolveClosedPRStatusesAsync_HandlesApiErrors_ContinuesProcessingOtherPRs()
    {
        // Arrange
        var project = await CreateTestProject();
        var removedPrs = new List<RemovedPrInfo>
        {
            new() { PullRequestId = "pr-1", GitHubPrNumber = 42 },
            new() { PullRequestId = "pr-2", GitHubPrNumber = 43 },
            new() { PullRequestId = "pr-3", GitHubPrNumber = 44 }
        };

        // First PR throws exception
        _mockGitHubService.Setup(s => s.GetPullRequestAsync(project.Id, 42))
            .ThrowsAsync(new Exception("API error"));

        // Second PR succeeds
        _mockGitHubService.Setup(s => s.GetPullRequestAsync(project.Id, 43))
            .ReturnsAsync(new PullRequestInfo
            {
                Number = 43,
                Title = "PR 43",
                Status = PullRequestStatus.Merged,
                MergedAt = DateTime.UtcNow
            });

        // Third PR returns null
        _mockGitHubService.Setup(s => s.GetPullRequestAsync(project.Id, 44))
            .ReturnsAsync((PullRequestInfo?)null);

        // Act
        await _resolver.ResolveClosedPRStatusesAsync(project.Id, removedPrs);

        // Assert - Should still call all three PRs and update cache for the successful one
        _mockGitHubService.Verify(s => s.GetPullRequestAsync(project.Id, 42), Times.Once);
        _mockGitHubService.Verify(s => s.GetPullRequestAsync(project.Id, 43), Times.Once);
        _mockGitHubService.Verify(s => s.GetPullRequestAsync(project.Id, 44), Times.Once);
        _mockGraphCacheService.Verify(
            s => s.UpdatePRStatusAsync(
                project.Id,
                project.LocalPath,
                43,
                PullRequestStatus.Merged,
                It.IsAny<DateTime?>(),
                null,
                It.IsAny<string?>()),
            Times.Once);
    }

    [Test]
    public async Task ResolveClosedPRStatusesAsync_EmptyList_DoesNothing()
    {
        // Arrange
        var project = await CreateTestProject();
        var removedPrs = new List<RemovedPrInfo>();

        // Act
        await _resolver.ResolveClosedPRStatusesAsync(project.Id, removedPrs);

        // Assert
        _mockGitHubService.Verify(
            s => s.GetPullRequestAsync(It.IsAny<string>(), It.IsAny<int>()),
            Times.Never);
        _mockGraphCacheService.Verify(
            s => s.UpdatePRStatusAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<PullRequestStatus>(),
                It.IsAny<DateTime?>(),
                It.IsAny<DateTime?>(),
                It.IsAny<string?>()),
            Times.Never);
    }

    [Test]
    public async Task ResolveClosedPRStatusesAsync_PrWithoutGitHubNumber_Skipped()
    {
        // Arrange
        var project = await CreateTestProject();
        var removedPrs = new List<RemovedPrInfo>
        {
            new() { PullRequestId = "pr-1", GitHubPrNumber = null }, // No GitHub PR number
            new() { PullRequestId = "pr-2", GitHubPrNumber = 42 }
        };

        _mockGitHubService.Setup(s => s.GetPullRequestAsync(project.Id, 42))
            .ReturnsAsync(new PullRequestInfo
            {
                Number = 42,
                Title = "PR 42",
                Status = PullRequestStatus.Merged,
                MergedAt = DateTime.UtcNow
            });

        // Act
        await _resolver.ResolveClosedPRStatusesAsync(project.Id, removedPrs);

        // Assert - Should only fetch PR 42
        _mockGitHubService.Verify(
            s => s.GetPullRequestAsync(It.IsAny<string>(), It.IsAny<int>()),
            Times.Once);
        _mockGitHubService.Verify(s => s.GetPullRequestAsync(project.Id, 42), Times.Once);
    }

    [Test]
    public async Task ResolveClosedPRStatusesAsync_ProjectNotFound_DoesNothing()
    {
        // Arrange - Don't create a project
        var removedPrs = new List<RemovedPrInfo>
        {
            new() { PullRequestId = "pr-1", GitHubPrNumber = 42 }
        };

        // Act
        await _resolver.ResolveClosedPRStatusesAsync("nonexistent-project", removedPrs);

        // Assert
        _mockGitHubService.Verify(
            s => s.GetPullRequestAsync(It.IsAny<string>(), It.IsAny<int>()),
            Times.Never);
    }

    [Test]
    public async Task ResolveClosedPRStatusesAsync_WithBeadsIssueId_PassesIssueIdToCache()
    {
        // Arrange
        var project = await CreateTestProject();
        var removedPrs = new List<RemovedPrInfo>
        {
            new() { PullRequestId = "pr-1", GitHubPrNumber = 42, BeadsIssueId = "abc123" }
        };

        _mockGitHubService.Setup(s => s.GetPullRequestAsync(project.Id, 42))
            .ReturnsAsync(new PullRequestInfo
            {
                Number = 42,
                Title = "PR 42",
                Status = PullRequestStatus.Merged,
                MergedAt = DateTime.UtcNow
            });

        // Act
        await _resolver.ResolveClosedPRStatusesAsync(project.Id, removedPrs);

        // Assert
        _mockGraphCacheService.Verify(
            s => s.UpdatePRStatusAsync(
                project.Id,
                project.LocalPath,
                42,
                PullRequestStatus.Merged,
                It.IsAny<DateTime?>(),
                null,
                "abc123"),
            Times.Once);
    }

    [Test]
    public async Task ResolveClosedPRStatusesAsync_WithoutBeadsIssueId_PassesNullIssueIdToCache()
    {
        // Arrange
        var project = await CreateTestProject();
        var removedPrs = new List<RemovedPrInfo>
        {
            new() { PullRequestId = "pr-1", GitHubPrNumber = 42, BeadsIssueId = null }
        };

        _mockGitHubService.Setup(s => s.GetPullRequestAsync(project.Id, 42))
            .ReturnsAsync(new PullRequestInfo
            {
                Number = 42,
                Title = "PR 42",
                Status = PullRequestStatus.Merged,
                MergedAt = DateTime.UtcNow
            });

        // Act
        await _resolver.ResolveClosedPRStatusesAsync(project.Id, removedPrs);

        // Assert
        _mockGraphCacheService.Verify(
            s => s.UpdatePRStatusAsync(
                project.Id,
                project.LocalPath,
                42,
                PullRequestStatus.Merged,
                It.IsAny<DateTime?>(),
                null,
                null),
            Times.Once);
    }

    #endregion

    #region Issue Status Update Tests

    [Test]
    public async Task ResolveClosedPRStatusesAsync_MergedPR_UpdatesIssueToComplete()
    {
        // Arrange
        var project = await CreateTestProject();
        var removedPrs = new List<RemovedPrInfo>
        {
            new() { PullRequestId = "pr-1", GitHubPrNumber = 42, BeadsIssueId = "issue-123" }
        };

        _mockGitHubService.Setup(s => s.GetPullRequestAsync(project.Id, 42))
            .ReturnsAsync(new PullRequestInfo
            {
                Number = 42,
                Title = "PR 42",
                Status = PullRequestStatus.Merged,
                MergedAt = DateTime.UtcNow
            });

        _mockIssuePrLinkingService.Setup(s => s.UpdateIssueStatusFromPRAsync(
            project.Id, "issue-123", PullRequestStatus.Merged, 42))
            .ReturnsAsync(true);

        // Act
        await _resolver.ResolveClosedPRStatusesAsync(project.Id, removedPrs);

        // Assert
        _mockIssuePrLinkingService.Verify(
            s => s.UpdateIssueStatusFromPRAsync(project.Id, "issue-123", PullRequestStatus.Merged, 42),
            Times.Once);
    }

    [Test]
    public async Task ResolveClosedPRStatusesAsync_ClosedPR_UpdatesIssueToClosed()
    {
        // Arrange
        var project = await CreateTestProject();
        var removedPrs = new List<RemovedPrInfo>
        {
            new() { PullRequestId = "pr-1", GitHubPrNumber = 42, BeadsIssueId = "issue-123" }
        };

        _mockGitHubService.Setup(s => s.GetPullRequestAsync(project.Id, 42))
            .ReturnsAsync(new PullRequestInfo
            {
                Number = 42,
                Title = "PR 42",
                Status = PullRequestStatus.Closed,
                ClosedAt = DateTime.UtcNow
            });

        _mockIssuePrLinkingService.Setup(s => s.UpdateIssueStatusFromPRAsync(
            project.Id, "issue-123", PullRequestStatus.Closed, 42))
            .ReturnsAsync(true);

        // Act
        await _resolver.ResolveClosedPRStatusesAsync(project.Id, removedPrs);

        // Assert
        _mockIssuePrLinkingService.Verify(
            s => s.UpdateIssueStatusFromPRAsync(project.Id, "issue-123", PullRequestStatus.Closed, 42),
            Times.Once);
    }

    [Test]
    public async Task ResolveClosedPRStatusesAsync_NoLinkedIssue_SkipsIssueUpdate()
    {
        // Arrange
        var project = await CreateTestProject();
        var removedPrs = new List<RemovedPrInfo>
        {
            new() { PullRequestId = "pr-1", GitHubPrNumber = 42, BeadsIssueId = null }
        };

        _mockGitHubService.Setup(s => s.GetPullRequestAsync(project.Id, 42))
            .ReturnsAsync(new PullRequestInfo
            {
                Number = 42,
                Title = "PR 42",
                Status = PullRequestStatus.Merged,
                MergedAt = DateTime.UtcNow
            });

        // Act
        await _resolver.ResolveClosedPRStatusesAsync(project.Id, removedPrs);

        // Assert - No issue update should be called
        _mockIssuePrLinkingService.Verify(
            s => s.UpdateIssueStatusFromPRAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PullRequestStatus>(), It.IsAny<int>()),
            Times.Never);
    }

    [Test]
    public async Task ResolveClosedPRStatusesAsync_EmptyIssueId_SkipsIssueUpdate()
    {
        // Arrange
        var project = await CreateTestProject();
        var removedPrs = new List<RemovedPrInfo>
        {
            new() { PullRequestId = "pr-1", GitHubPrNumber = 42, BeadsIssueId = "" }
        };

        _mockGitHubService.Setup(s => s.GetPullRequestAsync(project.Id, 42))
            .ReturnsAsync(new PullRequestInfo
            {
                Number = 42,
                Title = "PR 42",
                Status = PullRequestStatus.Merged,
                MergedAt = DateTime.UtcNow
            });

        // Act
        await _resolver.ResolveClosedPRStatusesAsync(project.Id, removedPrs);

        // Assert - No issue update should be called
        _mockIssuePrLinkingService.Verify(
            s => s.UpdateIssueStatusFromPRAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<PullRequestStatus>(), It.IsAny<int>()),
            Times.Never);
    }

    #endregion
}
