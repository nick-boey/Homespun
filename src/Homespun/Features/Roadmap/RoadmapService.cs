using Homespun.Features.Commands;
using Homespun.Features.Git;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.PullRequests.Data.Entities;

namespace Homespun.Features.Roadmap;

/// <summary>
/// Service for managing ROADMAP.json and future changes.
/// </summary>
public class RoadmapService(
    IDataStore dataStore,
    ICommandRunner commandRunner,
    IGitWorktreeService worktreeService)
    : IRoadmapService
{
    #region 3.1 Read and Display Future Changes

    /// <summary>
    /// Gets the path to the ROADMAP.json file for a project.
    /// </summary>
    public Task<string?> GetRoadmapPathAsync(string projectId)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null) return Task.FromResult<string?>(null);

        return Task.FromResult<string?>(Path.Combine(project.LocalPath, "ROADMAP.json"));
    }

    /// <summary>
    /// Loads and parses the ROADMAP.json file for a project.
    /// </summary>
    public async Task<Roadmap?> LoadRoadmapAsync(string projectId)
    {
        var path = await GetRoadmapPathAsync(projectId);
        if (path == null || !File.Exists(path))
            return null;

        return await RoadmapParser.LoadAsync(path);
    }

    /// <summary>
    /// Gets all future changes with their calculated time values.
    /// </summary>
    public async Task<List<FutureChangeWithTime>> GetFutureChangesAsync(string projectId)
    {
        var roadmap = await LoadRoadmapAsync(projectId);
        if (roadmap == null)
            return [];

        return roadmap.GetAllChangesWithTime()
            .Select(c => new FutureChangeWithTime(c.Change, c.Time, c.Depth))
            .ToList();
    }

    /// <summary>
    /// Gets future changes grouped by their group field.
    /// </summary>
    public async Task<Dictionary<string, List<FutureChangeWithTime>>> GetFutureChangesByGroupAsync(string projectId)
    {
        var changes = await GetFutureChangesAsync(projectId);

        return changes
            .GroupBy(c => c.Change.Group)
            .ToDictionary(g => g.Key, g => g.ToList());
    }

    /// <summary>
    /// Finds a specific change by ID in the roadmap.
    /// </summary>
    public async Task<RoadmapChange?> FindChangeByIdAsync(string projectId, string changeId)
    {
        var roadmap = await LoadRoadmapAsync(projectId);
        return roadmap?.Changes.FirstOrDefault(c => c.Id == changeId);
    }

    #endregion

    #region 3.2 Promote Future Change to Current PR

    /// <summary>
    /// Promotes a future change to an active pull request with a worktree.
    /// </summary>
    public async Task<PullRequest?> PromoteChangeAsync(string projectId, string changeId)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null) return null;

        var roadmapPath = Path.Combine(project.LocalPath, "ROADMAP.json");
        if (!File.Exists(roadmapPath)) return null;

        var roadmap = await RoadmapParser.LoadAsync(roadmapPath);
        var change = roadmap.Changes.FirstOrDefault(c => c.Id == changeId);
        if (change == null) return null;

        // The Id IS the branch name in the new schema
        var branchName = change.Id;

        // Create worktree
        var worktreePath = await worktreeService.CreateWorktreeAsync(
            project.LocalPath,
            branchName,
            createBranch: true,
            baseBranch: project.DefaultBranch);

        if (worktreePath == null)
        {
            // Try without creating branch if it already exists
            worktreePath = await worktreeService.CreateWorktreeAsync(
                project.LocalPath,
                branchName);
        }

        // Create pull request entry
        var pullRequest = new PullRequest
        {
            ProjectId = projectId,
            Title = change.Title,
            Description = change.Description,
            BranchName = branchName,
            Status = OpenPullRequestStatus.InDevelopment,
            WorktreePath = worktreePath
        };

        await dataStore.AddPullRequestAsync(pullRequest);

        // Update roadmap - remove the promoted change and update parent references
        await RemoveChangeAndUpdateParentsAsync(roadmap, changeId, roadmapPath);

        return pullRequest;
    }

    private async Task RemoveChangeAndUpdateParentsAsync(Roadmap roadmap, string changeId, string roadmapPath)
    {
        // Remove the change from the list
        var changeToRemove = roadmap.Changes.FirstOrDefault(c => c.Id == changeId);
        if (changeToRemove == null) return;

        roadmap.Changes.Remove(changeToRemove);

        // Remove this change's ID from all other changes' parent lists
        foreach (var change in roadmap.Changes)
        {
            change.Parents.Remove(changeId);
        }

        await RoadmapParser.SaveAsync(roadmap, roadmapPath);
    }

    #endregion

    #region 3.3 Plan Update PRs

    /// <summary>
    /// Generates a branch name for a plan-update PR.
    /// </summary>
    public string GeneratePlanUpdateBranchName(string description)
    {
        var sanitized = GitWorktreeService.SanitizeBranchName(description);
        return $"plan-update/chore/{sanitized}";
    }

    /// <summary>
    /// Checks if a pull request only modifies ROADMAP.json (plan update only).
    /// </summary>
    public async Task<bool> IsPlanUpdateOnlyAsync(string pullRequestId)
    {
        var pullRequest = dataStore.GetPullRequest(pullRequestId);
        if (pullRequest == null) return false;

        var project = dataStore.GetProject(pullRequest.ProjectId);
        if (project == null) return false;

        var workingDir = pullRequest.WorktreePath ?? project.LocalPath;
        var baseBranch = project.DefaultBranch ?? "main";

        // Get list of changed files
        var result = await commandRunner.RunAsync(
            "git",
            $"diff --name-only origin/{baseBranch}...HEAD",
            workingDir);

        if (!result.Success) return false;

        var changedFiles = result.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Trim())
            .ToList();

        // Check if only ROADMAP.json is changed
        return changedFiles.Count == 1 && changedFiles[0] == "ROADMAP.json";
    }

    /// <summary>
    /// Validates the ROADMAP.json in a pull request's worktree.
    /// </summary>
    public async Task<bool> ValidateRoadmapAsync(string pullRequestId)
    {
        var pullRequest = dataStore.GetPullRequest(pullRequestId);
        if (pullRequest == null) return false;

        var project = dataStore.GetProject(pullRequest.ProjectId);
        if (project == null) return false;

        var workingDir = pullRequest.WorktreePath ?? project.LocalPath;
        var roadmapPath = Path.Combine(workingDir, "ROADMAP.json");

        if (!File.Exists(roadmapPath)) return true; // No roadmap is valid

        try
        {
            await RoadmapParser.LoadAsync(roadmapPath);
            return true;
        }
        catch (RoadmapValidationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Creates a plan-update pull request for modifying the roadmap.
    /// </summary>
    public async Task<PullRequest?> CreatePlanUpdatePullRequestAsync(string projectId, string description)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null) return null;

        var branchName = GeneratePlanUpdateBranchName(description);

        // Create worktree
        var worktreePath = await worktreeService.CreateWorktreeAsync(
            project.LocalPath,
            branchName,
            createBranch: true,
            baseBranch: project.DefaultBranch);

        if (worktreePath == null) return null;

        // Create pull request entry
        var pullRequest = new PullRequest
        {
            ProjectId = projectId,
            Title = $"Plan Update: {description}",
            Description = "Updates to ROADMAP.json",
            BranchName = branchName,
            Status = OpenPullRequestStatus.InDevelopment,
            WorktreePath = worktreePath
        };

        await dataStore.AddPullRequestAsync(pullRequest);

        return pullRequest;
    }

    #endregion

    #region 3.4 Add New Change

    /// <summary>
    /// Adds a new change to the roadmap. Creates ROADMAP.json if it doesn't exist.
    /// </summary>
    public async Task<bool> AddChangeAsync(string projectId, RoadmapChange change)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null) return false;

        var roadmapPath = Path.Combine(project.LocalPath, "ROADMAP.json");
        
        Roadmap roadmap;
        if (File.Exists(roadmapPath))
        {
            roadmap = await RoadmapParser.LoadAsync(roadmapPath);
        }
        else
        {
            roadmap = new Roadmap
            {
                Version = "1.1"
            };
        }

        // Add the new change to the end of the changes list
        roadmap.Changes.Add(change);

        await RoadmapParser.SaveAsync(roadmap, roadmapPath);
        return true;
    }

    #endregion

    #region 3.5 Update Change Status

    /// <summary>
    /// Updates the status of a change in the roadmap.
    /// </summary>
    public async Task<bool> UpdateChangeStatusAsync(string projectId, string changeId, FutureChangeStatus status)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null) return false;

        var roadmapPath = Path.Combine(project.LocalPath, "ROADMAP.json");
        if (!File.Exists(roadmapPath)) return false;

        var roadmap = await RoadmapParser.LoadAsync(roadmapPath);
        var change = roadmap.Changes.FirstOrDefault(c => c.Id == changeId);
        if (change == null) return false;

        change.Status = status;
        await RoadmapParser.SaveAsync(roadmap, roadmapPath);
        return true;
    }

    /// <summary>
    /// Removes a parent reference from all changes that reference it.
    /// Used when a parent change is promoted to a PR.
    /// </summary>
    public async Task<bool> RemoveParentReferenceAsync(string projectId, string parentId)
    {
        var project = dataStore.GetProject(projectId);
        if (project == null) return false;

        var roadmapPath = Path.Combine(project.LocalPath, "ROADMAP.json");
        if (!File.Exists(roadmapPath)) return false;

        var roadmap = await RoadmapParser.LoadAsync(roadmapPath);
        var modified = false;

        foreach (var change in roadmap.Changes)
        {
            if (change.Parents.Remove(parentId))
            {
                modified = true;
            }
        }

        if (modified)
        {
            await RoadmapParser.SaveAsync(roadmap, roadmapPath);
        }

        return modified;
    }

    #endregion
}
