using System.Collections.Concurrent;
using System.Text.Json;
using Homespun.Features.Commands;
using Homespun.Features.Git;
using Homespun.Features.GitHub;
using Homespun.Features.Notifications;
using Homespun.Features.PullRequests;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.PullRequests.Data.Entities;
using Microsoft.AspNetCore.SignalR;

namespace Homespun.Features.Roadmap.Sync;

/// <summary>
/// Service for managing ROADMAP.local.json synchronization.
/// </summary>
public class RoadmapSyncService : IRoadmapSyncService
{
    private const string LocalRoadmapFileName = "ROADMAP.local.json";
    private const string RoadmapFileName = "ROADMAP.json";
    private const string SyncCommitMessage = "chore: sync ROADMAP.json";
    private const string LocalChangesCommitMessage = "chore: local roadmap changes";
    private const string PlanUpdateCommitMessage = "chore: update roadmap";

    private readonly IDataStore _dataStore;
    private readonly ICommandRunner _commandRunner;
    private readonly IGitWorktreeService _worktreeService;
    private readonly IGitHubService _gitHubService;
    private readonly INotificationService _notificationService;
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly ILogger<RoadmapSyncService> _logger;

    // Track pending conflicts per project
    private readonly ConcurrentDictionary<string, List<WorktreeConflict>> _pendingConflicts = new();

    public RoadmapSyncService(
        IDataStore dataStore,
        ICommandRunner commandRunner,
        IGitWorktreeService worktreeService,
        IGitHubService gitHubService,
        INotificationService notificationService,
        IHubContext<NotificationHub> hubContext,
        ILogger<RoadmapSyncService> logger)
    {
        _dataStore = dataStore;
        _commandRunner = commandRunner;
        _worktreeService = worktreeService;
        _gitHubService = gitHubService;
        _notificationService = notificationService;
        _hubContext = hubContext;
        _logger = logger;
    }

    public string GetLocalRoadmapPath(Project project)
    {
        // ROADMAP.local.json is stored at the project root level
        // e.g., ~/.homespun/src/<repo>/ROADMAP.local.json
        var parentDir = Path.GetDirectoryName(project.LocalPath);
        if (string.IsNullOrEmpty(parentDir))
        {
            throw new InvalidOperationException($"Cannot determine parent directory of {project.LocalPath}");
        }
        return Path.Combine(parentDir, LocalRoadmapFileName);
    }

    public string? GetLocalRoadmapPath(string projectId)
    {
        var project = _dataStore.GetProject(projectId);
        return project == null ? null : GetLocalRoadmapPath(project);
    }

    public async Task<bool> InitializeLocalRoadmapAsync(string projectId)
    {
        var project = _dataStore.GetProject(projectId);
        if (project == null)
        {
            _logger.LogWarning("Project {ProjectId} not found", projectId);
            return false;
        }

        var localPath = GetLocalRoadmapPath(project);

        // If local file already exists, don't overwrite
        if (File.Exists(localPath))
        {
            _logger.LogDebug("ROADMAP.local.json already exists at {Path}", localPath);
            return true;
        }

        // Get ROADMAP.json from main branch using git show
        var mainRoadmapContent = await GetMainBranchRoadmapAsync(project);

        if (mainRoadmapContent == null)
        {
            // No ROADMAP.json in main branch - create empty one
            _logger.LogInformation("No ROADMAP.json in main branch for project {ProjectId}, creating empty roadmap", projectId);
            var emptyRoadmap = new Roadmap { Version = "1.1" };
            await RoadmapParser.SaveAsync(emptyRoadmap, localPath);
        }
        else
        {
            // Save content from main branch
            await File.WriteAllTextAsync(localPath, mainRoadmapContent);
            _logger.LogInformation("Initialized ROADMAP.local.json from main branch for project {ProjectId}", projectId);
        }

        return true;
    }

