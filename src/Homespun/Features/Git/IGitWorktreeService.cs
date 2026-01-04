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
}
