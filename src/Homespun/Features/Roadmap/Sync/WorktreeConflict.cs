namespace Homespun.Features.Roadmap.Sync;

/// <summary>
/// Represents a conflict in a worktree that needs resolution.
/// </summary>
public class WorktreeConflict
{
    /// <summary>
    /// Path to the worktree with the conflict.
    /// </summary>
    public required string WorktreePath { get; init; }

    /// <summary>
    /// Branch name of the worktree.
    /// </summary>
    public required string BranchName { get; init; }

    /// <summary>
    /// Associated pull request ID, if any.
    /// </summary>
    public string? PullRequestId { get; init; }

    /// <summary>
    /// Pull request title for display.
    /// </summary>
    public string? PullRequestTitle { get; init; }

    /// <summary>
    /// Description of the conflict.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// When the conflict was detected.
    /// </summary>
    public DateTime DetectedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Whether this conflict has been resolved.
    /// </summary>
    public bool IsResolved { get; set; }

    /// <summary>
    /// How this conflict was resolved, if resolved.
    /// </summary>
    public ConflictResolution? Resolution { get; set; }
}
