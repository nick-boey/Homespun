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
/// Represents the git status of a worktree (modified, staged, and untracked file counts).
/// </summary>
public class WorktreeStatus
{
    /// <summary>
    /// Number of files staged for commit.
    /// </summary>
    public int StagedCount { get; set; }

    /// <summary>
    /// Number of modified files (not staged).
    /// </summary>
    public int ModifiedCount { get; set; }

    /// <summary>
    /// Number of untracked files.
    /// </summary>
    public int UntrackedCount { get; set; }

    /// <summary>
    /// Whether there are any changes (staged, modified, or untracked).
    /// </summary>
    public bool HasChanges => StagedCount > 0 || ModifiedCount > 0 || UntrackedCount > 0;
}

/// <summary>
/// Represents a lost worktree folder - a directory that looks like a worktree but is not
/// tracked by git worktree.
/// </summary>
public class LostWorktreeInfo
{
    /// <summary>
    /// Path to the lost worktree folder.
    /// </summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// The matching branch name if the folder name corresponds to a known branch.
    /// </summary>
    public string? MatchingBranchName { get; set; }

    /// <summary>
    /// The git status of the lost worktree, if it could be retrieved.
    /// </summary>
    public WorktreeStatus? Status { get; set; }

    /// <summary>
    /// Gets the folder name from the path.
    /// </summary>
    public string FolderName => System.IO.Path.GetFileName(Path) ?? "";

    /// <summary>
    /// Whether this lost worktree can be repaired (has a matching branch name).
    /// </summary>
    public bool CanRepair => !string.IsNullOrEmpty(MatchingBranchName);
}