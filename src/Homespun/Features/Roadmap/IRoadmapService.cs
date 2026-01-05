using Homespun.Features.PullRequests.Data.Entities;

namespace Homespun.Features.Roadmap;

/// <summary>
/// Interface for ROADMAP.json operations.
/// </summary>
public interface IRoadmapService
{
    Task<string?> GetRoadmapPathAsync(string projectId);
    Task<Roadmap?> LoadRoadmapAsync(string projectId);
    Task<List<FutureChangeWithTime>> GetFutureChangesAsync(string projectId);
    Task<Dictionary<string, List<FutureChangeWithTime>>> GetFutureChangesByGroupAsync(string projectId);
    Task<FutureChange?> FindChangeByIdAsync(string projectId, string changeId);
    Task<PullRequest?> PromoteChangeAsync(string projectId, string changeId);
    Task<bool> IsPlanUpdateOnlyAsync(string pullRequestId);
    Task<bool> ValidateRoadmapAsync(string pullRequestId);
    Task<PullRequest?> CreatePlanUpdatePullRequestAsync(string projectId, string description);
    string GeneratePlanUpdateBranchName(string description);
    
    /// <summary>
    /// Adds a new change to the roadmap. If no ROADMAP.json exists, creates one.
    /// </summary>
    Task<bool> AddChangeAsync(string projectId, FutureChange change);

    /// <summary>
    /// Updates the status of a change in the roadmap.
    /// </summary>
    Task<bool> UpdateChangeStatusAsync(string projectId, string changeId, FutureChangeStatus status);

    /// <summary>
    /// Removes a parent reference from all changes that reference it.
    /// Used when a parent change is promoted to a PR.
    /// </summary>
    Task<bool> RemoveParentReferenceAsync(string projectId, string parentId);
}
