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