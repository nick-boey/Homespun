using Fleece.Core.Models;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.Fleece.Services;
using Homespun.Features.Gitgraph.Services;
using Homespun.Features.Projects;
using Homespun.Features.Testing;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Gitgraph;

/// <summary>
/// Tests for the GraphService's BuildEnhancedTaskGraphAsync method.
/// Verifies that merged PRs are correctly filtered to show the most recent ones.
/// </summary>
[TestFixture]
public class GraphServiceEnhancedTaskGraphTests
{
    private MockDataStore _dataStore = null!;
    private Mock<IProjectService> _mockProjectService = null!;
    private Mock<IGitHubService> _mockGitHubService = null!;
    private Mock<IFleeceService> _mockFleeceService = null!;
    private Mock<IClaudeSessionStore> _mockSessionStore = null!;
    private Mock<PullRequestWorkflowService> _mockWorkflowService = null!;
    private Mock<IGraphCacheService> _mockCacheService = null!;
    private Mock<ILogger<GraphService>> _mockLogger = null!;
    private GraphService _service = null!;
    private Project _testProject = null!;

    [SetUp]
    public async Task SetUp()
    {
        _dataStore = new MockDataStore();

        // Create test project - use a temp path that exists with .fleece directory
        var testPath = Path.Combine(Path.GetTempPath(), $"graphservice-enhanced-test-{Guid.NewGuid()}");
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

        // Set up mocks
        _mockProjectService = new Mock<IProjectService>();
        _mockProjectService.Setup(s => s.GetByIdAsync(_testProject.Id))
            .ReturnsAsync(_testProject);

        _mockGitHubService = new Mock<IGitHubService>();

        _mockFleeceService = new Mock<IFleeceService>();
        _mockFleeceService.Setup(s => s.GetTaskGraphAsync(_testProject.LocalPath))
            .ReturnsAsync(new TaskGraph { Nodes = [], TotalLanes = 1 });

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

        _mockLogger = new Mock<ILogger<GraphService>>();

        _service = new GraphService(
            _mockProjectService.Object,
            _mockGitHubService.Object,
            _mockFleeceService.Object,
            _mockSessionStore.Object,
            _dataStore,
            _mockWorkflowService.Object,
            _mockCacheService.Object,
            _mockLogger.Object);
    }

