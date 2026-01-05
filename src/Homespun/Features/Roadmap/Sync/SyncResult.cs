namespace Homespun.Features.Roadmap.Sync;

/// <summary>
/// Result of syncing ROADMAP.local.json to all worktrees.
/// </summary>
public class SyncResult
{
    /// <summary>
    /// Whether the overall sync operation succeeded.
    /// True if all worktrees were synced without conflicts.
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Status of each worktree that was processed.
    /// </summary>
    public List<WorktreeSyncStatus> WorktreeStatuses { get; init; } = [];

    /// <summary>
    /// Worktrees that have conflicts requiring resolution.
    /// </summary>
    public List<WorktreeSyncStatus> Conflicts =>
        WorktreeStatuses.Where(s => s.Status == SyncStatus.ConflictNeedsResolution).ToList();

    /// <summary>
    /// Whether there are any conflicts that need resolution.
    /// </summary>
    public bool HasConflicts => Conflicts.Count > 0;

    /// <summary>
    /// Number of worktrees that were successfully synced.
    /// </summary>
    public int SyncedCount =>
        WorktreeStatuses.Count(s => s.Status is SyncStatus.Committed or SyncStatus.AlreadySynced);

    /// <summary>
    /// Creates a successful result with no conflicts.
    /// </summary>
    public static SyncResult Successful(List<WorktreeSyncStatus> statuses)
    {
        return new SyncResult
        {
            Success = !statuses.Any(s => s.Status == SyncStatus.ConflictNeedsResolution),
            WorktreeStatuses = statuses
        };
    }
}
