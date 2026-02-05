namespace Homespun.Features.Git;

/// <summary>
/// Represents a cached merge status entry for a branch.
/// </summary>
public class MergeStatus
{
    /// <summary>
    /// Whether the branch has been merged (via regular merge).
    /// </summary>
    public bool IsMerged { get; set; }

    /// <summary>
    /// Whether the branch has been squash-merged.
    /// </summary>
    public bool IsSquashMerged { get; set; }

    /// <summary>
    /// When the merge status was checked.
    /// </summary>
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Represents the full cache for a repository.
/// </summary>
public class MergeStatusCache
{
    /// <summary>
    /// Path to the repository this cache is for.
    /// </summary>
    public string RepoPath { get; set; } = "";

    /// <summary>
    /// When the cache was last updated.
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Target branch that merge status was checked against (usually "main" or "master").
    /// </summary>
    public string TargetBranch { get; set; } = "";

    /// <summary>
    /// Cached merge status for each branch.
    /// Key is the branch name.
    /// </summary>
    public Dictionary<string, MergeStatus> Branches { get; set; } = new();
}
