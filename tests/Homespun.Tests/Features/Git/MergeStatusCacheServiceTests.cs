using Homespun.Features.Git;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace Homespun.Tests.Features.Git;

[TestFixture]
public class MergeStatusCacheServiceTests
{
    private Mock<IGitCloneService> _mockGitService = null!;
    private Mock<ILogger<MergeStatusCacheService>> _mockLogger = null!;
    private MergeStatusCacheService _cacheService = null!;
    private string _testRepoPath = null!;

    [SetUp]
    public void Setup()
    {
        _mockGitService = new Mock<IGitCloneService>();
        _mockLogger = new Mock<ILogger<MergeStatusCacheService>>();
        _cacheService = new MergeStatusCacheService(_mockGitService.Object, _mockLogger.Object);
        _testRepoPath = Path.Combine(Path.GetTempPath(), $"test-repo-{Guid.NewGuid():N}");
    }

    [TearDown]
    public void TearDown()
    {
        // Invalidate to clean up any created cache files
        _cacheService.InvalidateRepository(_testRepoPath);
    }

    [Test]
    public async Task GetMergeStatusAsync_NotCached_CallsGitService()
    {
        // Arrange
        _mockGitService
            .Setup(s => s.IsBranchMergedAsync(_testRepoPath, "feature-branch", "main"))
            .ReturnsAsync(false);
        _mockGitService
            .Setup(s => s.IsSquashMergedAsync(_testRepoPath, "feature-branch", "main"))
            .ReturnsAsync(true);

        // Act
        var result = await _cacheService.GetMergeStatusAsync(_testRepoPath, "feature-branch", "main");

        // Assert
        Assert.That(result.IsMerged, Is.False);
        Assert.That(result.IsSquashMerged, Is.True);
        _mockGitService.Verify(s => s.IsBranchMergedAsync(_testRepoPath, "feature-branch", "main"), Times.Once);
        _mockGitService.Verify(s => s.IsSquashMergedAsync(_testRepoPath, "feature-branch", "main"), Times.Once);
    }

    [Test]
    public async Task GetMergeStatusAsync_Cached_DoesNotCallGitServiceAgain()
    {
        // Arrange
        _mockGitService
            .Setup(s => s.IsBranchMergedAsync(_testRepoPath, "feature-branch", "main"))
            .ReturnsAsync(false);
        _mockGitService
            .Setup(s => s.IsSquashMergedAsync(_testRepoPath, "feature-branch", "main"))
            .ReturnsAsync(true);

        // Act
        await _cacheService.GetMergeStatusAsync(_testRepoPath, "feature-branch", "main");
        var result = await _cacheService.GetMergeStatusAsync(_testRepoPath, "feature-branch", "main");

        // Assert
        Assert.That(result.IsMerged, Is.False);
        Assert.That(result.IsSquashMerged, Is.True);
        // Should only call git service once due to caching
        _mockGitService.Verify(s => s.IsBranchMergedAsync(_testRepoPath, "feature-branch", "main"), Times.Once);
    }

    [Test]
    public async Task GetMergeStatusAsync_AlreadyMerged_SkipsSquashMergeCheck()
    {
        // Arrange
        _mockGitService
            .Setup(s => s.IsBranchMergedAsync(_testRepoPath, "merged-branch", "main"))
            .ReturnsAsync(true);

        // Act
        var result = await _cacheService.GetMergeStatusAsync(_testRepoPath, "merged-branch", "main");

        // Assert
        Assert.That(result.IsMerged, Is.True);
        Assert.That(result.IsSquashMerged, Is.False); // Not checked because already merged
        _mockGitService.Verify(s => s.IsSquashMergedAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Test]
    public async Task InvalidateBranch_ClearsCache_ForSpecificBranch()
    {
        // Arrange
        _mockGitService
            .Setup(s => s.IsBranchMergedAsync(_testRepoPath, "feature-branch", "main"))
            .ReturnsAsync(false);
        _mockGitService
            .Setup(s => s.IsSquashMergedAsync(_testRepoPath, "feature-branch", "main"))
            .ReturnsAsync(false);

        await _cacheService.GetMergeStatusAsync(_testRepoPath, "feature-branch", "main");

        // Act
        _cacheService.InvalidateBranch(_testRepoPath, "feature-branch");

        // Need to call again
        await _cacheService.GetMergeStatusAsync(_testRepoPath, "feature-branch", "main");

        // Assert - should call twice due to invalidation
        _mockGitService.Verify(s => s.IsBranchMergedAsync(_testRepoPath, "feature-branch", "main"), Times.Exactly(2));
    }

    [Test]
    public async Task InvalidateRepository_ClearsAllBranchesInCache()
    {
        // Arrange
        _mockGitService
            .Setup(s => s.IsBranchMergedAsync(_testRepoPath, It.IsAny<string>(), "main"))
            .ReturnsAsync(false);
        _mockGitService
            .Setup(s => s.IsSquashMergedAsync(_testRepoPath, It.IsAny<string>(), "main"))
            .ReturnsAsync(false);

        await _cacheService.GetMergeStatusAsync(_testRepoPath, "branch-1", "main");
        await _cacheService.GetMergeStatusAsync(_testRepoPath, "branch-2", "main");

        // Act
        _cacheService.InvalidateRepository(_testRepoPath);

        // Call again for both
        await _cacheService.GetMergeStatusAsync(_testRepoPath, "branch-1", "main");
        await _cacheService.GetMergeStatusAsync(_testRepoPath, "branch-2", "main");

        // Assert - should call twice for each due to invalidation
        _mockGitService.Verify(s => s.IsBranchMergedAsync(_testRepoPath, "branch-1", "main"), Times.Exactly(2));
        _mockGitService.Verify(s => s.IsBranchMergedAsync(_testRepoPath, "branch-2", "main"), Times.Exactly(2));
    }

    [Test]
    public async Task GetMergeStatusAsync_DifferentBranches_CachedSeparately()
    {
        // Arrange
        _mockGitService
            .Setup(s => s.IsBranchMergedAsync(_testRepoPath, "branch-a", "main"))
            .ReturnsAsync(true);
        _mockGitService
            .Setup(s => s.IsBranchMergedAsync(_testRepoPath, "branch-b", "main"))
            .ReturnsAsync(false);
        _mockGitService
            .Setup(s => s.IsSquashMergedAsync(_testRepoPath, "branch-b", "main"))
            .ReturnsAsync(true);

        // Act
        var resultA = await _cacheService.GetMergeStatusAsync(_testRepoPath, "branch-a", "main");
        var resultB = await _cacheService.GetMergeStatusAsync(_testRepoPath, "branch-b", "main");

        // Assert
        Assert.That(resultA.IsMerged, Is.True);
        Assert.That(resultA.IsSquashMerged, Is.False);
        Assert.That(resultB.IsMerged, Is.False);
        Assert.That(resultB.IsSquashMerged, Is.True);
    }

    [Test]
    public void MergeStatus_CheckedAt_IsSetOnCreation()
    {
        // Arrange & Act
        var status = new MergeStatus();

        // Assert
        Assert.That(status.CheckedAt, Is.GreaterThan(DateTime.UtcNow.AddSeconds(-5)));
        Assert.That(status.CheckedAt, Is.LessThanOrEqualTo(DateTime.UtcNow));
    }
}
