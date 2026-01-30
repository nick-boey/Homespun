using Homespun.Features.PullRequests;

namespace Homespun.Features.Gitgraph.Services;

/// <summary>
/// Service for caching graph PR data to improve page load times.
/// PR data is expensive to fetch from GitHub API, so we cache it locally
/// and update asynchronously after displaying the cached version.
/// </summary>
public interface IGraphCacheService
{
    /// <summary>
    /// Gets cached PR data for a project.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <returns>Cached PR data, or null if not cached.</returns>
    CachedPRData? GetCachedPRData(string projectId);

    /// <summary>
    /// Caches PR data for a project.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="openPrs">The open PRs from GitHub.</param>
    /// <param name="closedPrs">The closed PRs from GitHub.</param>
    Task CachePRDataAsync(string projectId, List<PullRequestInfo> openPrs, List<PullRequestInfo> closedPrs);

    /// <summary>
    /// Invalidates the cache for a project (e.g., on manual refresh).
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    Task InvalidateCacheAsync(string projectId);

    /// <summary>
    /// Gets the timestamp of when the cache was last updated for a project.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <returns>The cache timestamp, or null if not cached.</returns>
    DateTime? GetCacheTimestamp(string projectId);
}

/// <summary>
/// Cached PR data for a project.
/// </summary>
public class CachedPRData
{
    /// <summary>
    /// Open PRs from GitHub.
    /// </summary>
    public List<PullRequestInfo> OpenPrs { get; set; } = [];

    /// <summary>
    /// Closed/merged PRs from GitHub.
    /// </summary>
    public List<PullRequestInfo> ClosedPrs { get; set; } = [];

    /// <summary>
    /// When the cache was last updated.
    /// </summary>
    public DateTime CachedAt { get; set; }
}
