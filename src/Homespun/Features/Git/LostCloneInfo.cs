namespace Homespun.Features.Git;

/// <summary>
/// Information about a lost/orphaned clone folder that is not tracked.
/// </summary>
public class LostCloneInfo
{
    /// <summary>
    /// Full path to the lost clone folder.
    /// </summary>
    public string Path { get; set; } = "";

    /// <summary>
    /// Branch name that matches this folder, if one can be determined.
    /// </summary>
    public string? MatchingBranchName { get; set; }

    /// <summary>
    /// Git status of the clone (file change counts), if available.
    /// </summary>
    public CloneStatus? Status { get; set; }

    /// <summary>
    /// Gets the folder name from the path.
    /// </summary>
    public string FolderName => System.IO.Path.GetFileName(Path) ?? "";

    /// <summary>
    /// Whether this lost clone can be repaired (has a matching branch).
    /// </summary>
    public bool CanRepair => !string.IsNullOrEmpty(MatchingBranchName);
}
