using Fleece.Core.Models;
using Fleece.Core.Serialization;
using Fleece.Core.Services;
using Homespun.Features.Commands;
using Homespun.Features.Fleece.Models;

namespace Homespun.Features.Fleece.Services;

/// <summary>
/// Service for synchronizing fleece issues with the remote repository.
/// </summary>
public class FleeceIssuesSyncService(
    ICommandRunner commandRunner,
    ILogger<FleeceIssuesSyncService> logger) : IFleeceIssuesSyncService
{
    private readonly IssueMerger _issueMerger = new();
    public async Task<BranchStatusResult> CheckBranchStatusAsync(string projectPath, string defaultBranch, CancellationToken ct = default)
    {
        logger.LogInformation("Checking branch status for {ProjectPath}, expected branch: {Branch}", projectPath, defaultBranch);

        // Get current branch name
        var branchResult = await commandRunner.RunAsync("git", "rev-parse --abbrev-ref HEAD", projectPath);
        if (!branchResult.Success)
        {
            logger.LogWarning("Failed to get current branch: {Error}", branchResult.Error);
            return new BranchStatusResult(
                Success: false,
                IsOnCorrectBranch: false,
                CurrentBranch: null,
                ErrorMessage: $"Failed to get current branch: {branchResult.Error}",
                IsBehindRemote: false,
                CommitsBehind: 0,
                CommitsAhead: 0);
        }

        var currentBranch = branchResult.Output.Trim();
        var isOnCorrectBranch = currentBranch.Equals(defaultBranch, StringComparison.OrdinalIgnoreCase);

        if (!isOnCorrectBranch)
        {
            logger.LogWarning("Not on expected branch. Current: {Current}, Expected: {Expected}", currentBranch, defaultBranch);
            return new BranchStatusResult(
                Success: true,
                IsOnCorrectBranch: false,
                CurrentBranch: currentBranch,
                ErrorMessage: $"You are on branch '{currentBranch}' but fleece issues can only be synced from the '{defaultBranch}' branch. Please switch to '{defaultBranch}' first.",
                IsBehindRemote: false,
                CommitsBehind: 0,
                CommitsAhead: 0);
        }

        // Fetch to get latest remote status
        var fetchResult = await commandRunner.RunAsync("git", "fetch origin", projectPath);
        if (!fetchResult.Success)
        {
            logger.LogWarning("Failed to fetch from origin: {Error}", fetchResult.Error);
            return new BranchStatusResult(
                Success: false,
                IsOnCorrectBranch: true,
                CurrentBranch: currentBranch,
                ErrorMessage: $"Failed to fetch from remote: {fetchResult.Error}",
                IsBehindRemote: false,
                CommitsBehind: 0,
                CommitsAhead: 0);
        }

        // Check how many commits behind/ahead we are
        var revListResult = await commandRunner.RunAsync("git", $"rev-list --left-right --count origin/{defaultBranch}...HEAD", projectPath);
        int commitsBehind = 0;
        int commitsAhead = 0;

        if (revListResult.Success)
        {
            // Output is "behind\tahead" e.g., "2\t1" means 2 behind, 1 ahead
            var parts = revListResult.Output.Trim().Split('\t', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                int.TryParse(parts[0], out commitsBehind);
                int.TryParse(parts[1], out commitsAhead);
            }
        }

        logger.LogInformation("Branch status: {Branch} is {Behind} commits behind and {Ahead} commits ahead of origin",
            currentBranch, commitsBehind, commitsAhead);

        return new BranchStatusResult(
            Success: true,
            IsOnCorrectBranch: true,
            CurrentBranch: currentBranch,
            ErrorMessage: null,
            IsBehindRemote: commitsBehind > 0,
            CommitsBehind: commitsBehind,
            CommitsAhead: commitsAhead);
    }

    public async Task<FleeceIssueSyncResult> SyncAsync(string projectPath, string defaultBranch, CancellationToken ct = default)
    {
        logger.LogInformation("Starting fleece issues sync for {ProjectPath} to branch {Branch}", projectPath, defaultBranch);

        // Step 1: Check if we're on the correct branch
        var branchStatus = await CheckBranchStatusAsync(projectPath, defaultBranch, ct);
        if (!branchStatus.Success)
        {
            return new FleeceIssueSyncResult(false, branchStatus.ErrorMessage, 0, false);
        }

        if (!branchStatus.IsOnCorrectBranch)
        {
            return new FleeceIssueSyncResult(false, branchStatus.ErrorMessage, 0, false);
        }

        // Step 2: Check for non-fleece changes that might cause merge conflicts
        var nonFleeceChanges = await GetNonFleeceChangesAsync(projectPath);
        if (nonFleeceChanges.Count > 0)
        {
            logger.LogWarning("Found {Count} non-fleece changed files that may cause conflicts: {Files}",
                nonFleeceChanges.Count, string.Join(", ", nonFleeceChanges.Take(5)));
        }

        // Step 3: Check for changes in .fleece/
        var statusResult = await commandRunner.RunAsync("git", "status --porcelain .fleece/", projectPath);
        if (!statusResult.Success)
        {
            logger.LogWarning("Failed to check git status: {Error}", statusResult.Error);
            return new FleeceIssueSyncResult(false, $"Failed to check status: {statusResult.Error}", 0, false);
        }

        // Count files to be committed
        var filesCount = CountChangedFiles(statusResult.Output);
        var hasFleeceChanges = filesCount > 0;

        if (hasFleeceChanges)
        {
            logger.LogInformation("Found {Count} changed files in .fleece/", filesCount);

            // Stage all .fleece/ files
            var addResult = await commandRunner.RunAsync("git", "add .fleece/", projectPath);
            if (!addResult.Success)
            {
                logger.LogWarning("Failed to stage .fleece/ files: {Error}", addResult.Error);
                return new FleeceIssueSyncResult(false, $"Failed to stage files: {addResult.Error}", 0, false);
            }

            // Commit with standardized message
            var commitResult = await commandRunner.RunAsync("git", "commit -m \"chore: sync fleece issues\"", projectPath);
            if (!commitResult.Success)
            {
                // Check if failure is due to nothing to commit (can happen if files were already staged)
                if (commitResult.Output.Contains("nothing to commit") || commitResult.Error.Contains("nothing to commit"))
                {
                    logger.LogInformation("Nothing to commit after staging");
                    hasFleeceChanges = false;
                    filesCount = 0;
                }
                else
                {
                    logger.LogWarning("Failed to commit: {Error}", commitResult.Error);
                    return new FleeceIssueSyncResult(false, $"Failed to commit: {commitResult.Error}", 0, false);
                }
            }
            else
            {
                logger.LogInformation("Committed {Count} files", filesCount);
            }
        }
        else
        {
            logger.LogInformation("No changes in .fleece/ folder to commit");
        }

        // Step 4: If we're behind remote, merge remote changes using field-level merging for .fleece/
        if (branchStatus.IsBehindRemote)
        {
            logger.LogInformation("Local branch is {Count} commits behind remote, merging remote changes", branchStatus.CommitsBehind);

            // Merge .fleece/ content using field-level merging (last-writer-wins per field)
            var mergeResult = await MergeFleeceFromRemoteAsync(projectPath, defaultBranch, ct);
            if (!mergeResult.Success)
            {
                return new FleeceIssueSyncResult(
                    Success: false,
                    ErrorMessage: mergeResult.ErrorMessage,
                    FilesCommitted: filesCount,
                    PushSucceeded: false,
                    RequiresPullFirst: true);
            }

            if (mergeResult.HasChanges)
            {
                // Stage and commit the merged .fleece/ content
                var addMergedResult = await commandRunner.RunAsync("git", "add .fleece/", projectPath);
                if (!addMergedResult.Success)
                {
                    logger.LogWarning("Failed to stage merged .fleece/ files: {Error}", addMergedResult.Error);
                    return new FleeceIssueSyncResult(false, $"Failed to stage merged files: {addMergedResult.Error}", filesCount, false);
                }

                var commitMergedResult = await commandRunner.RunAsync("git", "commit -m \"chore: merge fleece issues from remote\"", projectPath);
                if (!commitMergedResult.Success && !commitMergedResult.Output.Contains("nothing to commit") && !commitMergedResult.Error.Contains("nothing to commit"))
                {
                    logger.LogWarning("Failed to commit merged .fleece/ files: {Error}", commitMergedResult.Error);
                    return new FleeceIssueSyncResult(false, $"Failed to commit merged files: {commitMergedResult.Error}", filesCount, false);
                }
            }

            // Use git merge to integrate remote history (handles non-fleece files naturally)
            var gitMergeResult = await commandRunner.RunAsync("git", $"merge origin/{defaultBranch} --no-edit", projectPath);
            if (!gitMergeResult.Success)
            {
                var hasConflicts = DetectConflict(gitMergeResult.Error, gitMergeResult.Output);
                logger.LogWarning("Git merge failed (hasConflicts={HasConflicts}): {Error}", hasConflicts, gitMergeResult.Error);

                // Check if the conflicts are only in .fleece/ files - we can resolve those
                var fleeceConflictResolved = await TryResolveFleeceConflictsAsync(projectPath, ct);
                if (!fleeceConflictResolved)
                {
                    // Non-fleece conflicts - abort merge and report error
                    await commandRunner.RunAsync("git", "merge --abort", projectPath);

                    var conflictFiles = await GetNonFleeceChangesAsync(projectPath);
                    return new FleeceIssueSyncResult(
                        Success: false,
                        ErrorMessage: $"Failed to merge from remote: {gitMergeResult.Error}. The sync was aborted.",
                        FilesCommitted: filesCount,
                        PushSucceeded: false,
                        RequiresPullFirst: true,
                        HasNonFleeceChanges: conflictFiles.Count > 0,
                        NonFleeceChangedFiles: conflictFiles.Count > 0 ? conflictFiles : null);
                }
            }

            logger.LogInformation("Successfully merged remote changes");
        }

        // Step 5: Push to default branch
        // Only push if we have commits to push (either we committed fleece changes or we're ahead)
        var needsToPush = hasFleeceChanges || branchStatus.CommitsAhead > 0;

        // Re-check if we need to push after the merge
        if (!needsToPush)
        {
            var revListCheck = await commandRunner.RunAsync("git", $"rev-list --left-right --count origin/{defaultBranch}...HEAD", projectPath);
            if (revListCheck.Success)
            {
                var parts = revListCheck.Output.Trim().Split('\t', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && int.TryParse(parts[1], out var ahead) && ahead > 0)
                {
                    needsToPush = true;
                }
            }
        }

        if (!needsToPush)
        {
            logger.LogInformation("No changes to push, sync complete");
            return new FleeceIssueSyncResult(true, null, filesCount, true);
        }

        var pushResult = await commandRunner.RunAsync("git", $"push origin {defaultBranch}", projectPath);
        if (!pushResult.Success)
        {
            // Check if push failed because we're still behind (race condition with another push)
            if (pushResult.Error.Contains("non-fast-forward") || pushResult.Error.Contains("rejected"))
            {
                logger.LogWarning("Push rejected, remote has new changes. Error: {Error}", pushResult.Error);
                return new FleeceIssueSyncResult(
                    Success: false,
                    ErrorMessage: "Push was rejected because the remote has new changes. Please try syncing again.",
                    FilesCommitted: filesCount,
                    PushSucceeded: false,
                    RequiresPullFirst: true);
            }

            logger.LogWarning("Failed to push: {Error}", pushResult.Error);
            return new FleeceIssueSyncResult(false, $"Failed to push: {pushResult.Error}", filesCount, false);
        }

        logger.LogInformation("Successfully pushed fleece issues sync");
        return new FleeceIssueSyncResult(true, null, filesCount, true);
    }

    public async Task<PullResult> PullChangesAsync(string projectPath, string defaultBranch, CancellationToken ct = default)
    {
        logger.LogInformation("Pulling changes from {Branch} for {ProjectPath}", defaultBranch, projectPath);

        // Check for non-fleece changes first
        var nonFleeceChanges = await GetNonFleeceChangesAsync(projectPath);

        // Fetch first
        var fetchResult = await commandRunner.RunAsync("git", "fetch origin", projectPath);
        if (!fetchResult.Success)
        {
            logger.LogWarning("Failed to fetch: {Error}", fetchResult.Error);
            return new PullResult(false, false, $"Failed to fetch: {fetchResult.Error}");
        }

        // Merge .fleece/ content using field-level merging before git merge
        var mergeFleeceResult = await MergeFleeceFromRemoteAsync(projectPath, defaultBranch, ct);
        if (!mergeFleeceResult.Success)
        {
            logger.LogWarning("Failed to merge fleece content: {Error}", mergeFleeceResult.ErrorMessage);
            return new PullResult(false, false, mergeFleeceResult.ErrorMessage);
        }

        if (mergeFleeceResult.HasChanges)
        {
            // Stage and commit the merged .fleece/ content
            await commandRunner.RunAsync("git", "add .fleece/", projectPath);
            var commitResult = await commandRunner.RunAsync("git", "commit -m \"chore: merge fleece issues from remote\"", projectPath);
            if (!commitResult.Success && !commitResult.Output.Contains("nothing to commit") && !commitResult.Error.Contains("nothing to commit"))
            {
                logger.LogWarning("Failed to commit merged fleece: {Error}", commitResult.Error);
            }
        }

        // Use git merge instead of pull --rebase
        var gitMergeResult = await commandRunner.RunAsync("git", $"merge origin/{defaultBranch} --no-edit", projectPath);
        if (gitMergeResult.Success)
        {
            logger.LogInformation("Successfully pulled changes from {Branch}", defaultBranch);
            return new PullResult(true, false, null);
        }

        // Check if we can resolve .fleece/ conflicts (our merged version is correct)
        var fleeceConflictResolved = await TryResolveFleeceConflictsAsync(projectPath, ct);
        if (fleeceConflictResolved)
        {
            logger.LogInformation("Successfully pulled changes from {Branch} (resolved .fleece/ conflicts)", defaultBranch);
            return new PullResult(true, false, null);
        }

        // Non-fleece conflicts - abort merge and report
        var hasConflicts = DetectConflict(gitMergeResult.Error, gitMergeResult.Output);
        logger.LogWarning("Pull failed (hasConflicts={HasConflicts}): {Error}", hasConflicts, gitMergeResult.Error);

        await commandRunner.RunAsync("git", "merge --abort", projectPath);

        return new PullResult(
            Success: false,
            HasConflicts: hasConflicts,
            ErrorMessage: gitMergeResult.Error,
            HasNonFleeceChanges: nonFleeceChanges.Count > 0,
            NonFleeceChangedFiles: nonFleeceChanges.Count > 0 ? nonFleeceChanges : null);
    }

    public async Task<bool> StashChangesAsync(string projectPath, CancellationToken ct = default)
    {
        logger.LogInformation("Stashing changes for {ProjectPath}", projectPath);

        var result = await commandRunner.RunAsync("git", "stash push -m \"fleece-sync-auto-stash\"", projectPath);
        if (!result.Success)
        {
            logger.LogWarning("Failed to stash: {Error}", result.Error);
            return false;
        }

        logger.LogInformation("Successfully stashed changes");
        return true;
    }

    public async Task<bool> DiscardChangesAsync(string projectPath, CancellationToken ct = default)
    {
        logger.LogInformation("Discarding changes for {ProjectPath}", projectPath);

        // Abort any in-progress rebase
        await commandRunner.RunAsync("git", "rebase --abort", projectPath);

        // Reset staged changes
        var resetResult = await commandRunner.RunAsync("git", "reset HEAD", projectPath);
        if (!resetResult.Success)
        {
            logger.LogWarning("Failed to reset: {Error}", resetResult.Error);
            // Continue anyway - reset might fail if there's nothing staged
        }

        // Discard unstaged changes
        var checkoutResult = await commandRunner.RunAsync("git", "checkout -- .", projectPath);
        if (!checkoutResult.Success)
        {
            logger.LogWarning("Failed to checkout: {Error}", checkoutResult.Error);
            return false;
        }

        // Clean untracked files
        var cleanResult = await commandRunner.RunAsync("git", "clean -fd", projectPath);
        if (!cleanResult.Success)
        {
            logger.LogWarning("Failed to clean: {Error}", cleanResult.Error);
            // Non-fatal - untracked files might not exist
        }

        logger.LogInformation("Successfully discarded changes");
        return true;
    }

    public async Task<bool> DiscardNonFleeceChangesAsync(string projectPath, CancellationToken ct = default)
    {
        logger.LogInformation("Discarding non-fleece changes for {ProjectPath}", projectPath);

        // Abort any in-progress rebase
        await commandRunner.RunAsync("git", "rebase --abort", projectPath);

        // Get list of changed files excluding .fleece/
        var changedFiles = await GetNonFleeceChangesAsync(projectPath);

        if (changedFiles.Count == 0)
        {
            logger.LogInformation("No non-fleece changes to discard");
            return true;
        }

        logger.LogInformation("Discarding {Count} non-fleece changed files", changedFiles.Count);

        // Reset staged changes for non-fleece files
        foreach (var file in changedFiles)
        {
            // Try to restore the file to its original state
            var restoreResult = await commandRunner.RunAsync("git", $"checkout -- \"{file}\"", projectPath);
            if (!restoreResult.Success)
            {
                // If checkout fails, the file might be untracked - try to remove it
                var cleanResult = await commandRunner.RunAsync("git", $"clean -f -- \"{file}\"", projectPath);
                if (!cleanResult.Success)
                {
                    logger.LogWarning("Failed to discard file {File}: checkout error: {Error1}, clean error: {Error2}",
                        file, restoreResult.Error, cleanResult.Error);
                    // Continue with other files
                }
            }
        }

        // Also clean any untracked non-fleece files/directories
        // Note: We exclude .fleece to be safe
        var cleanAllResult = await commandRunner.RunAsync("git", "clean -fd --exclude=.fleece/", projectPath);
        if (!cleanAllResult.Success)
        {
            logger.LogWarning("Failed to clean untracked files: {Error}", cleanAllResult.Error);
            // Non-fatal
        }

        logger.LogInformation("Successfully discarded non-fleece changes");
        return true;
    }

    /// <summary>
    /// Merges .fleece/ content from remote using field-level (last-writer-wins) merging.
    /// Loads local issues, restores remote .fleece/ via git, loads remote issues,
    /// merges per-issue using IssueMerger, and writes the merged result.
    /// </summary>
    private async Task<(bool Success, string? ErrorMessage, bool HasChanges)> MergeFleeceFromRemoteAsync(
        string projectPath, string defaultBranch, CancellationToken ct)
    {
        var serializer = new JsonlSerializer();
        var schemaValidator = new SchemaValidator();

        try
        {
            // 1. Load local .fleece/ issues and changes into memory
            var localStorage = new JsonlStorageService(projectPath, serializer, schemaValidator);
            var localIssues = await localStorage.LoadIssuesAsync(ct);
            var localChanges = await localStorage.LoadChangesAsync(ct);

            var localIssueMap = localIssues.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);

            // 2. Restore remote .fleece/ files using git restore
            var restoreResult = await commandRunner.RunAsync("git", $"restore --source origin/{defaultBranch} -- .fleece/", projectPath);
            if (!restoreResult.Success)
            {
                logger.LogWarning("Failed to restore .fleece/ from remote: {Error}", restoreResult.Error);
                // If restore fails (e.g., no .fleece/ on remote), restore local files and continue
                await commandRunner.RunAsync("git", "checkout -- .fleece/", projectPath);
                return (true, null, false);
            }

            // 3. Load remote .fleece/ issues and changes
            var remoteStorage = new JsonlStorageService(projectPath, serializer, schemaValidator);
            var remoteIssues = await remoteStorage.LoadIssuesAsync(ct);
            var remoteChanges = await remoteStorage.LoadChangesAsync(ct);

            var remoteIssueMap = remoteIssues.ToDictionary(i => i.Id, StringComparer.OrdinalIgnoreCase);

            // 4. Merge issues using field-level merging
            var mergedIssues = new List<Issue>();
            var allIssueIds = new HashSet<string>(localIssueMap.Keys, StringComparer.OrdinalIgnoreCase);
            allIssueIds.UnionWith(remoteIssueMap.Keys);

            foreach (var issueId in allIssueIds)
            {
                var hasLocal = localIssueMap.TryGetValue(issueId, out var localIssue);
                var hasRemote = remoteIssueMap.TryGetValue(issueId, out var remoteIssue);

                if (hasLocal && hasRemote)
                {
                    // Both sides have the issue - merge using IssueMerger (last-writer-wins per field)
                    var mergeResult = _issueMerger.Merge(localIssue!, remoteIssue!);
                    mergedIssues.Add(mergeResult.MergedIssue);
                }
                else if (hasLocal)
                {
                    // Only local has it - keep it
                    mergedIssues.Add(localIssue!);
                }
                else
                {
                    // Only remote has it - keep it
                    mergedIssues.Add(remoteIssue!);
                }
            }

            // 5. Merge change records by deduplicating on ChangeId
            var mergedChanges = localChanges
                .Concat(remoteChanges)
                .GroupBy(c => c.ChangeId)
                .Select(g => g.First())
                .OrderBy(c => c.ChangedAt)
                .ToList();

            // 6. Write merged result back to .fleece/
            var mergedStorage = new JsonlStorageService(projectPath, serializer, schemaValidator);
            await mergedStorage.SaveIssuesAsync(mergedIssues, ct);
            await mergedStorage.SaveChangesAsync(mergedChanges, ct);

            logger.LogInformation("Merged {IssueCount} issues and {ChangeCount} changes from remote",
                mergedIssues.Count, mergedChanges.Count);

            return (true, null, true);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to merge .fleece/ content from remote");

            // Try to restore the working directory to a clean state
            await commandRunner.RunAsync("git", "checkout -- .fleece/", projectPath);

            return (false, $"Failed to merge fleece content: {ex.Message}", false);
        }
    }

    /// <summary>
    /// Attempts to resolve .fleece/ conflicts after a git merge by using our already-merged content.
    /// Returns true if all conflicts were in .fleece/ and were resolved.
    /// </summary>
    private async Task<bool> TryResolveFleeceConflictsAsync(string projectPath, CancellationToken ct)
    {
        // Check what files are in conflict
        var diffResult = await commandRunner.RunAsync("git", "diff --name-only --diff-filter=U", projectPath);
        if (!diffResult.Success || string.IsNullOrWhiteSpace(diffResult.Output))
        {
            return false;
        }

        var conflictedFiles = diffResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Check if ALL conflicts are in .fleece/
        var hasNonFleeceConflicts = conflictedFiles.Any(f =>
            !f.StartsWith(".fleece/", StringComparison.OrdinalIgnoreCase));

        if (hasNonFleeceConflicts)
        {
            return false;
        }

        // All conflicts are in .fleece/ - resolve with our version (which is already merged)
        var checkoutResult = await commandRunner.RunAsync("git", "checkout --ours -- .fleece/", projectPath);
        if (!checkoutResult.Success)
        {
            return false;
        }

        var addResult = await commandRunner.RunAsync("git", "add .fleece/", projectPath);
        if (!addResult.Success)
        {
            return false;
        }

        // Complete the merge with a commit
        var commitResult = await commandRunner.RunAsync("git", "commit --no-edit", projectPath);
        if (!commitResult.Success)
        {
            logger.LogWarning("Failed to complete merge commit after resolving .fleece/ conflicts: {Error}", commitResult.Error);
            return false;
        }

        logger.LogInformation("Resolved .fleece/ conflicts using merged content");
        return true;
    }

    /// <summary>
    /// Gets a list of changed files that are NOT in the .fleece/ directory.
    /// </summary>
    private async Task<IReadOnlyList<string>> GetNonFleeceChangesAsync(string projectPath)
    {
        // Get all changed files (staged and unstaged)
        var statusResult = await commandRunner.RunAsync("git", "status --porcelain", projectPath);
        if (!statusResult.Success || string.IsNullOrWhiteSpace(statusResult.Output))
        {
            return Array.Empty<string>();
        }

        var nonFleeceFiles = new List<string>();
        var lines = statusResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            // git status --porcelain format: "XY filename" where X and Y are status codes
            // The filename starts at character 3
            if (line.Length < 3) continue;

            var filename = line[3..].Trim();

            // Handle renamed files (format: "R  old -> new")
            if (filename.Contains(" -> "))
            {
                var parts = filename.Split(" -> ");
                filename = parts[^1]; // Take the new filename
            }

            // Exclude .fleece/ files
            if (!filename.StartsWith(".fleece/", StringComparison.OrdinalIgnoreCase) &&
                !filename.Equals(".fleece", StringComparison.OrdinalIgnoreCase))
            {
                nonFleeceFiles.Add(filename);
            }
        }

        return nonFleeceFiles;
    }

    private static int CountChangedFiles(string statusOutput)
    {
        if (string.IsNullOrWhiteSpace(statusOutput))
            return 0;

        // Each line in git status --porcelain represents one file
        return statusOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private static bool DetectConflict(string error, string output)
    {
        var combined = $"{error} {output}".ToLowerInvariant();

        // Common conflict indicators
        return combined.Contains("conflict") ||
               combined.Contains("would be overwritten") ||
               combined.Contains("uncommitted changes") ||
               combined.Contains("please commit or stash") ||
               combined.Contains("cannot pull with rebase") ||
               combined.Contains("merge conflict");
    }
}
