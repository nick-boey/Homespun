using System.Text.RegularExpressions;
using Homespun.Features.Commands;
using Homespun.Features.GitHub;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Homespun.Features.Git;

public class GitWorktreeService(ICommandRunner commandRunner, ILogger<GitWorktreeService> logger) : IGitWorktreeService
{
    public GitWorktreeService() : this(
        new CommandRunner(
            new NullGitHubEnvironmentService(),
            NullLogger<CommandRunner>.Instance),
        NullLogger<GitWorktreeService>.Instance)
    {
    }

    /// <summary>
    /// A no-op implementation of IGitHubEnvironmentService for testing scenarios
    /// where GitHub authentication is not needed.
    /// </summary>
    private class NullGitHubEnvironmentService : IGitHubEnvironmentService
    {
        public bool IsConfigured => false;
        public IDictionary<string, string> GetGitHubEnvironment() => new Dictionary<string, string>();
        public string? GetMaskedToken() => null;
        public Task<GitHubAuthStatus> CheckGhAuthStatusAsync(CancellationToken ct = default) =>
            Task.FromResult(new GitHubAuthStatus { IsAuthenticated = false, AuthMethod = GitHubAuthMethod.None });
    }

    public async Task<string?> CreateWorktreeAsync(string repoPath, string branchName, bool createBranch = false, string? baseBranch = null)
    {
        var sanitizedName = SanitizeBranchName(branchName);
        // Create worktree as sibling of the main repo, not inside it
        // e.g., ~/.homespun/src/repo/main -> ~/.homespun/src/repo/<branch-name>
        var parentDir = Path.GetDirectoryName(repoPath);
        if (string.IsNullOrEmpty(parentDir))
        {
            logger.LogError("Cannot determine parent directory of {RepoPath}", repoPath);
            throw new InvalidOperationException($"Cannot determine parent directory of {repoPath}");
        }
        
        // Normalize the path to use platform-native separators
        // This fixes issues on Windows where mixed forward/back slashes cause problems
        var worktreePath = Path.GetFullPath(Path.Combine(parentDir, sanitizedName));

        // Ensure parent directories exist for nested branch names (e.g., app/feature/id)
        var worktreeParentDir = Path.GetDirectoryName(worktreePath);
        if (!string.IsNullOrEmpty(worktreeParentDir) && !Directory.Exists(worktreeParentDir))
        {
            Directory.CreateDirectory(worktreeParentDir);
        }

        if (createBranch)
        {
            var baseRef = baseBranch ?? "HEAD";
            
            var branchResult = await commandRunner.RunAsync("git", $"branch \"{branchName}\" \"{baseRef}\"", repoPath);
            if (!branchResult.Success && !branchResult.Error.Contains("already exists"))
            {
                return null;
            }
        }

        var args = $"worktree add \"{worktreePath}\" \"{branchName}\"";
        var result = await commandRunner.RunAsync("git", args, repoPath);

        if (!result.Success)
        {
            logger.LogWarning("Failed to create worktree at {WorktreePath} for branch {BranchName}: {Error}",
                worktreePath, branchName, result.Error);
            return null;
        }
        
        logger.LogInformation("Created worktree at {WorktreePath} for branch {BranchName}", worktreePath, branchName);
        
        return worktreePath;
    }

    public async Task<bool> RemoveWorktreeAsync(string repoPath, string worktreePath)
    {
        var result = await commandRunner.RunAsync("git", $"worktree remove \"{worktreePath}\" --force", repoPath);
        
        if (result.Success)
        {
            logger.LogInformation("Removed worktree {WorktreePath}", worktreePath);
        }
        
        return result.Success;
    }

