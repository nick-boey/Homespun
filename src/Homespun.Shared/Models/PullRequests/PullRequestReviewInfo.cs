namespace Homespun.Shared.Models.PullRequests;

/// <summary>
/// Represents a PR review from GitHub.
/// </summary>
public class PullRequestReviewInfo
{
    public long Id { get; set; }
    public required string User { get; set; }
    public required string State { get; set; }
    public string? Body { get; set; }
    public DateTime SubmittedAt { get; set; }

    /// <summary>
    /// Whether this is an approval.
    /// </summary>
    public bool IsApproval => State.Equals("APPROVED", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether this is a changes requested review.
    /// </summary>
    public bool IsChangesRequested => State.Equals("CHANGES_REQUESTED", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether this is a comment-only review (no approval/rejection).
    /// </summary>
    public bool IsComment => State.Equals("COMMENTED", StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Summary of reviews on a PR.
/// </summary>
public class ReviewSummary
{
    public int TotalReviews { get; set; }
    public int Approvals { get; set; }
    public int ChangesRequested { get; set; }
    public int Comments { get; set; }
    public List<PullRequestReviewInfo> Reviews { get; set; } = [];
    public DateTime? LastReviewAt { get; set; }

    /// <summary>
    /// Whether the PR has any pending review feedback that needs addressing.
    /// </summary>
    public bool NeedsAction => ChangesRequested > 0;

    /// <summary>
    /// Whether the PR is approved (at least one approval and no changes requested).
    /// </summary>
    public bool IsApproved => Approvals > 0 && ChangesRequested == 0;
}
