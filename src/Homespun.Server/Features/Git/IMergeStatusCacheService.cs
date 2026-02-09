namespace Homespun.Features.Git;

/// <summary>
/// Service for caching merge status to avoid expensive git operations.
/// </summary>
public interface IMergeStatusCacheService
{
    /// <summary>
    /// Gets the cached merge status for a branch.
    /// If not cached or cache is stale, computes and caches the result.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="branchName">Name of the branch to check</param>
    /// <param name="targetBranch">Target branch to check against (usually default branch)</param>
    /// <returns>Merge status with IsMerged and IsSquashMerged flags</returns>
    Task<MergeStatus> GetMergeStatusAsync(string repoPath, string branchName, string targetBranch);

    /// <summary>
    /// Invalidates the cache for a specific branch.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="branchName">Name of the branch to invalidate</param>
    void InvalidateBranch(string repoPath, string branchName);

    /// <summary>
    /// Invalidates the entire cache for a repository.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    void InvalidateRepository(string repoPath);
}
