using Homespun.Shared.Models.Workflows;

namespace Homespun.Features.Workflows.Services;

/// <summary>
/// Result of starting a workflow execution.
/// </summary>
public class StartWorkflowResult
{
    /// <summary>
    /// Whether the workflow was started successfully.
    /// </summary>
    public required bool Success { get; set; }

    /// <summary>
    /// The execution instance if successful.
    /// </summary>
    public WorkflowExecution? Execution { get; set; }

    /// <summary>
    /// Error message if not successful.
    /// </summary>
    public string? Error { get; set; }
}

/// <summary>
/// Context provided when triggering a workflow.
/// </summary>
public class TriggerContext
{
    /// <summary>
    /// The type of trigger.
    /// </summary>
    public WorkflowTriggerType TriggerType { get; set; } = WorkflowTriggerType.Manual;

    /// <summary>
    /// Event type for event triggers.
    /// </summary>
    public WorkflowEventType? EventType { get; set; }

    /// <summary>
    /// Event payload data.
    /// </summary>
    public Dictionary<string, object>? EventData { get; set; }

    /// <summary>
    /// Initial input data for the workflow.
    /// </summary>
    public Dictionary<string, object> Input { get; set; } = [];

    /// <summary>
    /// User who triggered the workflow (for manual triggers).
    /// </summary>
    public string? TriggeredBy { get; set; }
}

/// <summary>
/// Service for executing workflows.
/// Handles the full lifecycle of workflow execution including starting, pausing,
/// resuming, and cancelling executions.
/// </summary>
public interface IWorkflowExecutionService
{
    /// <summary>
    /// Starts execution of a workflow.
    /// </summary>
    Task<StartWorkflowResult> StartWorkflowAsync(
        string projectPath,
        string workflowId,
        TriggerContext triggerContext,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the current state of an execution.
    /// </summary>
    Task<WorkflowExecution?> GetExecutionAsync(
        string projectPath,
        string executionId,
        CancellationToken ct = default);

    /// <summary>
    /// Lists all executions for a workflow.
    /// </summary>
    Task<IReadOnlyList<WorkflowExecution>> ListExecutionsAsync(
        string projectPath,
        string? workflowId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Pauses execution of a running workflow.
    /// </summary>
    Task<bool> PauseExecutionAsync(
        string projectPath,
        string executionId,
        CancellationToken ct = default);

    /// <summary>
    /// Resumes execution of a paused workflow.
    /// </summary>
    Task<bool> ResumeExecutionAsync(
        string projectPath,
        string executionId,
        CancellationToken ct = default);

    /// <summary>
    /// Cancels execution of a workflow.
    /// </summary>
    Task<bool> CancelExecutionAsync(
        string projectPath,
        string executionId,
        CancellationToken ct = default);

    /// <summary>
    /// Called when a step completes execution.
    /// </summary>
    Task OnStepCompletedAsync(
        string projectPath,
        string executionId,
        string stepId,
        Dictionary<string, object>? output,
        CancellationToken ct = default);

    /// <summary>
    /// Called when a step fails execution.
    /// </summary>
    Task OnStepFailedAsync(
        string projectPath,
        string executionId,
        string stepId,
        string errorMessage,
        CancellationToken ct = default);
}
