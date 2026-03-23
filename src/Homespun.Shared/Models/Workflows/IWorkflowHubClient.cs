using Homespun.Shared.Models.Workflows;

namespace Homespun.Shared.Hubs;

/// <summary>
/// Defines server-to-client SignalR messages for the Workflow hub.
/// </summary>
public interface IWorkflowHubClient
{
    Task StepStarted(string executionId, string stepId, int stepIndex);
    Task StepCompleted(string executionId, string stepId, StepExecutionStatus status, Dictionary<string, object>? output);
    Task StepFailed(string executionId, string stepId, string error);
    Task StepRetrying(string executionId, string stepId, int retryCount, int maxRetries);
    Task WorkflowCompleted(string executionId, WorkflowExecutionStatus status);
    Task WorkflowFailed(string executionId, string error);
    Task GatePending(string executionId, string stepId, string gateName);
}
