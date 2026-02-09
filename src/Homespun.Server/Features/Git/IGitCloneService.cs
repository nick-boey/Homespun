namespace Homespun.Features.Git;

/// <summary>
/// Interface for Git clone operations.
/// </summary>
public interface IGitCloneService
{
    Task<string?> CreateCloneAsync(string repoPath, string branchName, bool createBranch = false, string? baseBranch = null);
    Task<bool> RemoveCloneAsync(string repoPath, string clonePath);
    Task<List<CloneInfo>> ListClonesAsync(string repoPath);
    Task PruneClonesAsync(string repoPath);
    Task<bool> CloneExistsAsync(string repoPath, string branchName);

    /// <summary>
    /// Gets the clone path for a given branch name, accounting for branch name sanitization.
    /// First checks for a direct branch name match, then falls back to matching the sanitized
    /// branch name against clone paths.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="branchName">The original branch name (may contain special characters like +)</param>
    /// <returns>The clone path if found, null otherwise</returns>
    Task<string?> GetClonePathForBranchAsync(string repoPath, string branchName);


    /// <summary>
    /// Pull the latest changes from the remote for a clone.
    /// </summary>
    /// <param name="clonePath">Path to the clone directory</param>
    /// <returns>True if successful, false otherwise</returns>
    Task<bool> PullLatestAsync(string clonePath);

    /// <summary>
    /// Fetch and update a specific branch from remote to ensure it's up to date.
    /// This fetches the branch and updates the local ref without checking it out.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="branchName">Name of the branch to update (e.g., "main")</param>
    /// <returns>True if successful, false otherwise</returns>
    Task<bool> FetchAndUpdateBranchAsync(string repoPath, string branchName);

    /// <summary>
    /// List all local branches in the repository.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <returns>List of branch information</returns>
    Task<List<BranchInfo>> ListLocalBranchesAsync(string repoPath);

    /// <summary>
    /// List all remote branches that don't have a corresponding local branch.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <returns>List of remote-only branch names</returns>
    Task<List<string>> ListRemoteOnlyBranchesAsync(string repoPath);

    /// <summary>
    /// Check if a branch has been merged into the default branch.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="branchName">Name of the branch to check</param>
    /// <param name="targetBranch">Target branch to check against (usually the default branch)</param>
    /// <returns>True if the branch has been merged</returns>
    Task<bool> IsBranchMergedAsync(string repoPath, string branchName, string targetBranch);

    /// <summary>
    /// Delete a local branch.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="branchName">Name of the branch to delete</param>
    /// <param name="force">Force delete even if not merged</param>
    /// <returns>True if successful</returns>
    Task<bool> DeleteLocalBranchAsync(string repoPath, string branchName, bool force = false);

    /// <summary>
    /// Delete a remote branch.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="branchName">Name of the branch to delete</param>
    /// <returns>True if successful</returns>
    Task<bool> DeleteRemoteBranchAsync(string repoPath, string branchName);

    /// <summary>
    /// Check if a remote branch exists on the origin.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="branchName">Name of the branch to check</param>
    /// <returns>True if the remote branch exists</returns>
    Task<bool> RemoteBranchExistsAsync(string repoPath, string branchName);

    /// <summary>
    /// Create a local branch from a remote branch.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="remoteBranch">Name of the remote branch (e.g., "origin/feature-branch")</param>
    /// <returns>True if successful</returns>
    Task<bool> CreateLocalBranchFromRemoteAsync(string repoPath, string remoteBranch);

    /// <summary>
    /// Get the commit count difference between two branches.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="branchName">Name of the branch to compare</param>
    /// <param name="targetBranch">Target branch to compare against</param>
    /// <returns>Tuple of (ahead count, behind count)</returns>
    Task<(int ahead, int behind)> GetBranchDivergenceAsync(string repoPath, string branchName, string targetBranch);

    /// <summary>
    /// Fetch all remote branches.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <returns>True if successful</returns>
    Task<bool> FetchAllAsync(string repoPath);

    /// <summary>
    /// Gets the git status of a clone (modified, staged, and untracked file counts).
    /// </summary>
    /// <param name="clonePath">Path to the clone</param>
    /// <returns>CloneStatus with file counts</returns>
    Task<CloneStatus> GetCloneStatusAsync(string clonePath);

    /// <summary>
    /// Finds lost clone folders - directories that look like clones but are not
    /// tracked. These are sibling folders of the main repo that have
    /// a .git file or folder but are not in the clone list.
    /// </summary>
    /// <param name="repoPath">Path to the main repository</param>
    /// <returns>List of lost clone folder information</returns>
    Task<List<LostCloneInfo>> FindLostCloneFoldersAsync(string repoPath);

    /// <summary>
    /// Deletes a clone folder completely from disk.
    /// Use with caution - this permanently deletes the folder and all its contents.
    /// </summary>
    /// <param name="folderPath">Path to the folder to delete</param>
    /// <returns>True if deletion was successful</returns>
    Task<bool> DeleteCloneFolderAsync(string folderPath);

    /// <summary>
    /// Gets the current branch checked out in a clone.
    /// </summary>
    /// <param name="clonePath">Path to the clone</param>
    /// <returns>Branch name or null if error/detached HEAD</returns>
    Task<string?> GetCurrentBranchAsync(string clonePath);

    /// <summary>
    /// Checks out a specific branch in a clone.
    /// </summary>
    /// <param name="clonePath">Path to the clone</param>
    /// <param name="branchName">Name of the branch to checkout</param>
    /// <returns>True if successful</returns>
    Task<bool> CheckoutBranchAsync(string clonePath, string branchName);

    /// <summary>
    /// Checks if a branch has been squash-merged into the target branch.
    /// This detects when a PR was squash-merged rather than regular merged.
    /// </summary>
    /// <param name="repoPath">Path to the repository</param>
    /// <param name="branchName">Name of the branch to check</param>
    /// <param name="targetBranch">Target branch to check against (usually the default branch)</param>
    /// <returns>True if the branch appears to be squash-merged</returns>
    Task<bool> IsSquashMergedAsync(string repoPath, string branchName, string targetBranch);

    /// <summary>
    /// Creates a clone from a remote branch.
    /// </summary>
    /// <param name="repoPath">Path to the main repository</param>
    /// <param name="remoteBranch">Name of the remote branch (without origin/ prefix)</param>
    /// <returns>Path to the created clone, or null if failed</returns>
    Task<string?> CreateCloneFromRemoteBranchAsync(string repoPath, string remoteBranch);

    /// <summary>
    /// Repairs a lost clone by re-cloning if needed.
    /// </summary>
    /// <param name="repoPath">Path to the main repository</param>
    /// <param name="folderPath">Path to the lost clone folder</param>
    /// <param name="branchName">Name of the branch to attach</param>
    /// <returns>True if repair was successful</returns>
    Task<bool> RepairCloneAsync(string repoPath, string folderPath, string branchName);

    /// <summary>
    /// Get the list of files that have changed between the current branch and the target branch.
    /// Uses git diff --numstat to get file changes with addition/deletion counts.
    /// </summary>
    /// <param name="clonePath">Path to the clone directory</param>
    /// <param name="targetBranch">Target branch to compare against (e.g., "main")</param>
    /// <returns>List of changed files with their status and line counts</returns>
    Task<List<FileChangeInfo>> GetChangedFilesAsync(string clonePath, string targetBranch);
}
