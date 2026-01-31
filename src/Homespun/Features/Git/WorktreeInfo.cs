namespace Homespun.Features.Git;

public class WorktreeInfo
{
    public string Path { get; set; } = "";
    public string? Branch { get; set; }
    public string? HeadCommit { get; set; }
    public bool IsBare { get; set; }
    public bool IsDetached { get; set; }

    /// <summary>
    /// The expected branch based on the folder name, if different from the current branch.
    /// Null if the worktree is on the correct branch or no expected branch can be determined.
    /// </summary>
    public string? ExpectedBranch { get; set; }

    /// <summary>
    /// Whether the worktree is checked out to the expected branch.
    /// True if no expected branch mismatch is detected.
    /// </summary>
    public bool IsOnCorrectBranch => string.IsNullOrEmpty(ExpectedBranch);

    /// <summary>
    /// Gets the folder name from the path.
    /// </summary>
    public string FolderName => System.IO.Path.GetFileName(Path) ?? "";
}

/// <summary>
/// Represents the git status of a worktree.
/// </summary>
public class WorktreeStatus
{
    /// <summary>
    /// Number of modified (but unstaged) files.
    /// </summary>
    public int ModifiedCount { get; set; }

    /// <summary>
    /// Number of staged files.
    /// </summary>
    public int StagedCount { get; set; }

    /// <summary>
    /// Number of untracked files.
    /// </summary>
    public int UntrackedCount { get; set; }

    /// <summary>
    /// Whether the worktree has any uncommitted changes.
    /// </summary>
    public bool HasUncommittedChanges => ModifiedCount > 0 || StagedCount > 0 || UntrackedCount > 0;

    /// <summary>
    /// Whether the worktree has any changes (alias for HasUncommittedChanges).
    /// </summary>
    public bool HasChanges => HasUncommittedChanges;
}

/// <summary>
/// Information about a lost worktree folder - a directory that looks like it was
/// a worktree but is no longer tracked by git worktree.
/// </summary>
public class LostWorktreeInfo
{
    /// <summary>
    /// Full path to the lost worktree folder.
    /// </summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// Branch name that matches the folder name, if any.
    /// </summary>
    public string? MatchingBranchName { get; set; }

    /// <summary>
    /// Git status of the folder, if it could be read.
    /// </summary>
    public WorktreeStatus? Status { get; set; }

    /// <summary>
    /// Gets the folder name from the path.
    /// </summary>
    public string FolderName => System.IO.Path.GetFileName(Path) ?? "";

    /// <summary>
    /// Whether this lost worktree can be repaired (re-added to git worktree).
    /// True if there's a matching branch and no uncommitted changes.
    /// </summary>
    public bool CanRepair => !string.IsNullOrEmpty(MatchingBranchName) && (Status == null || !Status.HasChanges);
}