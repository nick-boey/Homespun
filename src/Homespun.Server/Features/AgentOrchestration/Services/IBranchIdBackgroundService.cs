namespace Homespun.Features.AgentOrchestration.Services;

/// <summary>
/// Service for handling asynchronous branch ID generation in the background.
/// </summary>
public interface IBranchIdBackgroundService
{
    /// <summary>
    /// Queues a branch ID generation task for the specified issue.
    /// </summary>
    /// <param name="issueId">The ID of the issue to generate a branch ID for.</param>
    /// <param name="projectId">The ID of the project containing the issue.</param>
    /// <param name="title">The title to use for branch ID generation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task QueueBranchIdGenerationAsync(string issueId, string projectId, string title);
}
