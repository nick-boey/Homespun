namespace Homespun.Features.Git;

/// <summary>
/// Represents the git status of a worktree with file change counts.
/// </summary>
public class WorktreeStatus
{
    /// <summary>
    /// Count of staged files (added to index).
    /// </summary>
    public int StagedCount { get; set; }

    /// <summary>
    /// Count of modified files in the working tree.
    /// </summary>
    public int ModifiedCount { get; set; }

    /// <summary>
    /// Count of untracked files.
    /// </summary>
    public int UntrackedCount { get; set; }

    /// <summary>
    /// Whether the worktree has any changes (staged, modified, or untracked).
    /// </summary>
    public bool HasChanges => StagedCount > 0 || ModifiedCount > 0 || UntrackedCount > 0;
}
