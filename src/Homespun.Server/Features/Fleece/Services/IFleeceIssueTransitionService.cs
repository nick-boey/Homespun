using Fleece.Core.Models;

namespace Homespun.Features.Fleece.Services;

/// <summary>
/// Service for managing status transitions for Fleece issues.
/// Coordinates between Fleece updates, agent workflow, and SignalR notifications.
/// </summary>
public interface IFleeceIssueTransitionService
{
    /// <summary>
    /// Transitions an issue to Open status (work in progress).
    /// Fleece doesn't have InProgress, so we use Open for active work.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <param name="issueId">The fleece issue ID</param>
    /// <returns>The transition result</returns>
    Task<FleeceTransitionResult> TransitionToInProgressAsync(string projectId, string issueId);

    /// <summary>
    /// Transitions an issue to Open status with an "awaiting-pr" tag.
    /// This indicates the issue is waiting for a PR to be created.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <param name="issueId">The fleece issue ID</param>
    /// <returns>The transition result</returns>
    Task<FleeceTransitionResult> TransitionToAwaitingPRAsync(string projectId, string issueId);

    /// <summary>
    /// Transitions an issue to Complete status when the PR is created.
    /// Links the PR number to the issue.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <param name="issueId">The fleece issue ID</param>
    /// <param name="prNumber">The GitHub PR number to link</param>
    /// <returns>The transition result</returns>
    Task<FleeceTransitionResult> TransitionToCompleteAsync(string projectId, string issueId, int? prNumber = null);

    /// <summary>
    /// Handles an agent failure by keeping the issue in Open status.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <param name="issueId">The fleece issue ID</param>
    /// <param name="error">The error message describing the failure</param>
    /// <returns>The transition result</returns>
    Task<FleeceTransitionResult> HandleAgentFailureAsync(string projectId, string issueId, string error);

    /// <summary>
    /// Gets the current status of an issue.
    /// </summary>
    /// <param name="projectId">The project ID</param>
    /// <param name="issueId">The fleece issue ID</param>
    /// <returns>The current status or null if the issue doesn't exist</returns>
    Task<IssueStatus?> GetStatusAsync(string projectId, string issueId);
}

/// <summary>
/// Result of a Fleece issue status transition operation.
/// </summary>
public class FleeceTransitionResult
{
    public bool Success { get; init; }
    public IssueStatus? PreviousStatus { get; init; }
    public IssueStatus? NewStatus { get; init; }
    public string? Error { get; init; }
    public int? PrNumber { get; init; }

    public static FleeceTransitionResult Ok(IssueStatus previousStatus, IssueStatus newStatus, int? prNumber = null)
        => new() { Success = true, PreviousStatus = previousStatus, NewStatus = newStatus, PrNumber = prNumber };

    public static FleeceTransitionResult Fail(string error, IssueStatus? currentStatus = null)
        => new() { Success = false, Error = error, PreviousStatus = currentStatus };
}
