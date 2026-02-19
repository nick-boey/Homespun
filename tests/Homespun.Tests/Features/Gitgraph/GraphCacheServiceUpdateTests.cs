using Homespun.Features.Gitgraph.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Gitgraph;

/// <summary>
/// Tests for the GraphCacheService UpdatePRStatusAsync method.
/// Verifies that PR statuses can be updated and persisted to cache
/// when PRs transition from open to merged/closed.
/// </summary>
[TestFixture]
public class GraphCacheServiceUpdateTests
{
    private string _tempDir = null!;
    private string _projectLocalPath = null!;
    private GraphCacheService _service = null!;
    private Mock<ILogger<GraphCacheService>> _mockLogger = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"graph-cache-update-test-{Guid.NewGuid()}");
        // Simulate project structure: data/src/{project}/{branch}
        _projectLocalPath = Path.Combine(_tempDir, "test-repo", "main");
        Directory.CreateDirectory(_projectLocalPath);

        _mockLogger = new Mock<ILogger<GraphCacheService>>();
        _service = new GraphCacheService(_mockLogger.Object);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    #region UpdatePRStatusAsync Tests

    [Test]
    public async Task UpdatePRStatusAsync_MovesOpenPrToClosedList_WhenMerged()
    {
        // Arrange - Cache an open PR
        var openPr = CreateOpenPr(42);
        await _service.CachePRDataAsync("project-1", _projectLocalPath, [openPr], []);

        var mergedAt = DateTime.UtcNow;

        // Act - Update status to Merged
        await _service.UpdatePRStatusAsync(
            "project-1",
            _projectLocalPath,
            42,
            PullRequestStatus.Merged,
            mergedAt: mergedAt);

        // Assert
        var cached = _service.GetCachedPRData("project-1");
        Assert.That(cached, Is.Not.Null);
        Assert.That(cached!.OpenPrs, Has.Count.EqualTo(0), "PR should be removed from open list");
        Assert.That(cached.ClosedPrs, Has.Count.EqualTo(1), "PR should be added to closed list");
        Assert.That(cached.ClosedPrs[0].Number, Is.EqualTo(42));
        Assert.That(cached.ClosedPrs[0].Status, Is.EqualTo(PullRequestStatus.Merged));
        Assert.That(cached.ClosedPrs[0].MergedAt, Is.EqualTo(mergedAt));
    }

    [Test]
    public async Task UpdatePRStatusAsync_MovesOpenPrToClosedList_WhenClosedWithoutMerge()
    {
        // Arrange - Cache an open PR
        var openPr = CreateOpenPr(42);
        await _service.CachePRDataAsync("project-1", _projectLocalPath, [openPr], []);

        var closedAt = DateTime.UtcNow;

        // Act - Update status to Closed
        await _service.UpdatePRStatusAsync(
            "project-1",
            _projectLocalPath,
            42,
            PullRequestStatus.Closed,
            closedAt: closedAt);

        // Assert
        var cached = _service.GetCachedPRData("project-1");
        Assert.That(cached, Is.Not.Null);
        Assert.That(cached!.OpenPrs, Has.Count.EqualTo(0), "PR should be removed from open list");
        Assert.That(cached.ClosedPrs, Has.Count.EqualTo(1), "PR should be added to closed list");
        Assert.That(cached.ClosedPrs[0].Number, Is.EqualTo(42));
        Assert.That(cached.ClosedPrs[0].Status, Is.EqualTo(PullRequestStatus.Closed));
        Assert.That(cached.ClosedPrs[0].ClosedAt, Is.EqualTo(closedAt));
    }

    [Test]
    public async Task UpdatePRStatusAsync_UpdatesMergedAtTimestamp_WhenMerged()
    {
        // Arrange
        var openPr = CreateOpenPr(42);
        await _service.CachePRDataAsync("project-1", _projectLocalPath, [openPr], []);

        var mergedAt = new DateTime(2025, 6, 15, 14, 30, 0, DateTimeKind.Utc);

        // Act
        await _service.UpdatePRStatusAsync(
            "project-1",
            _projectLocalPath,
            42,
            PullRequestStatus.Merged,
            mergedAt: mergedAt);

        // Assert
        var cached = _service.GetCachedPRData("project-1");
        Assert.That(cached!.ClosedPrs[0].MergedAt, Is.EqualTo(mergedAt));
    }

    [Test]
    public async Task UpdatePRStatusAsync_UpdatesClosedAtTimestamp_WhenClosed()
    {
        // Arrange
        var openPr = CreateOpenPr(42);
        await _service.CachePRDataAsync("project-1", _projectLocalPath, [openPr], []);

        var closedAt = new DateTime(2025, 6, 15, 14, 30, 0, DateTimeKind.Utc);

        // Act
        await _service.UpdatePRStatusAsync(
            "project-1",
            _projectLocalPath,
            42,
            PullRequestStatus.Closed,
            closedAt: closedAt);

        // Assert
        var cached = _service.GetCachedPRData("project-1");
        Assert.That(cached!.ClosedPrs[0].ClosedAt, Is.EqualTo(closedAt));
    }

    [Test]
    public async Task UpdatePRStatusAsync_PersistsChangesToJsonlFile()
    {
        // Arrange
        var openPr = CreateOpenPr(42);
        await _service.CachePRDataAsync("project-1", _projectLocalPath, [openPr], []);

        var mergedAt = DateTime.UtcNow;

        // Act
        await _service.UpdatePRStatusAsync(
            "project-1",
            _projectLocalPath,
            42,
            PullRequestStatus.Merged,
            mergedAt: mergedAt);

        // Assert - Create fresh service to verify persistence
        var freshService = new GraphCacheService(_mockLogger.Object);
        freshService.LoadCacheForProject("project-1", _projectLocalPath);
        var freshCached = freshService.GetCachedPRData("project-1");

        Assert.That(freshCached, Is.Not.Null);
        Assert.That(freshCached!.OpenPrs, Has.Count.EqualTo(0));
        Assert.That(freshCached.ClosedPrs, Has.Count.EqualTo(1));
        Assert.That(freshCached.ClosedPrs[0].Status, Is.EqualTo(PullRequestStatus.Merged));
    }

    [Test]
    public async Task UpdatePRStatusAsync_PrNotInCache_DoesNotThrow()
    {
        // Arrange - Cache a different PR
        var openPr = CreateOpenPr(100);
        await _service.CachePRDataAsync("project-1", _projectLocalPath, [openPr], []);

        // Act & Assert - Should not throw when PR 42 doesn't exist
        Assert.DoesNotThrowAsync(async () =>
            await _service.UpdatePRStatusAsync(
                "project-1",
                _projectLocalPath,
                42,
                PullRequestStatus.Merged,
                mergedAt: DateTime.UtcNow));

        // Cache should be unchanged
        var cached = _service.GetCachedPRData("project-1");
        Assert.That(cached!.OpenPrs, Has.Count.EqualTo(1));
        Assert.That(cached.OpenPrs[0].Number, Is.EqualTo(100));
    }

    [Test]
    public async Task UpdatePRStatusAsync_ProjectNotCached_DoesNotThrow()
    {
        // Act & Assert - Should not throw when project isn't cached
        Assert.DoesNotThrowAsync(async () =>
            await _service.UpdatePRStatusAsync(
                "nonexistent-project",
                _projectLocalPath,
                42,
                PullRequestStatus.Merged,
                mergedAt: DateTime.UtcNow));
    }

    [Test]
    public async Task UpdatePRStatusAsync_PreservesOtherOpenPrs()
    {
        // Arrange - Cache multiple open PRs
        var openPrs = new List<PullRequestInfo>
        {
            CreateOpenPr(1),
            CreateOpenPr(2),
            CreateOpenPr(3)
        };
        await _service.CachePRDataAsync("project-1", _projectLocalPath, openPrs, []);

        // Act - Close just PR #2
        await _service.UpdatePRStatusAsync(
            "project-1",
            _projectLocalPath,
            2,
            PullRequestStatus.Merged,
            mergedAt: DateTime.UtcNow);

        // Assert
        var cached = _service.GetCachedPRData("project-1");
        Assert.That(cached!.OpenPrs, Has.Count.EqualTo(2));
        Assert.That(cached.OpenPrs.Select(p => p.Number), Is.EquivalentTo(new[] { 1, 3 }));
        Assert.That(cached.ClosedPrs, Has.Count.EqualTo(1));
        Assert.That(cached.ClosedPrs[0].Number, Is.EqualTo(2));
    }

    [Test]
    public async Task UpdatePRStatusAsync_PreservesExistingClosedPrs()
    {
        // Arrange - Cache with existing closed PRs
        var openPrs = new List<PullRequestInfo> { CreateOpenPr(42) };
        var closedPrs = new List<PullRequestInfo>
        {
            CreateClosedPr(100),
            CreateClosedPr(101)
        };
        await _service.CachePRDataAsync("project-1", _projectLocalPath, openPrs, closedPrs);

        // Act - Close the open PR
        await _service.UpdatePRStatusAsync(
            "project-1",
            _projectLocalPath,
            42,
            PullRequestStatus.Merged,
            mergedAt: DateTime.UtcNow);

        // Assert
        var cached = _service.GetCachedPRData("project-1");
        Assert.That(cached!.OpenPrs, Has.Count.EqualTo(0));
        Assert.That(cached.ClosedPrs, Has.Count.EqualTo(3));
        Assert.That(cached.ClosedPrs.Select(p => p.Number), Is.EquivalentTo(new[] { 100, 101, 42 }));
    }

    [Test]
    public async Task UpdatePRStatusAsync_PreservesPrFields_WhenMovingToClosedList()
    {
        // Arrange - Create PR with all fields populated
        var openPr = new PullRequestInfo
        {
            Number = 42,
            Title = "Test PR",
            Body = "Test body",
            Status = PullRequestStatus.ReadyForReview,
            BranchName = "feature/test",
            HtmlUrl = "https://github.com/test/repo/pull/42",
            CreatedAt = new DateTime(2025, 1, 15, 10, 30, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2025, 1, 16, 12, 0, 0, DateTimeKind.Utc),
            ChecksPassing = true,
            IsApproved = true,
            ApprovalCount = 2
        };
        await _service.CachePRDataAsync("project-1", _projectLocalPath, [openPr], []);

        var mergedAt = DateTime.UtcNow;

        // Act
        await _service.UpdatePRStatusAsync(
            "project-1",
            _projectLocalPath,
            42,
            PullRequestStatus.Merged,
            mergedAt: mergedAt);

        // Assert
        var cached = _service.GetCachedPRData("project-1");
        var closedPr = cached!.ClosedPrs[0];

        Assert.That(closedPr.Title, Is.EqualTo("Test PR"));
        Assert.That(closedPr.Body, Is.EqualTo("Test body"));
        Assert.That(closedPr.BranchName, Is.EqualTo("feature/test"));
        Assert.That(closedPr.HtmlUrl, Is.EqualTo("https://github.com/test/repo/pull/42"));
        Assert.That(closedPr.CreatedAt, Is.EqualTo(new DateTime(2025, 1, 15, 10, 30, 0, DateTimeKind.Utc)));
        Assert.That(closedPr.ChecksPassing, Is.True);
        Assert.That(closedPr.IsApproved, Is.True);
        Assert.That(closedPr.ApprovalCount, Is.EqualTo(2));
    }

    [Test]
    public async Task UpdatePRStatusAsync_UpdatesIssuePrStatuses_WhenProvided()
    {
        // Arrange - Cache PR with issue status
        var openPr = CreateOpenPr(42);
        var statuses = new Dictionary<string, PullRequestStatus>
        {
            ["issue-1"] = PullRequestStatus.ReadyForReview
        };
        await _service.CachePRDataWithStatusesAsync("project-1", _projectLocalPath, [openPr], [], statuses);

        // Act
        await _service.UpdatePRStatusAsync(
            "project-1",
            _projectLocalPath,
            42,
            PullRequestStatus.Merged,
            mergedAt: DateTime.UtcNow,
            issueId: "issue-1");

        // Assert
        var cached = _service.GetCachedPRData("project-1");
        Assert.That(cached!.IssuePrStatuses["issue-1"], Is.EqualTo(PullRequestStatus.Merged));
    }

    #endregion

    #region Helper Methods

    private static PullRequestInfo CreateOpenPr(int number)
    {
        return new PullRequestInfo
        {
            Number = number,
            Title = $"Open PR #{number}",
            Status = PullRequestStatus.InProgress,
            BranchName = $"feature/pr-{number}",
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow
        };
    }

    private static PullRequestInfo CreateClosedPr(int number)
    {
        return new PullRequestInfo
        {
            Number = number,
            Title = $"Closed PR #{number}",
            Status = PullRequestStatus.Merged,
            BranchName = $"feature/pr-{number}",
            CreatedAt = DateTime.UtcNow.AddDays(-5),
            MergedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow
        };
    }

    #endregion
}