    public async Task<RoadmapDiffResult> CompareWithMainAsync(string projectId)
    {
        var project = _dataStore.GetProject(projectId);
        if (project == null)
        {
            return RoadmapDiffResult.NoLocalFile();
        }

        var localPath = GetLocalRoadmapPath(project);
        if (!File.Exists(localPath))
        {
            return RoadmapDiffResult.NoLocalFile();
        }

        // Load local roadmap
        Roadmap localRoadmap;
        try
        {
            localRoadmap = await RoadmapParser.LoadAsync(localPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse local roadmap at {Path}", localPath);
            return new RoadmapDiffResult
            {
                HasChanges = false,
                LocalExists = true,
                MainHasRoadmap = true
            };
        }

        // Get main branch roadmap
        var mainContent = await GetMainBranchRoadmapAsync(project);
        if (mainContent == null)
        {
            // Main has no roadmap - local has changes if it has any changes
            return new RoadmapDiffResult
            {
                HasChanges = localRoadmap.Changes.Count > 0,
                LocalLastUpdated = localRoadmap.LastUpdated,
                LocalExists = true,
                MainHasRoadmap = false,
                AddedChanges = localRoadmap.Changes.Select(c => c.Id).ToList()
            };
        }

        Roadmap mainRoadmap;
        try
        {
            mainRoadmap = RoadmapParser.Parse(mainContent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse main branch roadmap");
            return new RoadmapDiffResult
            {
                HasChanges = false,
                LocalExists = true,
                MainHasRoadmap = true
            };
        }

        // Compare the two roadmaps
        return CompareRoadmaps(localRoadmap, mainRoadmap);
    }

    public async Task<SyncResult> SyncToAllWorktreesAsync(string projectId)
    {
        var project = _dataStore.GetProject(projectId);
        if (project == null)
        {
            _logger.LogWarning("Project {ProjectId} not found for sync", projectId);
            return SyncResult.Successful([]);
        }

        var localPath = GetLocalRoadmapPath(project);
        if (!File.Exists(localPath))
        {
            _logger.LogWarning("ROADMAP.local.json not found at {Path}", localPath);
            return SyncResult.Successful([]);
        }

        // Read local roadmap content
        var localContent = await File.ReadAllTextAsync(localPath);

        // Get all worktrees
        var worktrees = await _worktreeService.ListWorktreesAsync(project.LocalPath);
        var statuses = new List<WorktreeSyncStatus>();
        var newConflicts = new List<WorktreeConflict>();

        foreach (var worktree in worktrees)
        {
            // Skip main branch worktree
            var branchName = worktree.Branch?.Replace("refs/heads/", "");
            if (branchName == project.DefaultBranch)
            {
                statuses.Add(new WorktreeSyncStatus
                {
                    WorktreePath = worktree.Path,
                    BranchName = branchName ?? "unknown",
                    Status = SyncStatus.Skipped
                });
                continue;
            }

            var status = await SyncWorktreeAsync(project, worktree, localContent, newConflicts);
            statuses.Add(status);
        }

        // Update pending conflicts
        if (newConflicts.Count > 0)
        {
            _pendingConflicts.AddOrUpdate(
                projectId,
                newConflicts,
                (_, existing) =>
                {
                    // Merge new conflicts, keeping existing unresolved ones
                    var merged = existing.Where(c => !c.IsResolved).ToList();
                    foreach (var newConflict in newConflicts)
                    {
                        if (!merged.Any(c => c.BranchName == newConflict.BranchName))
                        {
                            merged.Add(newConflict);
                        }
                    }
                    return merged;
                });

            // Notify about conflicts
            foreach (var conflict in newConflicts)
            {
                var notification = new Notification
                {
                    Type = NotificationType.ActionRequired,
                    Title = "Roadmap Conflict",
                    Message = $"Branch '{conflict.BranchName}' has conflicting roadmap changes",
                    ProjectId = projectId,
                    ActionLabel = "Resolve",
                    DeduplicationKey = $"roadmap-conflict-{projectId}-{conflict.BranchName}"
                };
                _notificationService.AddNotification(notification);
                await _hubContext.BroadcastNotificationAdded(notification);
            }
        }

        var result = SyncResult.Successful(statuses);
        _logger.LogInformation(
            "Sync completed for project {ProjectId}: {SyncedCount} synced, {ConflictCount} conflicts",
            projectId, result.SyncedCount, result.Conflicts.Count);

        return result;
    }

    public async Task<PullRequestInfo?> CreatePlanUpdatePRAsync(string projectId)
    {
        var project = _dataStore.GetProject(projectId);
        if (project == null)
        {
            _logger.LogWarning("Project {ProjectId} not found for plan update PR", projectId);
            return null;
        }

        var localPath = GetLocalRoadmapPath(project);
        if (!File.Exists(localPath))
        {
            _logger.LogWarning("ROADMAP.local.json not found at {Path}", localPath);
            return null;
        }

        // Generate unique branch name
        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
        var branchName = $"plan-update/chore/roadmap-{timestamp}";

        // Create branch from main
        var worktreePath = await _worktreeService.CreateWorktreeAsync(
            project.LocalPath,
            branchName,
            createBranch: true,
            baseBranch: project.DefaultBranch);

        if (worktreePath == null)
        {
            _logger.LogError("Failed to create worktree for plan update PR");
            return null;
        }

        try
        {
            // Copy ROADMAP.local.json to worktree as ROADMAP.json
            var localContent = await File.ReadAllTextAsync(localPath);
            var worktreeRoadmapPath = Path.Combine(worktreePath, RoadmapFileName);
            await File.WriteAllTextAsync(worktreeRoadmapPath, localContent);

            // Stage and commit
            var addResult = await _commandRunner.RunAsync("git", $"add \"{RoadmapFileName}\"", worktreePath);
            if (!addResult.Success)
            {
                _logger.LogError("Failed to stage ROADMAP.json: {Error}", addResult.Error);
                return null;
            }

            var commitResult = await _commandRunner.RunAsync(
                "git",
                $"commit -m \"{PlanUpdateCommitMessage}\"",
                worktreePath);

            if (!commitResult.Success && !commitResult.Error.Contains("nothing to commit"))
            {
                _logger.LogError("Failed to commit ROADMAP.json: {Error}", commitResult.Error);
                return null;
            }

            // Create pull request entry
            var pullRequest = new PullRequest
            {
                ProjectId = projectId,
                Title = "Update Roadmap",
                Description = "Syncs roadmap changes to the main branch",
                BranchName = branchName,
                Status = OpenPullRequestStatus.InDevelopment,
                WorktreePath = worktreePath
            };

            await _dataStore.AddPullRequestAsync(pullRequest);

            // Push and create PR
            var prInfo = await _gitHubService.CreatePullRequestAsync(projectId, pullRequest.Id);
            if (prInfo == null)
            {
                _logger.LogError("Failed to create GitHub PR for plan update");
                return null;
            }

            // Dismiss the notification
            _notificationService.DismissNotificationsByKey($"roadmap-changes-{projectId}");
            await _hubContext.BroadcastNotificationDismissed($"roadmap-changes-{projectId}");

            _logger.LogInformation("Created plan update PR #{PrNumber} for project {ProjectId}", prInfo.Number, projectId);
            return prInfo;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating plan update PR for project {ProjectId}", projectId);
            // Cleanup worktree on failure
            await _worktreeService.RemoveWorktreeAsync(project.LocalPath, worktreePath);
            return null;
        }
    }

    public async Task ResolveWorktreeConflictAsync(string projectId, string branchName, ConflictResolution resolution)
    {
        var project = _dataStore.GetProject(projectId);
        if (project == null) return;

        // Find the conflict
        if (!_pendingConflicts.TryGetValue(projectId, out var conflicts)) return;

        var conflict = conflicts.FirstOrDefault(c => c.BranchName == branchName && !c.IsResolved);
        if (conflict == null) return;

        if (resolution == ConflictResolution.UseNew)
        {
            // Overwrite with local roadmap
            var localPath = GetLocalRoadmapPath(project);
            var localContent = await File.ReadAllTextAsync(localPath);
            var worktreeRoadmapPath = Path.Combine(conflict.WorktreePath, RoadmapFileName);

            await File.WriteAllTextAsync(worktreeRoadmapPath, localContent);

            // Commit the change
            var addResult = await _commandRunner.RunAsync("git", $"add \"{RoadmapFileName}\"", conflict.WorktreePath);
            if (addResult.Success)
            {
                await _commandRunner.RunAsync(
                    "git",
                    $"commit -m \"{SyncCommitMessage} (resolved conflict)\"",
                    conflict.WorktreePath);
            }

            _logger.LogInformation("Resolved conflict for branch {Branch} with UseNew", branchName);
        }
        else
        {
            // KeepExisting - just mark as resolved
            _logger.LogInformation("Resolved conflict for branch {Branch} with KeepExisting", branchName);
        }

        // Mark conflict as resolved
        conflict.IsResolved = true;
        conflict.Resolution = resolution;

        // Dismiss notification
        _notificationService.DismissNotificationsByKey($"roadmap-conflict-{projectId}-{branchName}");
        await _hubContext.BroadcastNotificationDismissed($"roadmap-conflict-{projectId}-{branchName}");
    }

    public Task<List<WorktreeConflict>> GetPendingConflictsAsync(string projectId)
    {
        if (_pendingConflicts.TryGetValue(projectId, out var conflicts))
        {
            return Task.FromResult(conflicts.Where(c => !c.IsResolved).ToList());
        }
        return Task.FromResult(new List<WorktreeConflict>());
    }

    public async Task<bool> HasPendingChangesAsync(string projectId)
    {
        var diff = await CompareWithMainAsync(projectId);
        return diff.HasChanges;
    }

    private async Task<string?> GetMainBranchRoadmapAsync(Project project)
    {
        var result = await _commandRunner.RunAsync(
            "git",
            $"show {project.DefaultBranch}:{RoadmapFileName}",
            project.LocalPath);

        if (!result.Success)
        {
            // File might not exist in main branch
            if (result.Error.Contains("does not exist") || result.Error.Contains("Path") || result.Error.Contains("fatal"))
            {
                return null;
            }
            _logger.LogWarning("Failed to get roadmap from main branch: {Error}", result.Error);
            return null;
        }

        return result.Output;
    }

    private RoadmapDiffResult CompareRoadmaps(Roadmap local, Roadmap main)
    {
        var localIds = local.Changes.Select(c => c.Id).ToHashSet();
        var mainIds = main.Changes.Select(c => c.Id).ToHashSet();

        var added = localIds.Except(mainIds).ToList();
        var removed = mainIds.Except(localIds).ToList();
        var common = localIds.Intersect(mainIds);

        var modified = new List<string>();
        foreach (var id in common)
        {
            var localChange = local.Changes.First(c => c.Id == id);
            var mainChange = main.Changes.First(c => c.Id == id);

            if (!ChangesAreEqual(localChange, mainChange))
            {
                modified.Add(id);
            }
        }

        return new RoadmapDiffResult
        {
            HasChanges = added.Count > 0 || removed.Count > 0 || modified.Count > 0,
            LocalLastUpdated = local.LastUpdated,
            MainLastUpdated = main.LastUpdated,
            LocalExists = true,
            MainHasRoadmap = true,
            AddedChanges = added,
            RemovedChanges = removed,
            ModifiedChanges = modified
        };
    }

    private static bool ChangesAreEqual(FutureChange a, FutureChange b)
    {
        // Compare relevant fields
        return a.Id == b.Id &&
               a.Title == b.Title &&
               a.Description == b.Description &&
               a.Instructions == b.Instructions &&
               a.Status == b.Status &&
               a.Priority == b.Priority &&
               a.EstimatedComplexity == b.EstimatedComplexity &&
               a.Parents.SequenceEqual(b.Parents);
    }

    private async Task<WorktreeSyncStatus> SyncWorktreeAsync(
        Project project,
        WorktreeInfo worktree,
        string localContent,
        List<WorktreeConflict> newConflicts)
    {
        var branchName = worktree.Branch?.Replace("refs/heads/", "") ?? "unknown";
        var worktreeRoadmapPath = Path.Combine(worktree.Path, RoadmapFileName);

        try
        {
            // Check for uncommitted changes to ROADMAP.json
            var statusResult = await _commandRunner.RunAsync(
                "git",
                $"status --porcelain \"{RoadmapFileName}\"",
                worktree.Path);

            if (statusResult.Success && !string.IsNullOrWhiteSpace(statusResult.Output))
            {
                // Has uncommitted changes - commit them first
                _logger.LogInformation("Committing uncommitted roadmap changes in {Branch}", branchName);

                var addResult = await _commandRunner.RunAsync("git", $"add \"{RoadmapFileName}\"", worktree.Path);
                if (addResult.Success)
                {
                    await _commandRunner.RunAsync(
                        "git",
                        $"commit -m \"{LocalChangesCommitMessage}\"",
                        worktree.Path);
                }
            }

            // Check if worktree has committed changes that differ from local
            if (File.Exists(worktreeRoadmapPath))
            {
                var worktreeContent = await File.ReadAllTextAsync(worktreeRoadmapPath);

                // Compare content (normalize line endings)
                var normalizedLocal = localContent.Replace("\r\n", "\n").Trim();
                var normalizedWorktree = worktreeContent.Replace("\r\n", "\n").Trim();

                if (normalizedLocal == normalizedWorktree)
                {
                    // Already in sync
                    return new WorktreeSyncStatus
                    {
                        WorktreePath = worktree.Path,
                        BranchName = branchName,
                        Status = SyncStatus.AlreadySynced
                    };
                }

                // Check if there are committed changes different from what we want to sync
                // by comparing worktree ROADMAP.json to main branch
                var mainContent = await GetMainBranchRoadmapAsync(project);
                var normalizedMain = mainContent?.Replace("\r\n", "\n").Trim();

                if (normalizedWorktree != normalizedMain)
                {
                    // Worktree has committed changes that are different from both local and main
                    // This is a conflict
                    var pullRequest = _dataStore.GetPullRequestsByProject(project.Id)
                        .FirstOrDefault(pr => pr.BranchName == branchName);

                    var conflict = new WorktreeConflict
                    {
                        WorktreePath = worktree.Path,
                        BranchName = branchName,
                        PullRequestId = pullRequest?.Id,
                        PullRequestTitle = pullRequest?.Title,
                        Description = "This branch has committed roadmap changes that differ from the main roadmap."
                    };
                    newConflicts.Add(conflict);

                    return new WorktreeSyncStatus
                    {
                        WorktreePath = worktree.Path,
                        BranchName = branchName,
                        Status = SyncStatus.ConflictNeedsResolution,
                        ConflictDetails = conflict.Description
                    };
                }
            }

            // No conflict - sync the file
            await File.WriteAllTextAsync(worktreeRoadmapPath, localContent);

            // Stage and commit
            var stageResult = await _commandRunner.RunAsync("git", $"add \"{RoadmapFileName}\"", worktree.Path);
            if (!stageResult.Success)
            {
                return new WorktreeSyncStatus
                {
                    WorktreePath = worktree.Path,
                    BranchName = branchName,
                    Status = SyncStatus.Error,
                    ErrorMessage = $"Failed to stage: {stageResult.Error}"
                };
            }

            var commitResult = await _commandRunner.RunAsync(
                "git",
                $"commit -m \"{SyncCommitMessage}\"",
                worktree.Path);

            if (!commitResult.Success && !commitResult.Error.Contains("nothing to commit"))
            {
                return new WorktreeSyncStatus
                {
                    WorktreePath = worktree.Path,
                    BranchName = branchName,
                    Status = SyncStatus.Error,
                    ErrorMessage = $"Failed to commit: {commitResult.Error}"
                };
            }

            return new WorktreeSyncStatus
            {
                WorktreePath = worktree.Path,
                BranchName = branchName,
                Status = SyncStatus.Committed
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing worktree {Path}", worktree.Path);
            return new WorktreeSyncStatus
            {
                WorktreePath = worktree.Path,
                BranchName = branchName,
                Status = SyncStatus.Error,
                ErrorMessage = ex.Message
            };
        }
    }
}
