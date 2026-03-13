using Homespun.Shared.Models.PullRequests;

namespace Homespun.Features.Search;

/// <summary>
/// Service for retrieving searchable PR summaries from a project.
/// </summary>
public interface ISearchablePrService
{
    /// <summary>
    /// Gets all open and recently merged PRs for searching.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <returns>A result containing the PR list and a content hash</returns>
    Task<PrListResult> GetPrsAsync(string projectId);
}

/// <summary>
/// Interface for fetching PR data for search functionality.
/// Abstraction over PullRequestWorkflowService for testability.
/// </summary>
public interface IPrDataProvider
{
    /// <summary>
    /// Gets open pull requests with status.
    /// </summary>
    Task<List<PullRequestWithStatus>> GetOpenPullRequestsWithStatusAsync(string projectId);

    /// <summary>
    /// Gets merged pull requests with time.
    /// </summary>
    Task<List<PullRequestWithTime>> GetMergedPullRequestsWithTimeAsync(string projectId);
}

/// <summary>
/// Result of fetching PRs from a project.
/// </summary>
/// <param name="Prs">List of searchable PR summaries sorted by number</param>
/// <param name="Hash">SHA256 hash of the PR list for cache invalidation</param>
public record PrListResult(IReadOnlyList<SearchablePr> Prs, string Hash);

/// <summary>
/// Minimal PR data for search results.
/// </summary>
/// <param name="Number">GitHub PR number</param>
/// <param name="Title">PR title</param>
/// <param name="BranchName">PR source branch name</param>
public record SearchablePr(int Number, string Title, string? BranchName);
