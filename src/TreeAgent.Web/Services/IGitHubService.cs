using TreeAgent.Web.Data.Entities;

namespace TreeAgent.Web.Services;

/// <summary>
/// Represents a GitHub pull request
/// </summary>
public class GitHubPullRequest
{
    public int Number { get; set; }
    public required string Title { get; set; }
    public string? Body { get; set; }
    public required string State { get; set; } // "open", "closed"
    public bool Merged { get; set; }
    public required string BranchName { get; set; }
    public string? HtmlUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? MergedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
}

/// <summary>
/// Result of a sync operation
/// </summary>
public class SyncResult
{
    public int Imported { get; set; }
    public int Updated { get; set; }
    public List<string> Errors { get; set; } = [];
}

/// <summary>
/// Service for interacting with GitHub API
/// </summary>
public interface IGitHubService
{
    /// <summary>
    /// Check if GitHub is configured for the project
    /// </summary>
    Task<bool> IsConfiguredAsync(string projectId);

    /// <summary>
    /// Fetch all open pull requests
    /// </summary>
    Task<List<GitHubPullRequest>> GetOpenPullRequestsAsync(string projectId);

    /// <summary>
    /// Fetch all closed/merged pull requests
    /// </summary>
    Task<List<GitHubPullRequest>> GetClosedPullRequestsAsync(string projectId);

    /// <summary>
    /// Get a specific pull request by number
    /// </summary>
    Task<GitHubPullRequest?> GetPullRequestAsync(string projectId, int prNumber);

    /// <summary>
    /// Create a pull request from a feature branch
    /// </summary>
    Task<GitHubPullRequest?> CreatePullRequestAsync(string projectId, string featureId);

    /// <summary>
    /// Push a branch to the remote and create a PR
    /// </summary>
    Task<bool> PushBranchAsync(string projectId, string branchName);

    /// <summary>
    /// Sync pull requests with features - imports PRs as features and updates existing feature statuses
    /// </summary>
    Task<SyncResult> SyncPullRequestsAsync(string projectId);

    /// <summary>
    /// Link a pull request number to a feature
    /// </summary>
    Task<bool> LinkPullRequestAsync(string featureId, int prNumber);
}
