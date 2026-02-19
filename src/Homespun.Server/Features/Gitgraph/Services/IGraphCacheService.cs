
namespace Homespun.Features.Gitgraph.Services;

/// <summary>
/// Service for caching graph PR data to improve page load times.
/// PR data is expensive to fetch from GitHub API, so we cache it to JSONL files
/// stored alongside the project data (data/src/{project}/pull_requests.jsonl).
/// Cache is always loaded from disk for fast startup, and PR updates are fetched in the background.
/// </summary>
public interface IGraphCacheService
{
    /// <summary>
    /// Gets cached PR data for a project.
    /// Loads from the JSONL file at {projectLocalPath}/../pull_requests.jsonl.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <returns>Cached PR data, or null if not cached.</returns>
    CachedPRData? GetCachedPRData(string projectId);

    /// <summary>
    /// Caches PR data for a project to a JSONL file.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="projectLocalPath">The project's local path (used to derive the cache file location).</param>
    /// <param name="openPrs">The open PRs from GitHub.</param>
    /// <param name="closedPrs">The closed PRs from GitHub.</param>
    Task CachePRDataAsync(string projectId, string projectLocalPath, List<PullRequestInfo> openPrs, List<PullRequestInfo> closedPrs);

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

    /// <summary>
    /// Caches PR data including PR statuses for a project to a JSONL file.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="projectLocalPath">The project's local path (used to derive the cache file location).</param>
    /// <param name="openPrs">The open PRs from GitHub.</param>
    /// <param name="closedPrs">The closed PRs from GitHub.</param>
    /// <param name="issuePrStatuses">PR status for issues with linked PRs.</param>
    Task CachePRDataWithStatusesAsync(string projectId, string projectLocalPath, List<PullRequestInfo> openPrs, List<PullRequestInfo> closedPrs, Dictionary<string, PullRequestStatus> issuePrStatuses);

    /// <summary>
    /// Loads cache from disk for a project (called when a project is first accessed).
    /// This ensures the cache is always warm from disk, even after server restart.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="projectLocalPath">The project's local path.</param>
    void LoadCacheForProject(string projectId, string projectLocalPath);

    /// <summary>
    /// Updates a PR's status in the cache, moving it from open to closed list.
    /// Used when a PR is merged or closed on GitHub.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="projectLocalPath">The project's local path.</param>
    /// <param name="prNumber">The PR number to update.</param>
    /// <param name="newStatus">The new status (Merged or Closed).</param>
    /// <param name="mergedAt">When the PR was merged (if merged).</param>
    /// <param name="closedAt">When the PR was closed (if closed without merge).</param>
    /// <param name="issueId">Optional issue ID to update status for.</param>
    Task UpdatePRStatusAsync(
        string projectId,
        string projectLocalPath,
        int prNumber,
        PullRequestStatus newStatus,
        DateTime? mergedAt = null,
        DateTime? closedAt = null,
        string? issueId = null);
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
    /// PR status for issues that have linked PRs.
    /// Key is the issue ID, value is the PR status.
    /// </summary>
    public Dictionary<string, PullRequestStatus> IssuePrStatuses { get; set; } = new();

    /// <summary>
    /// When the cache was last updated.
    /// </summary>
    public DateTime CachedAt { get; set; }
}
