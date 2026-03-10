using Homespun.Features.Fleece.Services;
using Homespun.Shared.Models.Issues;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Mock implementation of IFleeceChangeApplicationService for testing.
/// </summary>
public class MockFleeceChangeApplicationService : IFleeceChangeApplicationService
{
    private readonly ILogger<MockFleeceChangeApplicationService> _logger;

    public MockFleeceChangeApplicationService(ILogger<MockFleeceChangeApplicationService> logger)
    {
        _logger = logger;
    }

    public Task<ApplyAgentChangesResponse> ApplyChangesAsync(
        string projectId,
        string sessionId,
        ConflictResolutionStrategy conflictStrategy,
        bool dryRun = false,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] ApplyChangesAsync: projectId={ProjectId}, sessionId={SessionId}, dryRun={DryRun}",
            projectId, sessionId, dryRun);

        return Task.FromResult(new ApplyAgentChangesResponse
        {
            Success = true,
            Message = dryRun ? "Would apply 0 changes" : "Applied 0 changes successfully",
            Changes = [],
            WouldApply = dryRun
        });
    }

    public Task<ApplyAgentChangesResponse> ResolveConflictsAsync(
        string projectId,
        string sessionId,
        List<ConflictResolution> resolutions,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] ResolveConflictsAsync: projectId={ProjectId}, sessionId={SessionId}, resolutions={Count}",
            projectId, sessionId, resolutions.Count);

        return Task.FromResult(new ApplyAgentChangesResponse
        {
            Success = true,
            Message = $"Resolved {resolutions.Count} conflicts successfully",
            Changes = []
        });
    }
}

/// <summary>
/// Mock implementation of IFleeceConflictDetectionService for testing.
/// </summary>
public class MockFleeceConflictDetectionService : IFleeceConflictDetectionService
{
    private readonly ILogger<MockFleeceConflictDetectionService> _logger;

    public MockFleeceConflictDetectionService(ILogger<MockFleeceConflictDetectionService> logger)
    {
        _logger = logger;
    }

    public Task<List<IssueConflictDto>> DetectConflictsAsync(
        string projectId,
        string sessionId,
        List<IssueChangeDto> changes,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] DetectConflictsAsync: projectId={ProjectId}, sessionId={SessionId}, changes={Count}",
            projectId, sessionId, changes.Count);

        // Return empty list - no conflicts in mock mode
        return Task.FromResult(new List<IssueConflictDto>());
    }
}

/// <summary>
/// Mock implementation of IFleeceChangeDetectionService for testing.
/// </summary>
public class MockFleeceChangeDetectionService : IFleeceChangeDetectionService
{
    private readonly ILogger<MockFleeceChangeDetectionService> _logger;

    public MockFleeceChangeDetectionService(ILogger<MockFleeceChangeDetectionService> logger)
    {
        _logger = logger;
    }

    public Task<List<IssueChangeDto>> DetectChangesAsync(
        string projectId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] DetectChangesAsync: projectId={ProjectId}, sessionId={SessionId}",
            projectId, sessionId);

        // Return empty list - no changes detected in mock mode
        return Task.FromResult(new List<IssueChangeDto>());
    }
}
