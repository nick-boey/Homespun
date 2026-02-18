namespace Homespun.Shared.Models.Git;

public class CloneInfo
{
    public string Path { get; set; } = "";

    /// <summary>
    /// The actual working directory path containing the git repository.
    /// For new structure: {Path}/workdir
    /// For legacy structure: same as Path
    /// This is the path that should be mounted as /workdir in containers.
    /// </summary>
    public string? WorkdirPath { get; set; }
    public string? Branch { get; set; }
    public string? HeadCommit { get; set; }
    public bool IsBare { get; set; }
    public bool IsDetached { get; set; }

    /// <summary>
    /// The expected branch based on the folder name, if different from the current branch.
    /// Null if the clone is on the correct branch or no expected branch can be determined.
    /// </summary>
    public string? ExpectedBranch { get; set; }

    /// <summary>
    /// Whether the clone is checked out to the expected branch.
    /// True if no expected branch mismatch is detected.
    /// </summary>
    public bool IsOnCorrectBranch => string.IsNullOrEmpty(ExpectedBranch);

    /// <summary>
    /// Gets the folder name from the path.
    /// </summary>
    public string FolderName => System.IO.Path.GetFileName(Path) ?? "";
}
