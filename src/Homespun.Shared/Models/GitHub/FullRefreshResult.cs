namespace Homespun.Shared.Models.GitHub;

/// <summary>
/// Result of a full refresh operation that fetches all PRs from GitHub.
/// </summary>
public class FullRefreshResult
{
    /// <summary>
    /// Count of open PRs fetched.
    /// </summary>
    public int OpenPrs { get; set; }

    /// <summary>
    /// Count of closed/merged PRs fetched.
    /// </summary>
    public int ClosedPrs { get; set; }

    /// <summary>
    /// Count of issues linked to PRs.
    /// </summary>
    public int LinkedIssues { get; set; }

    /// <summary>
    /// When the refresh completed.
    /// </summary>
    public DateTime RefreshedAt { get; set; }

    /// <summary>
    /// Any errors that occurred during refresh.
    /// </summary>
    public List<string> Errors { get; set; } = [];
}
