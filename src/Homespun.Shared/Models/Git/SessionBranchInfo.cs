namespace Homespun.Shared.Models.Git;

/// <summary>
/// Represents branch and commit information for a session's working directory.
/// </summary>
public class SessionBranchInfo
{
    /// <summary>
    /// The current branch name, or null if in detached HEAD state.
    /// </summary>
    public string? BranchName { get; set; }

    /// <summary>
    /// The short commit SHA (abbreviated hash).
    /// </summary>
    public string? CommitSha { get; set; }

    /// <summary>
    /// The commit message (subject line).
    /// </summary>
    public string? CommitMessage { get; set; }

    /// <summary>
    /// The commit date.
    /// </summary>
    public DateTime? CommitDate { get; set; }

    /// <summary>
    /// Number of commits ahead of upstream.
    /// </summary>
    public int AheadCount { get; set; }

    /// <summary>
    /// Number of commits behind upstream.
    /// </summary>
    public int BehindCount { get; set; }

    /// <summary>
    /// Whether there are uncommitted changes in the working directory.
    /// </summary>
    public bool HasUncommittedChanges { get; set; }
}
