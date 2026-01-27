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
    public async Task<FleeceIssueSyncResult> SyncAsync(string projectPath, string defaultBranch, CancellationToken ct = default)
    {
        logger.LogInformation("Starting fleece issues sync for {ProjectPath} to branch {Branch}", projectPath, defaultBranch);

        // Check for changes in .fleece/
        var statusResult = await commandRunner.RunAsync("git", "status --porcelain .fleece/", projectPath);
        if (!statusResult.Success)
        {
            logger.LogWarning("Failed to check git status: {Error}", statusResult.Error);
            return new FleeceIssueSyncResult(false, $"Failed to check status: {statusResult.Error}", 0, false);
        }

        // If no changes, return success with 0 files
        if (string.IsNullOrWhiteSpace(statusResult.Output))
        {
            logger.LogInformation("No changes in .fleece/ folder to sync");
            return new FleeceIssueSyncResult(true, null, 0, true);
        }

        // Count files to be committed
        var filesCount = CountChangedFiles(statusResult.Output);
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
                return new FleeceIssueSyncResult(true, null, 0, true);
            }

            logger.LogWarning("Failed to commit: {Error}", commitResult.Error);
            return new FleeceIssueSyncResult(false, $"Failed to commit: {commitResult.Error}", 0, false);
        }

        logger.LogInformation("Committed {Count} files", filesCount);

        // Push to default branch
        var pushResult = await commandRunner.RunAsync("git", $"push origin {defaultBranch}", projectPath);
        if (!pushResult.Success)
        {
            logger.LogWarning("Failed to push: {Error}", pushResult.Error);
            return new FleeceIssueSyncResult(false, $"Failed to push: {pushResult.Error}", filesCount, false);
        }

        logger.LogInformation("Successfully pushed fleece issues sync");
        return new FleeceIssueSyncResult(true, null, filesCount, true);
    }

    public async Task<PullResult> PullChangesAsync(string projectPath, string defaultBranch, CancellationToken ct = default)
    {
        logger.LogInformation("Pulling changes from {Branch} for {ProjectPath}", defaultBranch, projectPath);

        // Fetch first
        var fetchResult = await commandRunner.RunAsync("git", "fetch origin", projectPath);
        if (!fetchResult.Success)
        {
            logger.LogWarning("Failed to fetch: {Error}", fetchResult.Error);
            return new PullResult(false, false, $"Failed to fetch: {fetchResult.Error}");
        }

        // Pull with rebase
        var pullResult = await commandRunner.RunAsync("git", $"pull origin {defaultBranch} --rebase", projectPath);
        if (pullResult.Success)
        {
            logger.LogInformation("Successfully pulled changes from {Branch}", defaultBranch);
            return new PullResult(true, false, null);
        }

        // Check if it's a conflict
        var hasConflicts = DetectConflict(pullResult.Error, pullResult.Output);
        logger.LogWarning("Pull failed (hasConflicts={HasConflicts}): {Error}", hasConflicts, pullResult.Error);

        return new PullResult(false, hasConflicts, pullResult.Error);
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
