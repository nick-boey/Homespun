using Homespun.Shared.Hubs;
using Homespun.Shared.Models.Workflows;
using Microsoft.AspNetCore.SignalR;

namespace Homespun.Features.Workflows.Hubs;

/// <summary>
/// SignalR hub for real-time workflow execution updates.
/// Clients subscribe to execution-specific or project-level groups.
/// </summary>
public class WorkflowHub : Hub
{
    /// <summary>
    /// Join an execution group to receive execution-specific events.
    /// </summary>
    public async Task JoinExecution(string executionId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"execution-{executionId}");
    }

    /// <summary>
    /// Leave an execution group.
    /// </summary>
    public async Task LeaveExecution(string executionId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"execution-{executionId}");
    }

    /// <summary>
    /// Join a project group to receive project-level workflow summary events.
    /// </summary>
    public async Task JoinProject(string projectId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"project-{projectId}");
    }

    /// <summary>
    /// Leave a project group.
    /// </summary>
    public async Task LeaveProject(string projectId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"project-{projectId}");
    }
}

/// <summary>
/// Extension methods for broadcasting workflow events via SignalR.
/// </summary>
public static class WorkflowHubExtensions
{
    /// <summary>
    /// Broadcasts that a step has started executing.
    /// </summary>
    public static async Task BroadcastStepStarted(
        this IHubContext<WorkflowHub> hubContext,
        string executionId,
        string stepId,
        int stepIndex,
        string? projectId = null)
    {
        await hubContext.Clients.Group($"execution-{executionId}")
            .SendAsync("StepStarted", executionId, stepId, stepIndex);

        if (!string.IsNullOrEmpty(projectId))
        {
            await hubContext.Clients.Group($"project-{projectId}")
                .SendAsync("StepStarted", executionId, stepId, stepIndex);
        }
    }

    /// <summary>
    /// Broadcasts that a step has completed.
    /// </summary>
    public static async Task BroadcastStepCompleted(
        this IHubContext<WorkflowHub> hubContext,
        string executionId,
        string stepId,
        StepExecutionStatus status,
        Dictionary<string, object>? output,
        string? projectId = null)
    {
        await hubContext.Clients.Group($"execution-{executionId}")
            .SendAsync("StepCompleted", executionId, stepId, status, output);

        if (!string.IsNullOrEmpty(projectId))
        {
            await hubContext.Clients.Group($"project-{projectId}")
                .SendAsync("StepCompleted", executionId, stepId, status, output);
        }
    }

    /// <summary>
    /// Broadcasts that a step has failed.
    /// </summary>
    public static async Task BroadcastStepFailed(
        this IHubContext<WorkflowHub> hubContext,
        string executionId,
        string stepId,
        string error,
        string? projectId = null)
    {
        await hubContext.Clients.Group($"execution-{executionId}")
            .SendAsync("StepFailed", executionId, stepId, error);

        if (!string.IsNullOrEmpty(projectId))
        {
            await hubContext.Clients.Group($"project-{projectId}")
                .SendAsync("StepFailed", executionId, stepId, error);
        }
    }

    /// <summary>
    /// Broadcasts that a step is being retried.
    /// </summary>
    public static async Task BroadcastStepRetrying(
        this IHubContext<WorkflowHub> hubContext,
        string executionId,
        string stepId,
        int retryCount,
        int maxRetries,
        string? projectId = null)
    {
        await hubContext.Clients.Group($"execution-{executionId}")
            .SendAsync("StepRetrying", executionId, stepId, retryCount, maxRetries);

        if (!string.IsNullOrEmpty(projectId))
        {
            await hubContext.Clients.Group($"project-{projectId}")
                .SendAsync("StepRetrying", executionId, stepId, retryCount, maxRetries);
        }
    }

    /// <summary>
    /// Broadcasts that a workflow execution has completed.
    /// </summary>
    public static async Task BroadcastWorkflowCompleted(
        this IHubContext<WorkflowHub> hubContext,
        string executionId,
        WorkflowExecutionStatus status,
        string? projectId = null)
    {
        await hubContext.Clients.Group($"execution-{executionId}")
            .SendAsync("WorkflowCompleted", executionId, status);

        if (!string.IsNullOrEmpty(projectId))
        {
            await hubContext.Clients.Group($"project-{projectId}")
                .SendAsync("WorkflowCompleted", executionId, status);
        }
    }

    /// <summary>
    /// Broadcasts that a workflow execution has failed.
    /// </summary>
    public static async Task BroadcastWorkflowFailed(
        this IHubContext<WorkflowHub> hubContext,
        string executionId,
        string error,
        string? projectId = null)
    {
        await hubContext.Clients.Group($"execution-{executionId}")
            .SendAsync("WorkflowFailed", executionId, error);

        if (!string.IsNullOrEmpty(projectId))
        {
            await hubContext.Clients.Group($"project-{projectId}")
                .SendAsync("WorkflowFailed", executionId, error);
        }
    }

    /// <summary>
    /// Broadcasts that a gate step is pending approval.
    /// </summary>
    public static async Task BroadcastGatePending(
        this IHubContext<WorkflowHub> hubContext,
        string executionId,
        string stepId,
        string gateName,
        string? projectId = null)
    {
        await hubContext.Clients.Group($"execution-{executionId}")
            .SendAsync("GatePending", executionId, stepId, gateName);

        if (!string.IsNullOrEmpty(projectId))
        {
            await hubContext.Clients.Group($"project-{projectId}")
                .SendAsync("GatePending", executionId, stepId, gateName);
        }
    }
}
