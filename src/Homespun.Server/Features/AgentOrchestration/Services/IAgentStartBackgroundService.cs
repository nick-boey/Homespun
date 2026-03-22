using Fleece.Core.Models;

namespace Homespun.Features.AgentOrchestration.Services;

/// <summary>
/// Background service for handling asynchronous agent startup.
/// </summary>
public interface IAgentStartBackgroundService
{
    /// <summary>
    /// Queues an agent startup task to run in the background.
    /// </summary>
    /// <param name="request">The agent start request containing all necessary parameters.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task QueueAgentStartAsync(AgentStartRequest request);
}

/// <summary>
/// Request model for starting an agent in the background.
/// </summary>
public record AgentStartRequest
{
    /// <summary>
    /// The ID of the issue to start the agent on.
    /// </summary>
    public required string IssueId { get; init; }

    /// <summary>
    /// The ID of the project containing the issue.
    /// </summary>
    public required string ProjectId { get; init; }

    /// <summary>
    /// The local file path of the project.
    /// </summary>
    public required string ProjectLocalPath { get; init; }

    /// <summary>
    /// The default branch of the project.
    /// </summary>
    public required string ProjectDefaultBranch { get; init; }

    /// <summary>
    /// The issue to start the agent on.
    /// </summary>
    public required Issue Issue { get; init; }

    /// <summary>
    /// The prompt ID to use for the agent, or null for None.
    /// </summary>
    public string? PromptId { get; init; }

    /// <summary>
    /// The base branch to create the working branch from.
    /// </summary>
    public string? BaseBranch { get; init; }

    /// <summary>
    /// The Claude model to use.
    /// </summary>
    public string Model { get; init; } = "sonnet";

    /// <summary>
    /// The branch name to use for the agent session.
    /// </summary>
    public required string BranchName { get; init; }
}