    public async Task<List<WorktreeInfo>> ListWorktreesAsync(string repoPath)
    {
        var worktrees = await ListWorktreesRawAsync(repoPath);

        // Populate ExpectedBranch for branch mismatch detection
        var branches = await ListLocalBranchesAsync(repoPath);
        foreach (var worktree in worktrees.Where(w => !w.IsBare))
        {
            var expectedBranch = DetermineExpectedBranch(worktree, branches);
            if (expectedBranch != null)
            {
                var currentBranch = worktree.Branch?.Replace("refs/heads/", "") ?? "";
                if (!string.Equals(currentBranch, expectedBranch, StringComparison.OrdinalIgnoreCase))
                {
                    worktree.ExpectedBranch = expectedBranch;
                }
            }
        }

        return worktrees;
    }

    /// <summary>
    /// Lists worktrees by parsing `git worktree list --porcelain` output.
    /// This is the raw version that does NOT enrich with branch mismatch detection,
    /// avoiding mutual recursion with ListLocalBranchesAsync.
    /// </summary>
    private async Task<List<WorktreeInfo>> ListWorktreesRawAsync(string repoPath)
    {
        var result = await commandRunner.RunAsync("git", "worktree list --porcelain", repoPath);

        if (result == null || !result.Success)
        {
            return [];
        }

        var worktrees = new List<WorktreeInfo>();
        var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        WorktreeInfo? current = null;
        foreach (var line in lines)
        {
            if (line.StartsWith("worktree "))
            {
                if (current != null)
                    worktrees.Add(current);
                current = new WorktreeInfo { Path = line[9..].Trim() };
            }
            else if (line.StartsWith("HEAD ") && current != null)
            {
                current.HeadCommit = line[5..].Trim();
            }
            else if (line.StartsWith("branch ") && current != null)
            {
                current.Branch = line[7..].Trim();
            }
            else if (line == "bare" && current != null)
            {
                current.IsBare = true;
            }
            else if (line == "detached" && current != null)
            {
                current.IsDetached = true;
            }
        }

        if (current != null)
            worktrees.Add(current);

        return worktrees;
    }

    /// <summary>
    /// Determines the expected branch for a worktree based on its folder name.
    /// Checks both sanitized and unsanitized branch names.
    /// </summary>
    private string? DetermineExpectedBranch(WorktreeInfo worktree, List<BranchInfo> branches)
    {
        var folderName = worktree.FolderName;
        if (string.IsNullOrEmpty(folderName)) return null;

        // First check for exact match (unsanitized)
        var exactMatch = branches.FirstOrDefault(b =>
            string.Equals(b.ShortName, folderName, StringComparison.OrdinalIgnoreCase));
        if (exactMatch != null) return exactMatch.ShortName;

        // Then check for sanitized match
        foreach (var branch in branches)
        {
            var sanitizedBranchName = SanitizeBranchName(branch.ShortName);
            if (string.Equals(sanitizedBranchName, folderName, StringComparison.OrdinalIgnoreCase))
            {
                return branch.ShortName;
            }
        }

        return null;
    }

    public async Task PruneWorktreesAsync(string repoPath)
    {
        await commandRunner.RunAsync("git", "worktree prune", repoPath);
    }

    public async Task<bool> WorktreeExistsAsync(string repoPath, string branchName)
    {
        var worktrees = await ListWorktreesAsync(repoPath);
        return worktrees.Any(w => w.Branch?.EndsWith(branchName) == true);
    }

    public async Task<string?> GetWorktreePathForBranchAsync(string repoPath, string branchName)
    {
        var worktrees = await ListWorktreesAsync(repoPath);

        // First, try direct branch match (handles most cases where branch name hasn't been sanitized)
        var directMatch = worktrees.FirstOrDefault(w =>
            w.Branch?.EndsWith(branchName) == true ||
            w.Branch == $"refs/heads/{branchName}");

        if (directMatch != null)
        {
            logger.LogDebug("Found worktree for branch {BranchName} via direct match at {Path}", branchName, directMatch.Path);
            return directMatch.Path;
        }

        // Fall back to path-based matching using sanitized name
        // This handles cases where the branch name contains special characters like +
        // that were sanitized when the worktree folder was created
        var sanitizedName = SanitizeBranchName(branchName);
        var parentDir = Path.GetDirectoryName(repoPath);

        if (string.IsNullOrEmpty(parentDir))
        {
            return null;
        }

        var expectedPath = Path.GetFullPath(Path.Combine(parentDir, sanitizedName));
        var pathMatch = worktrees.FirstOrDefault(w =>
            Path.GetFullPath(w.Path).Equals(expectedPath, StringComparison.OrdinalIgnoreCase));

        if (pathMatch != null)
        {
            logger.LogDebug("Found worktree for branch {BranchName} via sanitized path match at {Path}", branchName, pathMatch.Path);
        }

        return pathMatch?.Path;
    }

