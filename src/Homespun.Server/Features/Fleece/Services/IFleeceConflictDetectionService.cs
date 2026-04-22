using Homespun.Shared.Models.Issues;

namespace Homespun.Features.Fleece.Services;

/// <summary>
/// Service for detecting conflicts between agent and main branch changes.
/// </summary>
public interface IFleeceConflictDetectionService
{
    /// <summary>
    /// Detects conflicts in the given changes.
    /// </summary>
    Task<List<IssueConflictDto>> DetectConflictsAsync(
        string projectId,
        string sessionId,
        List<IssueChangeDto> changes,
        CancellationToken cancellationToken = default);
}
