using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Git;

/// <summary>
/// Service for caching merge status to avoid expensive git operations.
/// Cache is stored in JSON files with a 1-hour expiry.
/// </summary>
public class MergeStatusCacheService(
    IGitCloneService gitCloneService,
    ILogger<MergeStatusCacheService> logger)
    : IMergeStatusCacheService
{
    private static readonly TimeSpan CacheExpiry = TimeSpan.FromHours(1);
    private static readonly string CacheDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".homespun", "cache", "merge-status");

    // In-memory cache to avoid file reads
    private readonly Dictionary<string, MergeStatusCache> _memoryCache = new();
    private readonly object _lock = new();

    public async Task<MergeStatus> GetMergeStatusAsync(string repoPath, string branchName, string targetBranch)
    {
        var cache = await LoadCacheAsync(repoPath, targetBranch);

        // Check if we have a valid cached entry
        if (cache.Branches.TryGetValue(branchName, out var status))
        {
            if (DateTime.UtcNow - status.CheckedAt < CacheExpiry)
            {
                logger.LogDebug("Cache hit for branch {BranchName} merge status", branchName);
                return status;
            }
            logger.LogDebug("Cache expired for branch {BranchName}", branchName);
        }

        // Compute merge status
        logger.LogDebug("Computing merge status for branch {BranchName}", branchName);
        status = new MergeStatus
        {
            IsMerged = await gitCloneService.IsBranchMergedAsync(repoPath, branchName, targetBranch),
            IsSquashMerged = false, // Only compute if not already merged
            CheckedAt = DateTime.UtcNow
        };

        // Only check squash-merged if not already regular-merged
        if (!status.IsMerged)
        {
            status.IsSquashMerged = await gitCloneService.IsSquashMergedAsync(repoPath, branchName, targetBranch);
        }

        // Update cache
        cache.Branches[branchName] = status;
        cache.LastUpdated = DateTime.UtcNow;
        await SaveCacheAsync(repoPath, targetBranch, cache);

        return status;
    }

    public void InvalidateBranch(string repoPath, string branchName)
    {
        var cacheKey = GetCacheKey(repoPath);
        lock (_lock)
        {
            if (_memoryCache.TryGetValue(cacheKey, out var cache))
            {
                cache.Branches.Remove(branchName);
            }
        }

        // Also remove from file cache
        var cacheFile = GetCacheFilePath(repoPath);
        if (File.Exists(cacheFile))
        {
            try
            {
                var json = File.ReadAllText(cacheFile);
                var fileCache = JsonSerializer.Deserialize<MergeStatusCache>(json);
                if (fileCache != null)
                {
                    fileCache.Branches.Remove(branchName);
                    File.WriteAllText(cacheFile, JsonSerializer.Serialize(fileCache));
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to invalidate branch {BranchName} from file cache", branchName);
            }
        }
    }

    public void InvalidateRepository(string repoPath)
    {
        var cacheKey = GetCacheKey(repoPath);
        lock (_lock)
        {
            _memoryCache.Remove(cacheKey);
        }

        var cacheFile = GetCacheFilePath(repoPath);
        try
        {
            if (File.Exists(cacheFile))
            {
                File.Delete(cacheFile);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to delete cache file for {RepoPath}", repoPath);
        }
    }

    private async Task<MergeStatusCache> LoadCacheAsync(string repoPath, string targetBranch)
    {
        var cacheKey = GetCacheKey(repoPath);

        // Check memory cache first
        lock (_lock)
        {
            if (_memoryCache.TryGetValue(cacheKey, out var memCache))
            {
                // Verify target branch matches
                if (memCache.TargetBranch == targetBranch)
                {
                    return memCache;
                }
                // Target branch changed, invalidate cache
                _memoryCache.Remove(cacheKey);
            }
        }

        // Try to load from file
        var cacheFile = GetCacheFilePath(repoPath);
        if (File.Exists(cacheFile))
        {
            try
            {
                var json = await File.ReadAllTextAsync(cacheFile);
                var cache = JsonSerializer.Deserialize<MergeStatusCache>(json);
                if (cache != null && cache.TargetBranch == targetBranch)
                {
                    lock (_lock)
                    {
                        _memoryCache[cacheKey] = cache;
                    }
                    return cache;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load cache file {CacheFile}", cacheFile);
            }
        }

        // Create new cache
        var newCache = new MergeStatusCache
        {
            RepoPath = repoPath,
            TargetBranch = targetBranch,
            LastUpdated = DateTime.UtcNow
        };

        lock (_lock)
        {
            _memoryCache[cacheKey] = newCache;
        }

        return newCache;
    }

    private async Task SaveCacheAsync(string repoPath, string targetBranch, MergeStatusCache cache)
    {
        var cacheFile = GetCacheFilePath(repoPath);
        var cacheKey = GetCacheKey(repoPath);

        try
        {
            var directory = Path.GetDirectoryName(cacheFile);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(cacheFile, json);

            lock (_lock)
            {
                _memoryCache[cacheKey] = cache;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to save cache file {CacheFile}", cacheFile);
        }
    }

    private static string GetCacheFilePath(string repoPath)
    {
        var hash = GetRepoHash(repoPath);
        return Path.Combine(CacheDirectory, $"{hash}.json");
    }

    private static string GetCacheKey(string repoPath)
    {
        return repoPath.ToLowerInvariant();
    }

    private static string GetRepoHash(string repoPath)
    {
        var bytes = Encoding.UTF8.GetBytes(repoPath.ToLowerInvariant());
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes)[..16].ToLowerInvariant();
    }
}
