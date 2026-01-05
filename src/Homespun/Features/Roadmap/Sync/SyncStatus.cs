namespace Homespun.Features.Roadmap.Sync;

/// <summary>
/// Status of a sync operation for a single worktree.
/// </summary>
public enum SyncStatus
{
    /// <summary>
    /// Worktree was already in sync, no changes needed.
    /// </summary>
    AlreadySynced,

    /// <summary>
    /// ROADMAP.json was updated and committed.
    /// </summary>
    Committed,

    /// <summary>
    /// Worktree has local changes that need manual resolution.
    /// </summary>
    ConflictNeedsResolution,

    /// <summary>
    /// Worktree was skipped (e.g., main branch).
    /// </summary>
    Skipped,

    /// <summary>
    /// An error occurred during sync.
    /// </summary>
    Error
}