    public async Task<bool> PullLatestAsync(string worktreePath)
    {
        // First fetch from remote (may fail for local-only branches, which is OK)
        await commandRunner.RunAsync("git", "fetch origin", worktreePath);

        // Try to pull with rebase to get latest changes
        var pullResult = await commandRunner.RunAsync("git", "pull --rebase --autostash", worktreePath);
        
        // Pull might fail if no upstream is set, which is fine for new branches
        return pullResult.Success || 
               pullResult.Error.Contains("no tracking information") ||
               pullResult.Error.Contains("There is no tracking information");
    }

    public async Task<bool> FetchAndUpdateBranchAsync(string repoPath, string branchName)
    {
        // Fetch the specific branch from origin
        var fetchResult = await commandRunner.RunAsync("git", $"fetch origin {branchName}:{branchName}", repoPath);
        
        if (!fetchResult.Success)
        {
            // Try a simple fetch if the branch update fails (might be checked out)
            var simpleFetchResult = await commandRunner.RunAsync("git", "fetch origin", repoPath);
            
            if (!simpleFetchResult.Success)
            {
                return false;
            }
        }

        return true;
    }

    public static string SanitizeBranchName(string branchName)
    {
        // Normalize path separators to forward slashes
        var sanitized = branchName.Replace('\\', '/');
        // Replace special characters (except forward slash) with dashes
        sanitized = Regex.Replace(sanitized, @"[@#\s]+", "-");
        // Remove any remaining invalid characters (keep forward slashes for folder structure)
        sanitized = Regex.Replace(sanitized, @"[^a-zA-Z0-9\-_./]", "-");
        // Remove consecutive dashes
        sanitized = Regex.Replace(sanitized, @"-+", "-");
        // Remove consecutive slashes
        sanitized = Regex.Replace(sanitized, @"/+", "/");
        // Trim dashes and slashes from ends
        return sanitized.Trim('-', '/');
    }

    public async Task<List<BranchInfo>> ListLocalBranchesAsync(string repoPath)
    {
        // Get list of worktrees first to map branches to their worktree paths
        // Use the raw version to avoid mutual recursion with ListWorktreesAsync
        var worktrees = await ListWorktreesRawAsync(repoPath);
        var worktreeByBranch = worktrees
            .Where(w => !string.IsNullOrEmpty(w.Branch))
            .ToDictionary(
                w => w.Branch!.Replace("refs/heads/", ""),
                w => w.Path);

        // Get branch info with format: branch name, commit sha, upstream, and tracking info
        var result = await commandRunner.RunAsync(
            "git",
            "for-each-ref --format='%(refname:short)|%(objectname:short)|%(upstream:short)|%(upstream:track)|%(committerdate:iso8601)|%(subject)' refs/heads/",
            repoPath);

        if (result == null || !result.Success)
        {
            logger.LogWarning("Failed to list local branches: {Error}", result?.Error);
            return [];
        }

        var branches = new List<BranchInfo>();
        var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Get the current branch
        var currentBranchResult = await commandRunner.RunAsync("git", "rev-parse --abbrev-ref HEAD", repoPath);
        var currentBranch = currentBranchResult is { Success: true } ? currentBranchResult.Output.Trim() : "";

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim().Trim('\'');
            var parts = trimmedLine.Split('|');
            if (parts.Length < 6) continue;

            var branchName = parts[0];
            var commitSha = parts[1];
            var upstream = string.IsNullOrEmpty(parts[2]) ? null : parts[2];
            var trackingInfo = parts[3];
            var dateStr = parts[4];
            var subject = parts[5];

            // Parse tracking info like "[ahead 2, behind 1]" or "[ahead 2]" or "[behind 1]"
            var (ahead, behind) = ParseTrackingInfo(trackingInfo);

            var branch = new BranchInfo
            {
                Name = $"refs/heads/{branchName}",
                ShortName = branchName,
                IsCurrent = branchName == currentBranch,
                CommitSha = commitSha,
                Upstream = upstream,
                AheadCount = ahead,
                BehindCount = behind,
                LastCommitMessage = subject,
                LastCommitDate = DateTime.TryParse(dateStr, out var date) ? date : null
            };

            // Check if branch has a worktree
            if (worktreeByBranch.TryGetValue(branchName, out var worktreePath))
            {
                branch.HasWorktree = true;
                branch.WorktreePath = worktreePath;
            }

            branches.Add(branch);
        }

