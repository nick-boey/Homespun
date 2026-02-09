namespace Homespun.Shared.Models.Git;

/// <summary>
/// Represents information about a Git branch.
/// </summary>
public class BranchInfo
{
    /// <summary>
    /// The full name of the branch (e.g., "refs/heads/main" or just "main").
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// The short name of the branch without refs/heads/ prefix.
    /// </summary>
    public string ShortName { get; set; } = "";

    /// <summary>
    /// Whether this is the currently checked out branch.
    /// </summary>
    public bool IsCurrent { get; set; }

    /// <summary>
    /// The commit SHA that this branch points to.
    /// </summary>
    public string? CommitSha { get; set; }

    /// <summary>
    /// The upstream tracking branch, if any.
    /// </summary>
    public string? Upstream { get; set; }

    /// <summary>
    /// Whether the branch has a remote tracking branch.
    /// </summary>
    public bool HasRemote => !string.IsNullOrEmpty(Upstream);

    /// <summary>
    /// Number of commits ahead of upstream.
    /// </summary>
    public int AheadCount { get; set; }

    /// <summary>
    /// Number of commits behind upstream.
    /// </summary>
    public int BehindCount { get; set; }

    /// <summary>
    /// Whether the branch has a corresponding clone.
    /// </summary>
    public bool HasClone { get; set; }

    /// <summary>
    /// Path to the clone if one exists.
    /// </summary>
    public string? ClonePath { get; set; }

    /// <summary>
    /// Whether the branch has been merged into the default branch.
    /// </summary>
    public bool IsMerged { get; set; }

    /// <summary>
    /// The last commit message on this branch.
    /// </summary>
    public string? LastCommitMessage { get; set; }

    /// <summary>
    /// The date of the last commit on this branch.
    /// </summary>
    public DateTime? LastCommitDate { get; set; }
}
