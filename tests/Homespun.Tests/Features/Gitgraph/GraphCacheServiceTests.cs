using System.Text.Json;
using Homespun.Features.Gitgraph.Services;
using Microsoft.Extensions.Logging;
using Moq;

namespace Homespun.Tests.Features.Gitgraph;

/// <summary>
/// Tests for the GraphCacheService JSONL file-based caching.
/// Verifies that PR data is correctly persisted to and loaded from
/// pull_requests.jsonl files in the project directory.
/// </summary>
[TestFixture]
public class GraphCacheServiceTests
{
    private string _tempDir = null!;
    private string _projectLocalPath = null!;
    private GraphCacheService _service = null!;
    private Mock<ILogger<GraphCacheService>> _mockLogger = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"graph-cache-test-{Guid.NewGuid()}");
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

    #region CachePRDataAsync Tests

    [Test]
    public async Task CachePRDataAsync_WritesJsonlFile()
    {
        // Arrange
        var openPrs = CreateOpenPrs(2);
        var closedPrs = CreateClosedPrs(1);

        // Act
        await _service.CachePRDataAsync("project-1", _projectLocalPath, openPrs, closedPrs);

        // Assert - File should exist in parent directory
        var cacheFile = GraphCacheService.GetCacheFilePath(_projectLocalPath);
        Assert.That(File.Exists(cacheFile), Is.True, $"Cache file should exist at {cacheFile}");
    }

    [Test]
    public async Task CachePRDataAsync_FileContainsCorrectNumberOfLines()
    {
        // Arrange - 2 open + 1 closed = 3 PR lines + 1 metadata line = 4 total
        var openPrs = CreateOpenPrs(2);
        var closedPrs = CreateClosedPrs(1);

        // Act
        await _service.CachePRDataAsync("project-1", _projectLocalPath, openPrs, closedPrs);

        // Assert
        var cacheFile = GraphCacheService.GetCacheFilePath(_projectLocalPath);
        var lines = File.ReadAllLines(cacheFile).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        Assert.That(lines.Length, Is.EqualTo(4), "Should have 1 metadata + 2 open + 1 closed lines");
    }

    [Test]
    public async Task CachePRDataAsync_DataAvailableFromMemoryImmediately()
    {
        // Arrange
        var openPrs = CreateOpenPrs(1);
        var closedPrs = CreateClosedPrs(1);

        // Act
        await _service.CachePRDataAsync("project-1", _projectLocalPath, openPrs, closedPrs);

        // Assert
        var cached = _service.GetCachedPRData("project-1");
        Assert.That(cached, Is.Not.Null);
        Assert.That(cached!.OpenPrs, Has.Count.EqualTo(1));
        Assert.That(cached.ClosedPrs, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task CachePRDataAsync_PreservesAllPrFields()
    {
        // Arrange
        var openPr = new PullRequestInfo
        {
            Number = 42,
            Title = "Test PR",
            Body = "This is the body",
            Status = PullRequestStatus.InProgress,
            BranchName = "feature/test-branch",
            HtmlUrl = "https://github.com/test/repo/pull/42",
            CreatedAt = new DateTime(2025, 1, 15, 10, 30, 0, DateTimeKind.Utc),
            MergedAt = null,
            ClosedAt = null,
            UpdatedAt = new DateTime(2025, 1, 16, 12, 0, 0, DateTimeKind.Utc),
            ChecksPassing = true,
            IsApproved = false,
            HasUnresolvedComments = true,
            ReviewCommentCount = 5,
            ChangesRequestedCount = 1,
            ApprovalCount = 0
        };

        // Act
        await _service.CachePRDataAsync("project-1", _projectLocalPath, [openPr], []);

        // Assert - Create new service instance to force reading from file
        var freshService = new GraphCacheService(_mockLogger.Object);
        freshService.LoadCacheForProject("project-1", _projectLocalPath);
        var cached = freshService.GetCachedPRData("project-1");

        Assert.That(cached, Is.Not.Null);
        Assert.That(cached!.OpenPrs, Has.Count.EqualTo(1));
        var loadedPr = cached.OpenPrs[0];
        Assert.That(loadedPr.Number, Is.EqualTo(42));
        Assert.That(loadedPr.Title, Is.EqualTo("Test PR"));
        Assert.That(loadedPr.Body, Is.EqualTo("This is the body"));
        Assert.That(loadedPr.Status, Is.EqualTo(PullRequestStatus.InProgress));
        Assert.That(loadedPr.BranchName, Is.EqualTo("feature/test-branch"));
        Assert.That(loadedPr.HtmlUrl, Is.EqualTo("https://github.com/test/repo/pull/42"));
        Assert.That(loadedPr.ChecksPassing, Is.True);
        Assert.That(loadedPr.IsApproved, Is.False);
        Assert.That(loadedPr.HasUnresolvedComments, Is.True);
        Assert.That(loadedPr.ReviewCommentCount, Is.EqualTo(5));
        Assert.That(loadedPr.ChangesRequestedCount, Is.EqualTo(1));
        Assert.That(loadedPr.ApprovalCount, Is.EqualTo(0));
    }

    [Test]
    public async Task CachePRDataAsync_OverwritesPreviousCache()
    {
        // Arrange - Cache initial data
        var initialPrs = CreateOpenPrs(1);
        await _service.CachePRDataAsync("project-1", _projectLocalPath, initialPrs, []);

        // Act - Cache new data with different count
        var updatedPrs = CreateOpenPrs(3);
        await _service.CachePRDataAsync("project-1", _projectLocalPath, updatedPrs, []);

        // Assert - Memory should have updated data
        var cached = _service.GetCachedPRData("project-1");
        Assert.That(cached!.OpenPrs, Has.Count.EqualTo(3));

        // Assert - File should also have updated data
        var freshService = new GraphCacheService(_mockLogger.Object);
        freshService.LoadCacheForProject("project-1", _projectLocalPath);
        var freshCached = freshService.GetCachedPRData("project-1");
        Assert.That(freshCached!.OpenPrs, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task CachePRDataAsync_EmptyLists_CreatesFileWithOnlyMetadata()
    {
        // Act
        await _service.CachePRDataAsync("project-1", _projectLocalPath, [], []);

        // Assert
        var cacheFile = GraphCacheService.GetCacheFilePath(_projectLocalPath);
        var lines = File.ReadAllLines(cacheFile).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        Assert.That(lines.Length, Is.EqualTo(1), "Should have only metadata line");

        var cached = _service.GetCachedPRData("project-1");
        Assert.That(cached!.OpenPrs, Has.Count.EqualTo(0));
        Assert.That(cached.ClosedPrs, Has.Count.EqualTo(0));
    }

    #endregion

    #region CachePRDataWithStatusesAsync Tests

    [Test]
    public async Task CachePRDataWithStatusesAsync_PersistsStatuses()
    {
        // Arrange
        var openPrs = CreateOpenPrs(1);
        var statuses = new Dictionary<string, PullRequestStatus>
        {
            ["issue-1"] = PullRequestStatus.ReadyForReview,
            ["issue-2"] = PullRequestStatus.ChecksFailing
        };

        // Act
        await _service.CachePRDataWithStatusesAsync("project-1", _projectLocalPath, openPrs, [], statuses);

        // Assert - Read from fresh service
        var freshService = new GraphCacheService(_mockLogger.Object);
        freshService.LoadCacheForProject("project-1", _projectLocalPath);
        var cached = freshService.GetCachedPRData("project-1");

        Assert.That(cached, Is.Not.Null);
        Assert.That(cached!.IssuePrStatuses, Has.Count.EqualTo(2));
        Assert.That(cached.IssuePrStatuses["issue-1"], Is.EqualTo(PullRequestStatus.ReadyForReview));
        Assert.That(cached.IssuePrStatuses["issue-2"], Is.EqualTo(PullRequestStatus.ChecksFailing));
    }

    #endregion

    #region GetCachedPRData Tests

    [Test]
    public void GetCachedPRData_NoCacheExists_ReturnsNull()
    {
        // Act
        var cached = _service.GetCachedPRData("nonexistent-project");

        // Assert
        Assert.That(cached, Is.Null);
    }

    [Test]
    public async Task GetCachedPRData_CacheExists_ReturnsData()
    {
        // Arrange
        var openPrs = CreateOpenPrs(2);
        await _service.CachePRDataAsync("project-1", _projectLocalPath, openPrs, []);

        // Act
        var cached = _service.GetCachedPRData("project-1");

        // Assert
        Assert.That(cached, Is.Not.Null);
        Assert.That(cached!.OpenPrs, Has.Count.EqualTo(2));
    }

    #endregion

    #region GetCacheTimestamp Tests

    [Test]
    public void GetCacheTimestamp_NoCacheExists_ReturnsNull()
    {
        // Act
        var timestamp = _service.GetCacheTimestamp("nonexistent-project");

        // Assert
        Assert.That(timestamp, Is.Null);
    }

    [Test]
    public async Task GetCacheTimestamp_CacheExists_ReturnsTimestamp()
    {
        // Arrange
        var before = DateTime.UtcNow;
        await _service.CachePRDataAsync("project-1", _projectLocalPath, [], []);
        var after = DateTime.UtcNow;

        // Act
        var timestamp = _service.GetCacheTimestamp("project-1");

        // Assert
        Assert.That(timestamp, Is.Not.Null);
        Assert.That(timestamp!.Value, Is.GreaterThanOrEqualTo(before));
        Assert.That(timestamp.Value, Is.LessThanOrEqualTo(after));
    }

    #endregion

    #region InvalidateCacheAsync Tests

    [Test]
    public async Task InvalidateCacheAsync_RemovesFromMemoryAndDisk()
    {
        // Arrange
        await _service.CachePRDataAsync("project-1", _projectLocalPath, CreateOpenPrs(1), []);
        var cacheFile = GraphCacheService.GetCacheFilePath(_projectLocalPath);
        Assert.That(File.Exists(cacheFile), Is.True, "Cache file should exist before invalidation");

        // Act
        await _service.InvalidateCacheAsync("project-1");

        // Assert
        Assert.That(_service.GetCachedPRData("project-1"), Is.Null, "Memory cache should be cleared");
        Assert.That(File.Exists(cacheFile), Is.False, "Cache file should be deleted");
    }

    [Test]
    public async Task InvalidateCacheAsync_NonexistentProject_DoesNotThrow()
    {
        // Act & Assert
        Assert.DoesNotThrowAsync(async () => await _service.InvalidateCacheAsync("nonexistent-project"));
    }

    #endregion

    #region LoadCacheForProject Tests

    [Test]
    public async Task LoadCacheForProject_LoadsFromDiskOnFirstAccess()
    {
        // Arrange - Cache data with one service instance
        var openPrs = CreateOpenPrs(3);
        var closedPrs = CreateClosedPrs(2);
        await _service.CachePRDataAsync("project-1", _projectLocalPath, openPrs, closedPrs);

        // Act - Create a new service instance (simulating server restart) and load
        var freshService = new GraphCacheService(_mockLogger.Object);
        freshService.LoadCacheForProject("project-1", _projectLocalPath);

        // Assert
        var cached = freshService.GetCachedPRData("project-1");
        Assert.That(cached, Is.Not.Null);
        Assert.That(cached!.OpenPrs, Has.Count.EqualTo(3));
        Assert.That(cached.ClosedPrs, Has.Count.EqualTo(2));
    }

    [Test]
    public async Task LoadCacheForProject_SkipsIfAlreadyInMemory()
    {
        // Arrange - Cache data
        await _service.CachePRDataAsync("project-1", _projectLocalPath, CreateOpenPrs(2), []);

        // Overwrite file with different data
        var cacheFile = GraphCacheService.GetCacheFilePath(_projectLocalPath);
        File.WriteAllText(cacheFile, ""); // Corrupt the file

        // Act - Loading should be skipped since it's already in memory
        _service.LoadCacheForProject("project-1", _projectLocalPath);

        // Assert - Should still have original data from memory
        var cached = _service.GetCachedPRData("project-1");
        Assert.That(cached!.OpenPrs, Has.Count.EqualTo(2));
    }

    [Test]
    public void LoadCacheForProject_NoFileExists_DoesNotThrow()
    {
        // Act & Assert
        Assert.DoesNotThrow(() => _service.LoadCacheForProject("project-1", _projectLocalPath));
        Assert.That(_service.GetCachedPRData("project-1"), Is.Null);
    }

    [Test]
    public async Task LoadCacheForProject_CorruptFile_DoesNotThrow()
    {
        // Arrange - Write a corrupt file
        var cacheFile = GraphCacheService.GetCacheFilePath(_projectLocalPath);
        Directory.CreateDirectory(Path.GetDirectoryName(cacheFile)!);
        await File.WriteAllTextAsync(cacheFile, "not valid json\n{also bad}");

        // Act & Assert - Should not throw
        Assert.DoesNotThrow(() => _service.LoadCacheForProject("project-1", _projectLocalPath));
    }

    #endregion

    #region File Path Tests

    [Test]
    public void GetCacheFilePath_ReturnsParentDirectoryPath()
    {
        // Arrange
        var localPath = Path.Combine(_tempDir, "my-repo", "main");
        Directory.CreateDirectory(localPath);

        // Act
        var cacheFile = GraphCacheService.GetCacheFilePath(localPath);

        // Assert - Should be in parent directory (my-repo), not in branch directory (main)
        var expectedDir = Path.Combine(_tempDir, "my-repo");
        Assert.That(Path.GetDirectoryName(cacheFile), Is.EqualTo(expectedDir));
        Assert.That(Path.GetFileName(cacheFile), Is.EqualTo("pull_requests.jsonl"));
    }

    #endregion

    #region JSONL Format Tests

    [Test]
    public async Task CachePRDataAsync_FirstLineIsMetadata()
    {
        // Arrange
        await _service.CachePRDataAsync("project-1", _projectLocalPath, CreateOpenPrs(1), []);

        // Act
        var cacheFile = GraphCacheService.GetCacheFilePath(_projectLocalPath);
        var firstLine = File.ReadAllLines(cacheFile)[0];
        using var doc = JsonDocument.Parse(firstLine);

        // Assert
        Assert.That(doc.RootElement.GetProperty("type").GetString(), Is.EqualTo("metadata"));
        Assert.That(doc.RootElement.TryGetProperty("cachedAt", out _), Is.True);
    }

    [Test]
    public async Task CachePRDataAsync_PrLinesHaveCorrectType()
    {
        // Arrange
        await _service.CachePRDataAsync("project-1", _projectLocalPath, CreateOpenPrs(1), CreateClosedPrs(1));

        // Act
        var cacheFile = GraphCacheService.GetCacheFilePath(_projectLocalPath);
        var lines = File.ReadAllLines(cacheFile).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();

        // Assert - line[0] = metadata, line[1] = open, line[2] = closed
        using var openDoc = JsonDocument.Parse(lines[1]);
        Assert.That(openDoc.RootElement.GetProperty("type").GetString(), Is.EqualTo("open"));
        Assert.That(openDoc.RootElement.TryGetProperty("pr", out _), Is.True);

        using var closedDoc = JsonDocument.Parse(lines[2]);
        Assert.That(closedDoc.RootElement.GetProperty("type").GetString(), Is.EqualTo("closed"));
        Assert.That(closedDoc.RootElement.TryGetProperty("pr", out _), Is.True);
    }

    [Test]
    public async Task CachePRDataAsync_JsonlUsesOneLinePerEntry()
    {
        // Arrange - Multiple PRs
        await _service.CachePRDataAsync("project-1", _projectLocalPath, CreateOpenPrs(3), CreateClosedPrs(2));

        // Act
        var cacheFile = GraphCacheService.GetCacheFilePath(_projectLocalPath);
        var lines = File.ReadAllLines(cacheFile).Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();

        // Assert - Each line is valid JSON, one per line
        Assert.That(lines.Length, Is.EqualTo(6), "1 metadata + 3 open + 2 closed = 6 lines");
        foreach (var line in lines)
        {
            Assert.DoesNotThrow(() => JsonDocument.Parse(line), $"Each line should be valid JSON: {line}");
        }
    }

    #endregion

    #region Multi-Project Tests

    [Test]
    public async Task MultipleProjects_IndependentCaches()
    {
        // Arrange - Two different projects
        var projectPath2 = Path.Combine(_tempDir, "other-repo", "main");
        Directory.CreateDirectory(projectPath2);

        // Act
        await _service.CachePRDataAsync("project-1", _projectLocalPath, CreateOpenPrs(2), []);
        await _service.CachePRDataAsync("project-2", projectPath2, CreateOpenPrs(5), []);

        // Assert
        var cached1 = _service.GetCachedPRData("project-1");
        var cached2 = _service.GetCachedPRData("project-2");
        Assert.That(cached1!.OpenPrs, Has.Count.EqualTo(2));
        Assert.That(cached2!.OpenPrs, Has.Count.EqualTo(5));
    }

    [Test]
    public async Task MultipleProjects_InvalidateOneDoesNotAffectOther()
    {
        // Arrange
        var projectPath2 = Path.Combine(_tempDir, "other-repo", "main");
        Directory.CreateDirectory(projectPath2);

        await _service.CachePRDataAsync("project-1", _projectLocalPath, CreateOpenPrs(2), []);
        await _service.CachePRDataAsync("project-2", projectPath2, CreateOpenPrs(5), []);

        // Act
        await _service.InvalidateCacheAsync("project-1");

        // Assert
        Assert.That(_service.GetCachedPRData("project-1"), Is.Null);
        Assert.That(_service.GetCachedPRData("project-2")!.OpenPrs, Has.Count.EqualTo(5));
    }

    #endregion

    #region Persistence Across Restart Tests

    [Test]
    public async Task CachePersistedAcrossServiceRestarts()
    {
        // Arrange - Cache data with statuses
        var openPrs = CreateOpenPrs(2);
        var closedPrs = CreateClosedPrs(3);
        var statuses = new Dictionary<string, PullRequestStatus>
        {
            ["issue-A"] = PullRequestStatus.Merged,
            ["issue-B"] = PullRequestStatus.Conflict
        };

        await _service.CachePRDataWithStatusesAsync("project-1", _projectLocalPath, openPrs, closedPrs, statuses);

        // Act - Simulate server restart: create new service instance
        var restartedService = new GraphCacheService(_mockLogger.Object);
        restartedService.LoadCacheForProject("project-1", _projectLocalPath);

        // Assert
        var cached = restartedService.GetCachedPRData("project-1");
        Assert.That(cached, Is.Not.Null);
        Assert.That(cached!.OpenPrs, Has.Count.EqualTo(2));
        Assert.That(cached.ClosedPrs, Has.Count.EqualTo(3));
        Assert.That(cached.IssuePrStatuses, Has.Count.EqualTo(2));
        Assert.That(cached.IssuePrStatuses["issue-A"], Is.EqualTo(PullRequestStatus.Merged));
        Assert.That(cached.IssuePrStatuses["issue-B"], Is.EqualTo(PullRequestStatus.Conflict));
        Assert.That(cached.CachedAt, Is.Not.EqualTo(default(DateTime)));
    }

    #endregion

    #region Enum Serialization Tests

    [Test]
    public async Task CachePRDataAsync_StatusEnumsSerializedAsStrings()
    {
        // Arrange
        var pr = new PullRequestInfo
        {
            Number = 1,
            Title = "Test",
            Status = PullRequestStatus.ReadyForMerging,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Act
        await _service.CachePRDataAsync("project-1", _projectLocalPath, [pr], []);

        // Assert - Check the raw file content for string enum values
        var cacheFile = GraphCacheService.GetCacheFilePath(_projectLocalPath);
        var content = File.ReadAllText(cacheFile);
        Assert.That(content, Does.Contain("readyForMerging"), "Status should be serialized as camelCase string");
    }

    #endregion

    #region Helper Methods

    private static List<PullRequestInfo> CreateOpenPrs(int count)
    {
        return Enumerable.Range(1, count).Select(i => new PullRequestInfo
        {
            Number = i,
            Title = $"Open PR #{i}",
            Status = PullRequestStatus.InProgress,
            BranchName = $"feature/pr-{i}",
            CreatedAt = DateTime.UtcNow.AddDays(-i),
            UpdatedAt = DateTime.UtcNow
        }).ToList();
    }

    private static List<PullRequestInfo> CreateClosedPrs(int count)
    {
        return Enumerable.Range(100, count).Select(i => new PullRequestInfo
        {
            Number = i,
            Title = $"Closed PR #{i}",
            Status = PullRequestStatus.Merged,
            BranchName = $"feature/merged-{i}",
            CreatedAt = DateTime.UtcNow.AddDays(-i),
            MergedAt = DateTime.UtcNow.AddDays(-1),
            UpdatedAt = DateTime.UtcNow
        }).ToList();
    }

    #endregion
}