        return branches;
    }

    public async Task<List<string>> ListRemoteOnlyBranchesAsync(string repoPath)
    {
        // First fetch to ensure we have the latest remote refs
        await commandRunner.RunAsync("git", "fetch --prune", repoPath);

        // Get all remote branches
        var remoteResult = await commandRunner.RunAsync(
            "git",
            "for-each-ref --format='%(refname:short)' refs/remotes/origin/",
            repoPath);

        if (!remoteResult.Success)
        {
            logger.LogWarning("Failed to list remote branches: {Error}", remoteResult.Error);
            return [];
        }

        var remoteBranches = remoteResult.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(b => b.Trim().Trim('\''))
            .Where(b => !b.EndsWith("/HEAD")) // Skip the HEAD pointer
            .Select(b => b.Replace("origin/", ""))
            .ToHashSet();

        // Get all local branches
        var localResult = await commandRunner.RunAsync(
            "git",
            "for-each-ref --format='%(refname:short)' refs/heads/",
            repoPath);

        if (!localResult.Success)
        {
            return remoteBranches.ToList();
        }

        var localBranches = localResult.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(b => b.Trim().Trim('\''))
            .ToHashSet();

        // Return remote branches that don't have a local counterpart
        return remoteBranches.Except(localBranches).ToList();
    }

    public async Task<bool> IsBranchMergedAsync(string repoPath, string branchName, string targetBranch)
    {
        // Check if the branch is an ancestor of the target branch
        var result = await commandRunner.RunAsync(
            "git",
            $"merge-base --is-ancestor \"{branchName}\" \"{targetBranch}\"",
            repoPath);

        // Exit code 0 means it's an ancestor (merged), non-zero means not
        return result.Success;
    }

    public async Task<bool> DeleteLocalBranchAsync(string repoPath, string branchName, bool force = false)
    {
        var flag = force ? "-D" : "-d";
        var result = await commandRunner.RunAsync("git", $"branch {flag} \"{branchName}\"", repoPath);

        if (result.Success)
        {
            logger.LogInformation("Deleted local branch {BranchName}", branchName);
        }
        else
        {
            logger.LogWarning("Failed to delete local branch {BranchName}: {Error}", branchName, result.Error);
        }

        return result.Success;
    }

    public async Task<bool> DeleteRemoteBranchAsync(string repoPath, string branchName)
    {
        var result = await commandRunner.RunAsync("git", $"push origin --delete \"{branchName}\"", repoPath);

        if (result.Success)
        {
            logger.LogInformation("Deleted remote branch {BranchName}", branchName);
        }
        else
        {
            logger.LogWarning("Failed to delete remote branch {BranchName}: {Error}", branchName, result.Error);
        }

        return result.Success;
    }

    public async Task<bool> CreateLocalBranchFromRemoteAsync(string repoPath, string remoteBranch)
    {
        // The remoteBranch parameter is expected to be the branch name without "origin/" prefix
        // e.g., "feature/test" not "origin/feature/test"
        var localBranchName = remoteBranch;

        // Create local branch tracking the remote
        var result = await commandRunner.RunAsync(
            "git",
            $"checkout -b \"{localBranchName}\" \"origin/{localBranchName}\"",
            repoPath);

        if (!result.Success)
        {
            // Try an alternative approach - just create the branch without checking out
            result = await commandRunner.RunAsync(
                "git",
                $"branch \"{localBranchName}\" \"origin/{localBranchName}\"",
                repoPath);
        }

        if (result.Success)
        {
            logger.LogInformation("Created local branch {BranchName} from remote", localBranchName);
        }
        else
        {
            logger.LogWarning("Failed to create local branch from remote {RemoteBranch}: {Error}", remoteBranch, result.Error);
        }

        return result.Success;
    }

    public async Task<(int ahead, int behind)> GetBranchDivergenceAsync(string repoPath, string branchName, string targetBranch)
    {
        var result = await commandRunner.RunAsync(
            "git",
            $"rev-list --left-right --count \"{targetBranch}...{branchName}\"",
            repoPath);

        if (!result.Success)
        {
            return (0, 0);
        }

        var parts = result.Output.Trim().Split('\t', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return (0, 0);
        }

        // Output format is: behind<tab>ahead
        int.TryParse(parts[0], out var behind);
        int.TryParse(parts[1], out var ahead);

        return (ahead, behind);
    }

    public async Task<bool> FetchAllAsync(string repoPath)
    {
        var result = await commandRunner.RunAsync("git", "fetch --all --prune", repoPath);

        if (!result.Success)
        {
            logger.LogWarning("Failed to fetch all: {Error}", result.Error);
        }

        return result.Success;
    }

    private static (int ahead, int behind) ParseTrackingInfo(string trackingInfo)
    {
        var ahead = 0;
        var behind = 0;

        if (string.IsNullOrEmpty(trackingInfo))
            return (ahead, behind);

        // Parse formats like "[ahead 2]", "[behind 1]", "[ahead 2, behind 1]"
        var aheadMatch = Regex.Match(trackingInfo, @"ahead (\d+)");
        var behindMatch = Regex.Match(trackingInfo, @"behind (\d+)");

        if (aheadMatch.Success)
            int.TryParse(aheadMatch.Groups[1].Value, out ahead);
        if (behindMatch.Success)
            int.TryParse(behindMatch.Groups[1].Value, out behind);

        return (ahead, behind);
    }

    public async Task<WorktreeStatus> GetWorktreeStatusAsync(string worktreePath)
    {
        var result = await commandRunner.RunAsync("git", "status --porcelain", worktreePath);

        var status = new WorktreeStatus();

        if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
        {
            return status;
        }

        var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            if (line.Length < 2) continue;

            var indexStatus = line[0];
            var worktreeStatus = line[1];

            // Staged files: any non-space/? in first position
            if (indexStatus != ' ' && indexStatus != '?')
            {
                status.StagedCount++;
            }

            // Modified files: any non-space in second position (except ?)
            if (worktreeStatus != ' ' && worktreeStatus != '?')
            {
                status.ModifiedCount++;
            }

            // Untracked files: ??
            if (indexStatus == '?' && worktreeStatus == '?')
            {
                status.UntrackedCount++;
            }
        }

        return status;
    }

    public async Task<List<LostWorktreeInfo>> FindLostWorktreeFoldersAsync(string repoPath)
    {
        var lostWorktrees = new List<LostWorktreeInfo>();

        // Get the parent directory where worktrees are siblings
        var parentDir = Path.GetDirectoryName(repoPath);
        if (string.IsNullOrEmpty(parentDir) || !Directory.Exists(parentDir))
        {
            return lostWorktrees;
        }

        // Get all tracked worktree paths (use raw version to avoid unnecessary branch enrichment)
        var worktrees = await ListWorktreesRawAsync(repoPath);
        var trackedPaths = worktrees.Select(w => Path.GetFullPath(w.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Get all local branches for matching
        var branches = await ListLocalBranchesAsync(repoPath);
        var branchNames = branches.Select(b => b.ShortName).ToHashSet();

        // Scan sibling directories
        foreach (var dir in Directory.GetDirectories(parentDir))
        {
            var fullPath = Path.GetFullPath(dir);

            // Skip if it's a tracked worktree
            if (trackedPaths.Contains(fullPath))
            {
                continue;
            }

            // Check if it looks like a git worktree (has .git file or folder)
            var gitPath = Path.Combine(dir, ".git");
            if (!File.Exists(gitPath) && !Directory.Exists(gitPath))
            {
                continue;
            }

            var folderName = Path.GetFileName(dir);
            if (string.IsNullOrEmpty(folderName))
            {
                continue;
            }

            var lostWorktree = new LostWorktreeInfo
            {
                Path = fullPath
            };

            // Try to match the folder name to a branch
            // The folder name might be a sanitized version of the branch name
            var matchingBranch = branchNames.FirstOrDefault(b =>
                SanitizeBranchName(b).Equals(folderName, StringComparison.OrdinalIgnoreCase) ||
                b.Equals(folderName, StringComparison.OrdinalIgnoreCase));

            if (matchingBranch != null)
            {
                lostWorktree.MatchingBranchName = matchingBranch;
            }

            // Try to get the status of the lost worktree
            try
            {
                lostWorktree.Status = await GetWorktreeStatusAsync(fullPath);
            }
            catch
            {
                // Ignore errors getting status
            }

            lostWorktrees.Add(lostWorktree);
        }

        return lostWorktrees;
    }

    public Task<bool> DeleteWorktreeFolderAsync(string folderPath)
    {
        try
        {
            if (!Directory.Exists(folderPath))
            {
                return Task.FromResult(false);
            }

            // Delete the directory recursively
            Directory.Delete(folderPath, recursive: true);
            logger.LogInformation("Deleted worktree folder {FolderPath}", folderPath);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to delete worktree folder {FolderPath}", folderPath);
            return Task.FromResult(false);
        }
    }

    public async Task<string?> GetCurrentBranchAsync(string worktreePath)
    {
        var result = await commandRunner.RunAsync("git", "rev-parse --abbrev-ref HEAD", worktreePath);

        if (!result.Success)
        {
            return null;
        }

        return result.Output.Trim();
    }

    public async Task<bool> CheckoutBranchAsync(string worktreePath, string branchName)
    {
        var result = await commandRunner.RunAsync("git", $"checkout \"{branchName}\"", worktreePath);

        if (result.Success)
        {
            logger.LogInformation("Checked out branch {BranchName} in worktree {WorktreePath}", branchName, worktreePath);
        }
        else
        {
            logger.LogWarning("Failed to checkout branch {BranchName} in worktree {WorktreePath}: {Error}",
                branchName, worktreePath, result.Error);
        }

        return result.Success;
    }

    public async Task<bool> IsSquashMergedAsync(string repoPath, string branchName, string targetBranch)
    {
        // First check if there are commits in branchName that aren't in targetBranch
        var logResult = await commandRunner.RunAsync(
            "git",
            $"log \"{targetBranch}..{branchName}\" --format=%H",
            repoPath);

        if (!logResult.Success || string.IsNullOrWhiteSpace(logResult.Output))
        {
            // No commits to check, or branch is already merged the normal way
            return false;
        }

        // Use git cherry to check if commits are already applied (squash-merged)
        // Empty output means all commits are equivalent to something in target
        var cherryResult = await commandRunner.RunAsync(
            "git",
            $"cherry \"{targetBranch}\" \"{branchName}\"",
            repoPath);

        if (!cherryResult.Success)
        {
            return false;
        }

        // If cherry output is empty, all commits are already in target (squash-merged)
        // If output has "+" lines, those commits are not in target
        var output = cherryResult.Output.Trim();
        return string.IsNullOrEmpty(output) || !output.Contains('+');
    }

    public async Task<string?> CreateWorktreeFromRemoteBranchAsync(string repoPath, string remoteBranch)
    {
        var sanitizedName = SanitizeBranchName(remoteBranch);
        var parentDir = Path.GetDirectoryName(repoPath);

        if (string.IsNullOrEmpty(parentDir))
        {
            logger.LogError("Cannot determine parent directory of {RepoPath}", repoPath);
            return null;
        }

        var worktreePath = Path.GetFullPath(Path.Combine(parentDir, sanitizedName));

        // Ensure parent directories exist for nested branch names
        var worktreeParentDir = Path.GetDirectoryName(worktreePath);
        if (!string.IsNullOrEmpty(worktreeParentDir) && !Directory.Exists(worktreeParentDir))
        {
            Directory.CreateDirectory(worktreeParentDir);
        }

        // Create local branch from remote WITHOUT checking it out (use git branch, not checkout)
        // This avoids changing the main worktree's checked out branch
        var branchResult = await commandRunner.RunAsync(
            "git",
            $"branch \"{remoteBranch}\" \"origin/{remoteBranch}\"",
            repoPath);

        // It's okay if branch already exists
        if (!branchResult.Success && !branchResult.Error.Contains("already exists"))
        {
            logger.LogWarning("Failed to create local branch from remote {RemoteBranch}: {Error}",
                remoteBranch, branchResult.Error);
            // Continue anyway - the branch might exist
        }

        // Create the worktree
        var worktreeResult = await commandRunner.RunAsync(
            "git",
            $"worktree add \"{worktreePath}\" \"{remoteBranch}\"",
            repoPath);

        if (!worktreeResult.Success)
        {
            logger.LogWarning("Failed to create worktree for remote branch {RemoteBranch}: {Error}",
                remoteBranch, worktreeResult.Error);
            return null;
        }

        logger.LogInformation("Created worktree at {WorktreePath} for remote branch {RemoteBranch}",
            worktreePath, remoteBranch);
        return worktreePath;
    }

    /// <summary>
    /// Repairs a lost worktree by using git worktree repair.
    /// The repair command fixes worktree administrative files that may have become
    /// corrupt or orphaned (e.g., if the worktree was moved without using git).
    /// </summary>
    public async Task<bool> RepairWorktreeAsync(string repoPath, string folderPath, string branchName)
    {
        logger.LogInformation(
            "Attempting to repair worktree at {FolderPath} for branch {BranchName}",
            folderPath, branchName);

        // First, try git worktree repair which fixes both main repo and worktree admin files
        var repairResult = await commandRunner.RunAsync(
            "git",
            $"worktree repair \"{folderPath}\"",
            repoPath);

        if (repairResult.Success)
        {
            logger.LogInformation("Successfully repaired worktree at {FolderPath}", folderPath);
            return true;
        }

        logger.LogWarning(
            "git worktree repair failed for {FolderPath}: {Error}. Trying worktree add...",
            folderPath, repairResult.Error);

        // If repair fails, try to re-add the worktree
        // First remove any stale reference
        await commandRunner.RunAsync("git", "worktree prune", repoPath);

        // Try to add the existing folder as a worktree
        // Note: git worktree add will fail if the folder already exists with content
        // In this case, we need to use a different approach

        // Check if the branch exists locally
        var branchCheck = await commandRunner.RunAsync(
            "git",
            $"rev-parse --verify \"{branchName}\"",
            repoPath);

        if (!branchCheck.Success)
        {
            logger.LogWarning("Branch {BranchName} does not exist locally", branchName);
            return false;
        }

        // Since the folder exists, we need to check it out there
        // First, ensure the folder has a .git file pointing to the main repo
        var gitFile = Path.Combine(folderPath, ".git");
        if (File.Exists(gitFile) || Directory.Exists(gitFile))
        {
            // The worktree might just need its admin files fixed in the main repo
            // Run repair from the worktree directory itself
            var selfRepair = await commandRunner.RunAsync(
                "git",
                "worktree repair",
                folderPath);

            if (selfRepair.Success)
            {
                logger.LogInformation("Successfully self-repaired worktree at {FolderPath}", folderPath);
                return true;
            }
        }

        logger.LogError("Failed to repair worktree at {FolderPath}", folderPath);
        return false;
    }
}