    [TearDown]
    public void TearDown()
    {
        _dataStore.Clear();

        // Clean up the temp directory
        if (_testProject != null && Directory.Exists(_testProject.LocalPath))
        {
            try
            {
                Directory.Delete(_testProject.LocalPath, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    #region MaxPastPRs Filtering Tests

    [Test]
    public async Task BuildEnhancedTaskGraphAsync_WithMaxPastPRs_ShowsMostRecentPRs()
    {
        // Arrange - Create 10 merged PRs with different merge dates
        var closedPrs = Enumerable.Range(1, 10)
            .Select(i => CreateMergedPR(i, DateTime.UtcNow.AddDays(-10 + i))) // PR 1 is oldest, PR 10 is newest
            .ToList();

        var cachedData = new CachedPRData { OpenPrs = [], ClosedPrs = closedPrs, IssuePrStatuses = new Dictionary<string, PullRequestStatus>(), CachedAt = DateTime.UtcNow };
        _mockCacheService.Setup(s => s.GetCachedPRData(_testProject.Id)).Returns(cachedData);

        // Act - Request only 5 most recent
        var response = await _service.BuildEnhancedTaskGraphAsync(_testProject.Id, maxPastPRs: 5);

        // Assert - Should only have 5 PRs (the most recent ones: 6, 7, 8, 9, 10)
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.MergedPrs, Has.Count.EqualTo(5));

        // Should be in ascending order (oldest of the 5 most recent first, newest last)
        Assert.That(response.MergedPrs[0].Number, Is.EqualTo(6), "First shown should be PR 6 (oldest of the 5 most recent)");
        Assert.That(response.MergedPrs[1].Number, Is.EqualTo(7));
        Assert.That(response.MergedPrs[2].Number, Is.EqualTo(8));
        Assert.That(response.MergedPrs[3].Number, Is.EqualTo(9));
        Assert.That(response.MergedPrs[4].Number, Is.EqualTo(10), "Last shown should be PR 10 (most recent)");
    }

    [Test]
    public async Task BuildEnhancedTaskGraphAsync_WithMaxPastPRs_SetsHasMorePastPrsTrue()
    {
        // Arrange - Create 10 merged PRs
        var closedPrs = Enumerable.Range(1, 10)
            .Select(i => CreateMergedPR(i, DateTime.UtcNow.AddDays(-10 + i)))
            .ToList();

        var cachedData = new CachedPRData { OpenPrs = [], ClosedPrs = closedPrs, IssuePrStatuses = new Dictionary<string, PullRequestStatus>(), CachedAt = DateTime.UtcNow };
        _mockCacheService.Setup(s => s.GetCachedPRData(_testProject.Id)).Returns(cachedData);

        // Act - Request only 5
        var response = await _service.BuildEnhancedTaskGraphAsync(_testProject.Id, maxPastPRs: 5);

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.HasMorePastPrs, Is.True);
        Assert.That(response.TotalPastPrsShown, Is.EqualTo(5));
    }

    [Test]
    public async Task BuildEnhancedTaskGraphAsync_WithMaxPastPRs_SetsHasMorePastPrsFalse_WhenAllShown()
    {
        // Arrange - Create 3 merged PRs
        var closedPrs = Enumerable.Range(1, 3)
            .Select(i => CreateMergedPR(i, DateTime.UtcNow.AddDays(-3 + i)))
            .ToList();

        var cachedData = new CachedPRData { OpenPrs = [], ClosedPrs = closedPrs, IssuePrStatuses = new Dictionary<string, PullRequestStatus>(), CachedAt = DateTime.UtcNow };
        _mockCacheService.Setup(s => s.GetCachedPRData(_testProject.Id)).Returns(cachedData);

        // Act - Request 5 but only 3 exist
        var response = await _service.BuildEnhancedTaskGraphAsync(_testProject.Id, maxPastPRs: 5);

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.HasMorePastPrs, Is.False);
        Assert.That(response.TotalPastPrsShown, Is.EqualTo(3));
    }

    [Test]
    public async Task BuildEnhancedTaskGraphAsync_PRsAreOrderedByMergeDate()
    {
        // Arrange - Create PRs with out-of-order PR numbers but sequential merge dates
        var closedPrs = new List<PullRequestInfo>
        {
            CreateMergedPR(5, DateTime.UtcNow.AddDays(-3)), // Oldest
            CreateMergedPR(2, DateTime.UtcNow.AddDays(-2)),
            CreateMergedPR(8, DateTime.UtcNow.AddDays(-1))  // Newest
        };

        var cachedData = new CachedPRData { OpenPrs = [], ClosedPrs = closedPrs, IssuePrStatuses = new Dictionary<string, PullRequestStatus>(), CachedAt = DateTime.UtcNow };
        _mockCacheService.Setup(s => s.GetCachedPRData(_testProject.Id)).Returns(cachedData);

        // Act
        var response = await _service.BuildEnhancedTaskGraphAsync(_testProject.Id, maxPastPRs: 5);

        // Assert - Should be ordered by merge date (oldest first)
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.MergedPrs, Has.Count.EqualTo(3));
        Assert.That(response.MergedPrs[0].Number, Is.EqualTo(5), "First should be PR 5 (oldest by merge date)");
        Assert.That(response.MergedPrs[1].Number, Is.EqualTo(2));
        Assert.That(response.MergedPrs[2].Number, Is.EqualTo(8), "Last should be PR 8 (newest by merge date)");
    }

    [Test]
    public async Task BuildEnhancedTaskGraphAsync_NoCachedData_ReturnsEmptyMergedPrs()
    {
        // Arrange - No cached data
        _mockCacheService.Setup(s => s.GetCachedPRData(_testProject.Id)).Returns((CachedPRData?)null);

        // Act
        var response = await _service.BuildEnhancedTaskGraphAsync(_testProject.Id, maxPastPRs: 5);

        // Assert
        Assert.That(response, Is.Not.Null);
        Assert.That(response!.MergedPrs, Is.Empty);
        Assert.That(response.HasMorePastPrs, Is.False);
    }

    #endregion

    #region Helper Methods

    private static PullRequestInfo CreateMergedPR(int number, DateTime mergedAt)
    {
        return new PullRequestInfo
        {
            Number = number,
            Title = $"PR #{number}",
            Status = PullRequestStatus.Merged,
            BranchName = $"feature/pr-{number}",
            CreatedAt = mergedAt.AddDays(-1),
            UpdatedAt = mergedAt,
            MergedAt = mergedAt
        };
    }

    #endregion
}
