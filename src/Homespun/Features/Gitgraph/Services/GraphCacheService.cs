using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Homespun.Features.PullRequests;

namespace Homespun.Features.Gitgraph.Services;

/// <summary>
/// File-based cache service for graph PR data.
/// Stores cached PR data both in memory and on disk for persistence across app restarts.
/// </summary>
public class GraphCacheService : IGraphCacheService
{
    private readonly string _cacheDirectory;
    private readonly ILogger<GraphCacheService> _logger;
    private readonly ConcurrentDictionary<string, CachedPRData> _memoryCache = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public GraphCacheService(string cacheDirectory, ILogger<GraphCacheService> logger)
    {
        _cacheDirectory = cacheDirectory;
        _logger = logger;

        // Ensure cache directory exists
        if (!Directory.Exists(_cacheDirectory))
        {
            Directory.CreateDirectory(_cacheDirectory);
        }

        // Load existing cache files into memory
        LoadCacheFromDisk();
    }

    /// <inheritdoc />
    public CachedPRData? GetCachedPRData(string projectId)
    {
        if (_memoryCache.TryGetValue(projectId, out var cached))
        {
            _logger.LogDebug("Cache hit for project {ProjectId}, cached at {CachedAt}", projectId, cached.CachedAt);
            return cached;
        }

        _logger.LogDebug("Cache miss for project {ProjectId}", projectId);
        return null;
    }

    /// <inheritdoc />
    public async Task CachePRDataAsync(string projectId, List<PullRequestInfo> openPrs, List<PullRequestInfo> closedPrs)
    {
        var cachedData = new CachedPRData
        {
            OpenPrs = openPrs,
            ClosedPrs = closedPrs,
            CachedAt = DateTime.UtcNow
        };

        _memoryCache[projectId] = cachedData;

        // Persist to disk asynchronously
        await PersistToDiskAsync(projectId, cachedData);

        _logger.LogInformation(
            "Cached PR data for project {ProjectId}: {OpenCount} open, {ClosedCount} closed",
            projectId, openPrs.Count, closedPrs.Count);
    }

    /// <inheritdoc />
    public async Task InvalidateCacheAsync(string projectId)
    {
        _memoryCache.TryRemove(projectId, out _);

        var cacheFile = GetCacheFilePath(projectId);
        if (File.Exists(cacheFile))
        {
            await _lock.WaitAsync();
            try
            {
                File.Delete(cacheFile);
                _logger.LogInformation("Invalidated cache for project {ProjectId}", projectId);
            }
            finally
            {
                _lock.Release();
            }
        }
    }

    /// <inheritdoc />
    public DateTime? GetCacheTimestamp(string projectId)
    {
        if (_memoryCache.TryGetValue(projectId, out var cached))
        {
            return cached.CachedAt;
        }
        return null;
    }

    private string GetCacheFilePath(string projectId)
    {
        // Sanitize project ID for use as filename
        var safeProjectId = string.Join("_", projectId.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_cacheDirectory, $"graph-cache-{safeProjectId}.json");
    }

    private void LoadCacheFromDisk()
    {
        try
        {
            var cacheFiles = Directory.GetFiles(_cacheDirectory, "graph-cache-*.json");
            foreach (var cacheFile in cacheFiles)
            {
                try
                {
                    var json = File.ReadAllText(cacheFile);
                    var wrapper = JsonSerializer.Deserialize<CacheFileWrapper>(json, JsonOptions);
                    if (wrapper != null)
                    {
                        _memoryCache[wrapper.ProjectId] = wrapper.Data;
                        _logger.LogDebug(
                            "Loaded cache from disk for project {ProjectId}, cached at {CachedAt}",
                            wrapper.ProjectId, wrapper.Data.CachedAt);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load cache file {CacheFile}", cacheFile);
                }
            }

            _logger.LogInformation("Loaded {Count} cached graph entries from disk", _memoryCache.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load cache from disk");
        }
    }

    /// <inheritdoc />
    public async Task CachePRDataWithStatusesAsync(
        string projectId,
        List<PullRequestInfo> openPrs,
        List<PullRequestInfo> closedPrs,
        Dictionary<string, PullRequestStatus> issuePrStatuses)
    {
        var cachedData = new CachedPRData
        {
            OpenPrs = openPrs,
            ClosedPrs = closedPrs,
            IssuePrStatuses = issuePrStatuses,
            CachedAt = DateTime.UtcNow
        };

        _memoryCache[projectId] = cachedData;

        // Persist to disk asynchronously
        await PersistToDiskAsync(projectId, cachedData);

        _logger.LogInformation(
            "Cached PR data with statuses for project {ProjectId}: {OpenCount} open, {ClosedCount} closed, {StatusCount} statuses",
            projectId, openPrs.Count, closedPrs.Count, issuePrStatuses.Count);
    }

    private async Task PersistToDiskAsync(string projectId, CachedPRData data)
    {
        await _lock.WaitAsync();
        try
        {
            var cacheFile = GetCacheFilePath(projectId);
            var wrapper = new CacheFileWrapper
            {
                ProjectId = projectId,
                Data = data
            };
            var json = JsonSerializer.Serialize(wrapper, JsonOptions);
            await File.WriteAllTextAsync(cacheFile, json);
            _logger.LogDebug("Persisted cache to disk for project {ProjectId}", projectId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist cache to disk for project {ProjectId}", projectId);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Wrapper for persisting cache data with project ID.
    /// </summary>
    private class CacheFileWrapper
    {
        public string ProjectId { get; set; } = "";
        public CachedPRData Data { get; set; } = new();
    }
}
