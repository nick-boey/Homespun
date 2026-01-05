namespace Homespun.Features.Roadmap.Sync;

/// <summary>
/// Status of a sync operation for a single worktree.
/// </summary>
public class WorktreeSyncStatus
{
    /// <summary>
    /// Path to the worktree.
    /// </summary>
    public required string WorktreePath { get; init; }

    /// <summary>
    /// Branch name of the worktree.
    /// </summary>
    public required string BranchName { get; init; }

    /// <summary>
    /// Result status of the sync operation.
    /// </summary>
    public required SyncStatus Status { get; init; }

    /// <summary>
    /// Details about a conflict if Status is ConflictNeedsResolution.
    /// </summary>
    public string? ConflictDetails { get; init; }

    /// <summary>
    /// Error message if Status is Error.
    /// </summary>
    public string? ErrorMessage { get; init; }
}
