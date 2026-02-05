using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Homespun.Features.PullRequests;

namespace Homespun.Features.Gitgraph.Services;

/// <summary>
/// File-based cache service for graph PR data using JSONL files.
/// Stores cached PR data in pull_requests.jsonl files alongside each project's data directory
/// (data/src/{project}/pull_requests.jsonl). This ensures the cache persists across server restarts
/// and loads quickly from disk without needing GitHub API calls.
///
/// Each line in the JSONL file is a serialized PullRequestInfo with metadata.
/// The file is always the source of truth - memory cache is just for fast access.
/// </summary>
public class GraphCacheService : IGraphCacheService
{
    private const string CacheFileName = "pull_requests.jsonl";

    private readonly ILogger<GraphCacheService> _logger;
    private readonly ConcurrentDictionary<string, CachedPRData> _memoryCache = new();
    private readonly ConcurrentDictionary<string, string> _projectCachePaths = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _projectLocks = new();

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public GraphCacheService(ILogger<GraphCacheService> logger)
    {
        _logger = logger;
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
    public async Task CachePRDataAsync(string projectId, string projectLocalPath, List<PullRequestInfo> openPrs, List<PullRequestInfo> closedPrs)
    {
        var cachedData = new CachedPRData
        {
            OpenPrs = openPrs,
            ClosedPrs = closedPrs,
            CachedAt = DateTime.UtcNow
        };

        _memoryCache[projectId] = cachedData;

        // Persist to JSONL file in the project directory
        await PersistToJsonlAsync(projectId, projectLocalPath, cachedData);

        _logger.LogInformation(
            "Cached PR data for project {ProjectId}: {OpenCount} open, {ClosedCount} closed",
            projectId, openPrs.Count, closedPrs.Count);
    }

    /// <inheritdoc />
    public async Task InvalidateCacheAsync(string projectId)
    {
        _memoryCache.TryRemove(projectId, out _);

        if (_projectCachePaths.TryGetValue(projectId, out var cachePath) && File.Exists(cachePath))
        {
            var projectLock = GetProjectLock(projectId);
            await projectLock.WaitAsync();
            try
            {
                File.Delete(cachePath);
                _projectCachePaths.TryRemove(projectId, out _);
                _logger.LogInformation("Invalidated cache for project {ProjectId}", projectId);
            }
            finally
            {
                projectLock.Release();
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

    /// <inheritdoc />
    public async Task CachePRDataWithStatusesAsync(
        string projectId,
        string projectLocalPath,
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

        // Persist to JSONL file in the project directory
        await PersistToJsonlAsync(projectId, projectLocalPath, cachedData);

        _logger.LogInformation(
            "Cached PR data with statuses for project {ProjectId}: {OpenCount} open, {ClosedCount} closed, {StatusCount} statuses",
            projectId, openPrs.Count, closedPrs.Count, issuePrStatuses.Count);
    }

    /// <inheritdoc />
    public void LoadCacheForProject(string projectId, string projectLocalPath)
    {
        // Already loaded?
        if (_memoryCache.ContainsKey(projectId))
            return;

        var cacheFile = GetCacheFilePath(projectLocalPath);
        if (!File.Exists(cacheFile))
        {
            _logger.LogDebug("No cache file found for project {ProjectId} at {CacheFile}", projectId, cacheFile);
            return;
        }

        try
        {
            var cachedData = ReadJsonlFile(cacheFile);
            if (cachedData != null)
            {
                _memoryCache[projectId] = cachedData;
                _projectCachePaths[projectId] = cacheFile;
                _logger.LogDebug(
                    "Loaded cache from disk for project {ProjectId}: {OpenCount} open, {ClosedCount} closed, cached at {CachedAt}",
                    projectId, cachedData.OpenPrs.Count, cachedData.ClosedPrs.Count, cachedData.CachedAt);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load cache file for project {ProjectId} at {CacheFile}", projectId, cacheFile);
        }
    }

    /// <summary>
    /// Gets the path to the JSONL cache file for a project.
    /// The file is stored in the project's parent directory (data/src/{project}/pull_requests.jsonl).
    /// The project LocalPath is typically data/src/{project}/{branch}, so we go up one level.
    /// </summary>
    internal static string GetCacheFilePath(string projectLocalPath)
    {
        var projectDir = Directory.GetParent(projectLocalPath)?.FullName ?? projectLocalPath;
        return Path.Combine(projectDir, CacheFileName);
    }

    /// <summary>
    /// Persists cached data to a JSONL file.
    /// Format: First line is metadata (cachedAt, issuePrStatuses), subsequent lines are PR entries.
    /// Each PR line has a "type" field indicating "open" or "closed".
    /// </summary>
    private async Task PersistToJsonlAsync(string projectId, string projectLocalPath, CachedPRData data)
    {
        var cacheFile = GetCacheFilePath(projectLocalPath);
        _projectCachePaths[projectId] = cacheFile;

        var projectLock = GetProjectLock(projectId);
        await projectLock.WaitAsync();
        try
        {
            // Ensure directory exists
            var dir = Path.GetDirectoryName(cacheFile);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            using var writer = new StreamWriter(cacheFile, append: false);

            // Write metadata line first
            var metadata = new CacheMetadataLine
            {
                Type = "metadata",
                CachedAt = data.CachedAt,
                IssuePrStatuses = data.IssuePrStatuses
            };
            await writer.WriteLineAsync(JsonSerializer.Serialize(metadata, JsonOptions));

            // Write each open PR as a JSONL line
            foreach (var pr in data.OpenPrs)
            {
                var line = new CachePrLine
                {
                    Type = "open",
                    Pr = pr
                };
                await writer.WriteLineAsync(JsonSerializer.Serialize(line, JsonOptions));
            }

            // Write each closed PR as a JSONL line
            foreach (var pr in data.ClosedPrs)
            {
                var line = new CachePrLine
                {
                    Type = "closed",
                    Pr = pr
                };
                await writer.WriteLineAsync(JsonSerializer.Serialize(line, JsonOptions));
            }

            _logger.LogDebug("Persisted cache to JSONL for project {ProjectId} at {CacheFile}", projectId, cacheFile);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist cache to JSONL for project {ProjectId}", projectId);
        }
        finally
        {
            projectLock.Release();
        }
    }

    /// <summary>
    /// Reads a JSONL cache file and reconstructs CachedPRData.
    /// </summary>
    private CachedPRData? ReadJsonlFile(string cacheFile)
    {
        var lines = File.ReadAllLines(cacheFile);
        if (lines.Length == 0)
            return null;

        var openPrs = new List<PullRequestInfo>();
        var closedPrs = new List<PullRequestInfo>();
        DateTime cachedAt = default;
        Dictionary<string, PullRequestStatus> issuePrStatuses = new();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            try
            {
                // Peek at the type field to determine how to deserialize
                using var doc = JsonDocument.Parse(line);
                var type = doc.RootElement.GetProperty("type").GetString();

                switch (type)
                {
                    case "metadata":
                        var metadata = JsonSerializer.Deserialize<CacheMetadataLine>(line, JsonOptions);
                        if (metadata != null)
                        {
                            cachedAt = metadata.CachedAt;
                            issuePrStatuses = metadata.IssuePrStatuses ?? new();
                        }
                        break;

                    case "open":
                        var openLine = JsonSerializer.Deserialize<CachePrLine>(line, JsonOptions);
                        if (openLine?.Pr != null)
                            openPrs.Add(openLine.Pr);
                        break;

                    case "closed":
                        var closedLine = JsonSerializer.Deserialize<CachePrLine>(line, JsonOptions);
                        if (closedLine?.Pr != null)
                            closedPrs.Add(closedLine.Pr);
                        break;

                    default:
                        _logger.LogDebug("Unknown line type '{Type}' in cache file {CacheFile}", type, cacheFile);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to parse line in cache file {CacheFile}: {Line}", cacheFile, line);
            }
        }

        return new CachedPRData
        {
            OpenPrs = openPrs,
            ClosedPrs = closedPrs,
            IssuePrStatuses = issuePrStatuses,
            CachedAt = cachedAt
        };
    }

    private SemaphoreSlim GetProjectLock(string projectId)
    {
        return _projectLocks.GetOrAdd(projectId, _ => new SemaphoreSlim(1, 1));
    }

    /// <summary>
    /// Metadata line in the JSONL cache file.
    /// Always the first line, contains cache timestamp and issue-PR status mappings.
    /// </summary>
    internal class CacheMetadataLine
    {
        public string Type { get; set; } = "metadata";
        public DateTime CachedAt { get; set; }
        public Dictionary<string, PullRequestStatus>? IssuePrStatuses { get; set; }
    }

    /// <summary>
    /// PR entry line in the JSONL cache file.
    /// Type is either "open" or "closed" to indicate the PR state.
    /// </summary>
    internal class CachePrLine
    {
        public string Type { get; set; } = "";
        public PullRequestInfo Pr { get; set; } = null!;
    }
}
