using Fleece.Core.Models;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Gitgraph.Services;
using Homespun.Features.Projects;
using Homespun.Features.Testing;
using Homespun.Shared.Models.Fleece;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Gitgraph;

/// <summary>
/// Tests for GraphService.IncrementalRefreshAsync which fetches only open PRs
/// and compares with cache to detect newly closed PRs.
/// </summary>
[TestFixture]
public class GraphServiceIncrementalRefreshTests
{
    private MockDataStore _dataStore = null!;
    private Mock<IProjectService> _mockProjectService = null!;
    private Mock<IGitHubService> _mockGitHubService = null!;
    private Mock<IFleeceService> _mockFleeceService = null!;
    private Mock<IClaudeSessionStore> _mockSessionStore = null!;
    private Mock<PullRequestWorkflowService> _mockWorkflowService = null!;
    private Mock<IGraphCacheService> _mockCacheService = null!;
    private Mock<IPRStatusResolver> _mockPrStatusResolver = null!;
    private Mock<ILogger<GraphService>> _mockLogger = null!;
    private GraphService _service = null!;
    private Project _testProject = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dataStore = new MockDataStore();

        var testPath = Path.Combine(Path.GetTempPath(), $"graphservice-incr-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(testPath);
        Directory.CreateDirectory(Path.Combine(testPath, ".fleece"));

        _testProject = new Project
        {
            Name = "test-repo",
            LocalPath = testPath,
            GitHubOwner = "test-owner",
            GitHubRepo = "test-repo",
            DefaultBranch = "main"
        };
        await _dataStore.AddProjectAsync(_testProject);

        _mockProjectService = new Mock<IProjectService>();
        _mockProjectService.Setup(s => s.GetByIdAsync(_testProject.Id))
            .ReturnsAsync(_testProject);

        _mockGitHubService = new Mock<IGitHubService>();
        _mockGitHubService.Setup(s => s.GetOpenPullRequestsAsync(_testProject.Id))
            .ReturnsAsync(new List<PullRequestInfo>());
        _mockGitHubService.Setup(s => s.GetClosedPullRequestsAsync(_testProject.Id))
            .ReturnsAsync(new List<PullRequestInfo>());

        _mockFleeceService = new Mock<IFleeceService>();
        _mockFleeceService.Setup(s => s.ListIssuesAsync(_testProject.LocalPath, null, null, null, default))
            .ReturnsAsync(new List<Issue>());

        _mockSessionStore = new Mock<IClaudeSessionStore>();
        _mockSessionStore.Setup(s => s.GetByProjectId(_testProject.Id))
            .Returns(new List<ClaudeSession>());

        _mockWorkflowService = new Mock<PullRequestWorkflowService>(
            MockBehavior.Loose,
            _dataStore,
            null!,
            null!,
            null!,
            null!);

        _mockCacheService = new Mock<IGraphCacheService>();
        _mockCacheService.Setup(s => s.GetCachedPRData(It.IsAny<string>()))
            .Returns((CachedPRData?)null);
        _mockCacheService.Setup(s => s.CachePRDataAsync(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<List<PullRequestInfo>>(), It.IsAny<List<PullRequestInfo>>()))
            .Returns(Task.CompletedTask);
        _mockCacheService.Setup(s => s.CachePRDataWithStatusesAsync(It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<List<PullRequestInfo>>(), It.IsAny<List<PullRequestInfo>>(),
                It.IsAny<Dictionary<string, PullRequestStatus>>()))
            .Returns(Task.CompletedTask);

        _mockPrStatusResolver = new Mock<IPRStatusResolver>();

        _mockLogger = new Mock<ILogger<GraphService>>();

        _service = new GraphService(
            _mockProjectService.Object,
            _mockGitHubService.Object,
            _mockFleeceService.Object,
            _mockSessionStore.Object,
            _dataStore,
            _mockWorkflowService.Object,
            _mockCacheService.Object,
            _mockPrStatusResolver.Object,
            _mockLogger.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _dataStore.Clear();
        if (_testProject != null && Directory.Exists(_testProject.LocalPath))
        {
            try { Directory.Delete(_testProject.LocalPath, recursive: true); }
            catch { /* ignore */ }
        }
    }

    [Test]
    public async Task IncrementalRefreshAsync_WhenCacheExists_OnlyFetchesOpenPrs()
    {
        // Arrange - Cache has 1 open PR and 2 closed PRs
        var cachedOpenPr = CreatePrInfo(1, "Open PR", PullRequestStatus.InProgress);
        var cachedClosedPr1 = CreatePrInfo(2, "Closed PR", PullRequestStatus.Merged);
        var cachedClosedPr2 = CreatePrInfo(3, "Closed PR 2", PullRequestStatus.Closed);

        SetupCache(new CachedPRData
        {
            OpenPrs = [cachedOpenPr],
            ClosedPrs = [cachedClosedPr1, cachedClosedPr2],
            CachedAt = DateTime.UtcNow.AddMinutes(-5)
        });

        // Fresh open PRs from GitHub - same PR still open
        _mockGitHubService.Setup(s => s.GetOpenPullRequestsAsync(_testProject.Id))
            .ReturnsAsync(new List<PullRequestInfo> { cachedOpenPr });

        // Act
        var result = await _service.IncrementalRefreshAsync(_testProject.Id);

        // Assert - GetClosedPullRequestsAsync should NOT be called
        _mockGitHubService.Verify(s => s.GetOpenPullRequestsAsync(_testProject.Id), Times.Once);
        _mockGitHubService.Verify(s => s.GetClosedPullRequestsAsync(It.IsAny<string>()), Times.Never);
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task IncrementalRefreshAsync_WhenNoCacheExists_FallsBackToFullFetch()
    {
        // Arrange - No cache
        _mockCacheService.Setup(s => s.GetCachedPRData(_testProject.Id))
            .Returns((CachedPRData?)null);

        var openPr = CreatePrInfo(1, "Open PR", PullRequestStatus.InProgress);
        _mockGitHubService.Setup(s => s.GetOpenPullRequestsAsync(_testProject.Id))
            .ReturnsAsync(new List<PullRequestInfo> { openPr });
        _mockGitHubService.Setup(s => s.GetClosedPullRequestsAsync(_testProject.Id))
            .ReturnsAsync(new List<PullRequestInfo>());

        // Act
        var result = await _service.IncrementalRefreshAsync(_testProject.Id);

        // Assert - Both open and closed PRs should be fetched (full fetch)
        _mockGitHubService.Verify(s => s.GetOpenPullRequestsAsync(_testProject.Id), Times.Once);
        _mockGitHubService.Verify(s => s.GetClosedPullRequestsAsync(_testProject.Id), Times.Once);
        Assert.That(result, Is.Not.Null);
    }

    [Test]
    public async Task IncrementalRefreshAsync_DetectsNewlyClosedPrs_CallsPRStatusResolver()
    {
        // Arrange - Cache has 2 open PRs
        var openPr1 = CreatePrInfo(1, "Still Open", PullRequestStatus.InProgress);
        var openPr2 = CreatePrInfo(2, "Will Be Closed", PullRequestStatus.InProgress);

        // Create a tracked PR for PR #2 so we can get its issue ID
        var trackedPr = new PullRequest
        {
            ProjectId = _testProject.Id,
            Title = "Will Be Closed",
            BranchName = "feature/close-me",
            GitHubPRNumber = 2,
            BeadsIssueId = "issue-1",
            Status = OpenPullRequestStatus.InDevelopment
        };
        await _dataStore.AddPullRequestAsync(trackedPr);

        SetupCache(new CachedPRData
        {
            OpenPrs = [openPr1, openPr2],
            ClosedPrs = [],
            CachedAt = DateTime.UtcNow.AddMinutes(-5)
        });

        // Fresh open PRs from GitHub - only PR #1 is still open (PR #2 was closed)
        _mockGitHubService.Setup(s => s.GetOpenPullRequestsAsync(_testProject.Id))
            .ReturnsAsync(new List<PullRequestInfo> { openPr1 });

        // Act
        await _service.IncrementalRefreshAsync(_testProject.Id);

        // Assert - PRStatusResolver should be called for the newly closed PR
        _mockPrStatusResolver.Verify(r => r.ResolveClosedPRStatusesAsync(
            _testProject.Id,
            It.Is<List<RemovedPrInfo>>(list =>
                list.Count == 1 &&
                list[0].GitHubPrNumber == 2 &&
                list[0].BeadsIssueId == "issue-1")),
            Times.Once);
    }

    [Test]
    public async Task IncrementalRefreshAsync_DetectsNewOpenPrs_IncludedInResult()
    {
        // Arrange - Cache has 1 open PR
        var existingOpenPr = CreatePrInfo(1, "Existing PR", PullRequestStatus.InProgress);

        SetupCache(new CachedPRData
        {
            OpenPrs = [existingOpenPr],
            ClosedPrs = [],
            CachedAt = DateTime.UtcNow.AddMinutes(-5)
        });

        // Fresh open PRs - existing + a new one
        var newOpenPr = CreatePrInfo(2, "New PR", PullRequestStatus.InProgress);
        _mockGitHubService.Setup(s => s.GetOpenPullRequestsAsync(_testProject.Id))
            .ReturnsAsync(new List<PullRequestInfo> { existingOpenPr, newOpenPr });

        // Act
        var result = await _service.IncrementalRefreshAsync(_testProject.Id);

        // Assert - Result should contain commits for both open PRs
        Assert.That(result, Is.Not.Null);
        var prCommits = result.Commits.Where(c => c.PullRequestNumber.HasValue).ToList();
        Assert.That(prCommits.Count, Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public async Task IncrementalRefreshAsync_PreservesExistingClosedPrsInCache()
    {
        // Arrange - Cache has closed PRs
        var closedPr = CreatePrInfo(10, "Already Merged", PullRequestStatus.Merged);
        var openPr = CreatePrInfo(1, "Open PR", PullRequestStatus.InProgress);

        SetupCache(new CachedPRData
        {
            OpenPrs = [openPr],
            ClosedPrs = [closedPr],
            CachedAt = DateTime.UtcNow.AddMinutes(-5)
        });

        _mockGitHubService.Setup(s => s.GetOpenPullRequestsAsync(_testProject.Id))
            .ReturnsAsync(new List<PullRequestInfo> { openPr });

        // Act
        await _service.IncrementalRefreshAsync(_testProject.Id);

        // Assert - Cache should be updated with both open and existing closed PRs
        _mockCacheService.Verify(s => s.CachePRDataWithStatusesAsync(
            _testProject.Id,
            _testProject.LocalPath,
            It.Is<List<PullRequestInfo>>(list => list.Count == 1 && list[0].Number == 1),
            It.Is<List<PullRequestInfo>>(list => list.Count == 1 && list[0].Number == 10),
            It.IsAny<Dictionary<string, PullRequestStatus>>()),
            Times.Once);
    }

    [Test]
    public async Task IncrementalRefreshAsync_UpdatesCacheWithFreshData()
    {
        // Arrange
        var openPr = CreatePrInfo(1, "Open PR", PullRequestStatus.InProgress);

        SetupCache(new CachedPRData
        {
            OpenPrs = [openPr],
            ClosedPrs = [],
            CachedAt = DateTime.UtcNow.AddMinutes(-5)
        });

        _mockGitHubService.Setup(s => s.GetOpenPullRequestsAsync(_testProject.Id))
            .ReturnsAsync(new List<PullRequestInfo> { openPr });

        // Act
        await _service.IncrementalRefreshAsync(_testProject.Id);

        // Assert - CachePRDataWithStatusesAsync should be called
        _mockCacheService.Verify(s => s.CachePRDataWithStatusesAsync(
            _testProject.Id,
            _testProject.LocalPath,
            It.IsAny<List<PullRequestInfo>>(),
            It.IsAny<List<PullRequestInfo>>(),
            It.IsAny<Dictionary<string, PullRequestStatus>>()),
            Times.Once);
    }

    [Test]
    public async Task IncrementalRefreshAsync_UsesTrackedPrsForIssueStatuses_NotGitHub()
    {
        // Arrange - Create a tracked PR with a linked issue
        var trackedPr = new PullRequest
        {
            ProjectId = _testProject.Id,
            Title = "Feature PR",
            BranchName = "feature/test",
            GitHubPRNumber = 1,
            BeadsIssueId = "issue-1",
            Status = OpenPullRequestStatus.InDevelopment
        };
        await _dataStore.AddPullRequestAsync(trackedPr);

        var openPr = CreatePrInfo(1, "Feature PR", PullRequestStatus.InProgress);

        SetupCache(new CachedPRData
        {
            OpenPrs = [openPr],
            ClosedPrs = [],
            CachedAt = DateTime.UtcNow.AddMinutes(-5)
        });

        // Setup an issue that's linked to the PR
        var issue = new Issue
        {
            Id = "other-issue",
            Title = "Unlinked issue",
            Status = IssueStatus.Open,
            Type = IssueType.Task,
            CreatedAt = DateTimeOffset.UtcNow,
            LastUpdate = DateTimeOffset.UtcNow
        };
        _mockFleeceService.Setup(s => s.ListIssuesAsync(_testProject.LocalPath, null, null, null, default))
            .ReturnsAsync(new List<Issue> { issue });

        _mockGitHubService.Setup(s => s.GetOpenPullRequestsAsync(_testProject.Id))
            .ReturnsAsync(new List<PullRequestInfo> { openPr });

        // Act
        await _service.IncrementalRefreshAsync(_testProject.Id);

        // Assert - Only GetOpenPullRequestsAsync should be called (not GetClosedPullRequestsAsync
        // or any review-status fetching methods that hit GitHub)
        _mockGitHubService.Verify(s => s.GetOpenPullRequestsAsync(_testProject.Id), Times.Once);
        _mockGitHubService.Verify(s => s.GetClosedPullRequestsAsync(It.IsAny<string>()), Times.Never);
        // GetPullRequestAsync should NOT be called since no PRs were newly closed
        _mockGitHubService.Verify(s => s.GetPullRequestAsync(It.IsAny<string>(), It.IsAny<int>()), Times.Never);
    }

    #region Helper Methods

    private void SetupCache(CachedPRData cachedData)
    {
        _mockCacheService.Setup(s => s.GetCachedPRData(_testProject.Id))
            .Returns(cachedData);
    }

    private static PullRequestInfo CreatePrInfo(int number, string title, PullRequestStatus status)
    {
        return new PullRequestInfo
        {
            Number = number,
            Title = title,
            Status = status,
            HtmlUrl = $"https://github.com/test/repo/pull/{number}",
            BranchName = $"feature/pr-{number}",
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            MergedAt = status == PullRequestStatus.Merged ? DateTime.UtcNow : null,
            ClosedAt = status == PullRequestStatus.Closed ? DateTime.UtcNow : null
        };
    }

    #endregion
}
