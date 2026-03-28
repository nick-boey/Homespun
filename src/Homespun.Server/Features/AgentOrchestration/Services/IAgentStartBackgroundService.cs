using Fleece.Core.Models;
using Homespun.Features.Workflows.Services;

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

    /// <summary>
    /// The workflow ID to start for this issue, when dispatched via workflow mapping.
    /// Set by QueueCoordinator when the issue's type has a workflow mapping configured.
    /// </summary>
    public string? WorkflowId { get; init; }

    /// <summary>
    /// The workflow execution ID, if this agent is being started as part of a workflow.
    /// </summary>
    public string? WorkflowExecutionId { get; init; }

    /// <summary>
    /// The index of the current workflow step being executed.
    /// </summary>
    public int? WorkflowStepIndex { get; init; }

    /// <summary>
    /// The ID of the current workflow step being executed.
    /// </summary>
    public string? WorkflowStepId { get; init; }

    /// <summary>
    /// Optional user instructions that override the prompt template.
    /// When provided, this text is sent as the initial message instead of rendering the prompt template.
    /// </summary>
    public string? UserInstructions { get; init; }

    /// <summary>
    /// Whether this request is part of a workflow execution.
    /// </summary>
    public bool IsWorkflowRequest => WorkflowExecutionId != null && WorkflowStepId != null;
}
