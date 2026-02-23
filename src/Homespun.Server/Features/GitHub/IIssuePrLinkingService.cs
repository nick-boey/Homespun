using Homespun.Shared.Models.PullRequests;

namespace Homespun.Features.GitHub;

/// <summary>
/// Service for linking beads issues to pull requests.
/// </summary>
public interface IIssuePrLinkingService
{
    /// <summary>
    /// Links a pull request to a beads issue by setting the BeadsIssueId
    /// and adding the hsp:pr-{prNumber} label to the issue.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="pullRequestId">The pull request ID.</param>
    /// <param name="issueId">The beads issue ID to link.</param>
    /// <param name="prNumber">The GitHub PR number for the label.</param>
    /// <returns>True if linking succeeded, false otherwise.</returns>
    Task<bool> LinkPullRequestToIssueAsync(string projectId, string pullRequestId, string issueId, int prNumber);

    /// <summary>
    /// Attempts to link a pull request to a beads issue by extracting the issue ID
    /// from the branch name.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="pullRequestId">The pull request ID.</param>
    /// <returns>The linked issue ID if successful, null otherwise.</returns>
    Task<string?> TryLinkByBranchNameAsync(string projectId, string pullRequestId);

    /// <summary>
    /// Closes the beads issue linked to a pull request.
    /// Used when a PR is merged or closed.
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="pullRequestId">The pull request ID.</param>
    /// <param name="reason">The reason for closing (e.g., "PR #123 merged").</param>
    /// <returns>True if the issue was closed, false if no linked issue or close failed.</returns>
    Task<bool> CloseLinkedIssueAsync(string projectId, string pullRequestId, string? reason = null);

    /// <summary>
    /// Updates a Fleece issue status based on the associated PR status.
    /// Maps PR status to issue status:
    /// - Open PR (InProgress, ReadyForReview, ChecksFailing, Conflict, ReadyForMerging) → Review
    /// - Merged PR → Complete
    /// - Closed PR → Closed
    /// </summary>
    /// <param name="projectId">The project ID.</param>
    /// <param name="issueId">The Fleece issue ID to update.</param>
    /// <param name="prStatus">The PR status to map from.</param>
    /// <param name="prNumber">The GitHub PR number.</param>
    /// <returns>True if the issue was updated, false if already in correct status or update failed.</returns>
    Task<bool> UpdateIssueStatusFromPRAsync(string projectId, string issueId, PullRequestStatus prStatus, int prNumber);
}
