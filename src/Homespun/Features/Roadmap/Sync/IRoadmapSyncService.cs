using Homespun.Features.PullRequests;
using Homespun.Features.PullRequests.Data.Entities;

namespace Homespun.Features.Roadmap.Sync;

/// <summary>
/// Service for managing ROADMAP.local.json synchronization across worktrees.
/// </summary>
public interface IRoadmapSyncService
{
    /// <summary>
    /// Initialize ROADMAP.local.json from the main branch if it doesn't exist.
    /// Called when a project is loaded.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <returns>True if initialized or already exists, false on error</returns>
    Task<bool> InitializeLocalRoadmapAsync(string projectId);

    /// <summary>
    /// Gets the path to ROADMAP.local.json for a project.
    /// </summary>
    /// <param name="project">The project</param>
    /// <returns>Path to ROADMAP.local.json</returns>
    string GetLocalRoadmapPath(Project project);

    /// <summary>
    /// Gets the path to ROADMAP.local.json for a project by ID.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <returns>Path to ROADMAP.local.json, or null if project not found</returns>
    string? GetLocalRoadmapPath(string projectId);

    /// <summary>
    /// Compare ROADMAP.local.json with the main branch version.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <returns>Comparison result with details of differences</returns>
    Task<RoadmapDiffResult> CompareWithMainAsync(string projectId);

    /// <summary>
    /// Sync ROADMAP.local.json to all worktrees (excluding main branch).
    /// Creates commits in each worktree.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <returns>Result with status for each worktree</returns>
    Task<SyncResult> SyncToAllWorktreesAsync(string projectId);

    /// <summary>
    /// Create a PR to update the main branch with the current ROADMAP.local.json.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <returns>Created pull request info, or null on failure</returns>
    Task<PullRequestInfo?> CreatePlanUpdatePRAsync(string projectId);

    /// <summary>
    /// Resolve a conflict in a specific worktree.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <param name="branchName">Branch name of the worktree</param>
    /// <param name="resolution">How to resolve the conflict</param>
    Task ResolveWorktreeConflictAsync(string projectId, string branchName, ConflictResolution resolution);

    /// <summary>
    /// Get all pending conflicts that need resolution.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <returns>List of conflicts awaiting resolution</returns>
    Task<List<WorktreeConflict>> GetPendingConflictsAsync(string projectId);

    /// <summary>
    /// Check if a notification about roadmap changes should be shown.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <returns>True if there are local changes not in main</returns>
    Task<bool> HasPendingChangesAsync(string projectId);
}
