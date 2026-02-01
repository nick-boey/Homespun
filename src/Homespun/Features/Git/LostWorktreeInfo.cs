namespace Homespun.Features.Git;

/// <summary>
/// Information about a lost/orphaned worktree folder that is not tracked by git worktree.
/// </summary>
public class LostWorktreeInfo
{
    /// <summary>
    /// Full path to the lost worktree folder.
    /// </summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// Branch name that matches this folder, if one can be determined.
    /// </summary>
    public string? MatchingBranchName { get; set; }

    /// <summary>
    /// Git status of the worktree (file change counts), if available.
    /// </summary>
    public WorktreeStatus? Status { get; set; }

    /// <summary>
    /// Gets the folder name from the path.
    /// </summary>
    public string FolderName => System.IO.Path.GetFileName(Path) ?? "";

    /// <summary>
    /// Whether this lost worktree can be repaired (has a matching branch).
    /// </summary>
    public bool CanRepair => !string.IsNullOrEmpty(MatchingBranchName);
}
