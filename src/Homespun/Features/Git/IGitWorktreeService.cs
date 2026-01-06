namespace Homespun.Features.Git;

/// <summary>
/// Interface for Git worktree operations.
/// </summary>
public interface IGitWorktreeService
{
    Task<string?> CreateWorktreeAsync(string repoPath, string branchName, bool createBranch = false, string? baseBranch = null);
    Task<bool> RemoveWorktreeAsync(string repoPath, string worktreePath);
    Task<List<WorktreeInfo>> ListWorktreesAsync(string repoPath);
    Task PruneWorktreesAsync(string repoPath);
    Task<bool> WorktreeExistsAsync(string repoPath, string branchName);
    
    /// <summary>
    /// Pull the latest changes from the remote for a worktree.
    /// </summary>
    /// <param name="worktreePath">Path to the worktree directory</param>
    /// <returns>True if successful, false otherwise</returns>
    Task<bool> PullLatestAsync(string worktreePath);

    /// <summary>
    /// Fetch and update a specific branch from remote to ensure it's up to date.
    /// This fetches the branch and updates the local ref without checking it out.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="branchName">Name of the branch to update (e.g., "main")</param>
    /// <returns>True if successful, false otherwise</returns>
    Task<bool> FetchAndUpdateBranchAsync(string repoPath, string branchName);
}
