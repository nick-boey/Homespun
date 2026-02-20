using Homespun.Shared.Models.Fleece;

namespace Homespun.Shared.Models.PullRequests;

/// <summary>
/// Details for a merged pull request including linked issue information.
/// Used for displaying merged PR details in the UI.
/// </summary>
public class MergedPullRequestDetails
{
    /// <summary>
    /// The pull request information from GitHub.
    /// </summary>
    public required PullRequestInfo PullRequest { get; set; }

    /// <summary>
    /// The ID of the linked Fleece issue, extracted from the branch name.
    /// Null if no issue ID was found in the branch name.
    /// </summary>
    public string? LinkedIssueId { get; set; }

    /// <summary>
    /// The full issue details if the linked issue was found.
    /// Null if no issue was linked or the issue couldn't be loaded.
    /// </summary>
    public IssueResponse? LinkedIssue { get; set; }
}
