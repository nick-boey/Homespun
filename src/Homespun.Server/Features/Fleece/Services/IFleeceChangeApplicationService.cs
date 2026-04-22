using Homespun.Shared.Models.Issues;

namespace Homespun.Features.Fleece.Services;

/// <summary>
/// Service for applying agent changes back to the main branch.
/// </summary>
public interface IFleeceChangeApplicationService
{
    /// <summary>
    /// Applies agent changes to the main branch, handling conflicts as specified.
    /// </summary>
    Task<ApplyAgentChangesResponse> ApplyChangesAsync(
        string projectId,
        string sessionId,
        ConflictResolutionStrategy conflictStrategy,
        bool dryRun = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves specific conflicts with manual resolutions.
    /// </summary>
    Task<ApplyAgentChangesResponse> ResolveConflictsAsync(
        string projectId,
        string sessionId,
        List<ConflictResolution> resolutions,
        CancellationToken cancellationToken = default);
}
