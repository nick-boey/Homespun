using Fleece.Core.Models;
using Homespun.Shared.Models.Fleece;

namespace Homespun.Features.Gitgraph.Services;

/// <summary>
/// Service for building graph data from BeadsIssues and PullRequests.
/// </summary>
public interface IGraphService
{
    /// <summary>
    /// Builds a complete graph for a project.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="maxPastPRs">Maximum number of past (closed/merged) PRs to show. If null, shows all. Default is 5.</param>
    Task<Graph> BuildGraphAsync(string projectId, int? maxPastPRs = 5);

    /// <summary>
    /// Builds graph JSON data for a project, ready for timeline visualization.
    /// Uses cached PR data if available for faster initial load.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="maxPastPRs">Maximum number of past (closed/merged) PRs to show. If null, shows all. Default is 5.</param>
    /// <param name="useCache">If true, uses cached PR data if available. Default is true.</param>
    Task<GitgraphJsonData> BuildGraphJsonAsync(string projectId, int? maxPastPRs = 5, bool useCache = true);

    /// <summary>
    /// Builds graph JSON data with fresh PR data from GitHub (bypasses cache).
    /// Also updates the cache with the fresh data.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="maxPastPRs">Maximum number of past (closed/merged) PRs to show. If null, shows all. Default is 5.</param>
    Task<GitgraphJsonData> BuildGraphJsonWithFreshDataAsync(string projectId, int? maxPastPRs = 5);

    /// <summary>
    /// Gets the timestamp of when the PR data was last cached for a project.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <returns>The cache timestamp, or null if not cached.</returns>
    DateTime? GetCacheTimestamp(string projectId);

    /// <summary>
    /// Checks if cached PR data is available for a project.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    bool HasCachedData(string projectId);

    /// <summary>
    /// Builds graph using ONLY cached data. No GitHub API calls are made.
    /// Used for immediate initial render before fresh data is fetched.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="maxPastPRs">Maximum number of past (closed/merged) PRs to show. If null, shows all. Default is 5.</param>
    /// <returns>Graph data from cache, or null if no cache exists.</returns>
    Task<Graph?> BuildGraphFromCacheOnlyAsync(string projectId, int? maxPastPRs = 5);

    /// <summary>
    /// Builds graph JSON data using ONLY cached data. No GitHub API calls are made.
    /// Used for immediate initial render before fresh data is fetched.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="maxPastPRs">Maximum number of past (closed/merged) PRs to show. If null, shows all. Default is 5.</param>
    /// <returns>Graph JSON data from cache, or null if no cache exists.</returns>
    Task<GitgraphJsonData?> BuildGraphJsonFromCacheOnlyAsync(string projectId, int? maxPastPRs = 5);

    /// <summary>
    /// Builds a task graph for a project using Fleece.Core's TaskGraphService.
    /// The task graph displays issues with actionable items on the left (lane 0)
    /// and parent/blocking issues on the right (higher lanes).
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <returns>Task graph data, or null if no issues exist.</returns>
    Task<TaskGraph?> BuildTaskGraphAsync(string projectId);

    /// <summary>
    /// Builds a plain text rendering of the task graph for a project.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <returns>Text rendering of the task graph, or null if no issues exist.</returns>
    Task<string?> BuildTaskGraphTextAsync(string projectId);

    /// <summary>
    /// Builds an enhanced task graph response including merged PRs, agent statuses,
    /// and linked PR information for the UI.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="maxPastPRs">Maximum number of past (closed/merged) PRs to show. Default is 5.</param>
    /// <returns>Enhanced task graph response, or null if no issues exist.</returns>
    Task<TaskGraphResponse?> BuildEnhancedTaskGraphAsync(string projectId, int maxPastPRs = 5);
}
