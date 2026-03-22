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
    /// <param name="projectPath">The path to the project directory.</param>
    /// <param name="workflowId">The workflow ID to execute.</param>
    /// <param name="triggerContext">Context about how the workflow was triggered.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result containing the execution instance or error.</returns>
    Task<StartWorkflowResult> StartWorkflowAsync(
        string projectPath,
        string workflowId,
        TriggerContext triggerContext,
        CancellationToken ct = default);

    /// <summary>
    /// Gets the current state of an execution.
    /// </summary>
    /// <param name="projectPath">The path to the project directory.</param>
    /// <param name="executionId">The execution ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The execution, or null if not found.</returns>
    Task<WorkflowExecution?> GetExecutionAsync(
        string projectPath,
        string executionId,
        CancellationToken ct = default);

    /// <summary>
    /// Lists all executions for a workflow.
    /// </summary>
    /// <param name="projectPath">The path to the project directory.</param>
    /// <param name="workflowId">Optional workflow ID to filter by.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of executions.</returns>
    Task<IReadOnlyList<WorkflowExecution>> ListExecutionsAsync(
        string projectPath,
        string? workflowId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Pauses execution of a running workflow.
    /// </summary>
    /// <param name="projectPath">The path to the project directory.</param>
    /// <param name="executionId">The execution ID to pause.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if paused successfully, false if not found or not running.</returns>
    Task<bool> PauseExecutionAsync(
        string projectPath,
        string executionId,
        CancellationToken ct = default);

    /// <summary>
    /// Resumes execution of a paused workflow.
    /// </summary>
    /// <param name="projectPath">The path to the project directory.</param>
    /// <param name="executionId">The execution ID to resume.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if resumed successfully, false if not found or not paused.</returns>
    Task<bool> ResumeExecutionAsync(
        string projectPath,
        string executionId,
        CancellationToken ct = default);

    /// <summary>
    /// Cancels execution of a workflow.
    /// </summary>
    /// <param name="projectPath">The path to the project directory.</param>
    /// <param name="executionId">The execution ID to cancel.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if cancelled successfully, false if not found or already completed.</returns>
    Task<bool> CancelExecutionAsync(
        string projectPath,
        string executionId,
        CancellationToken ct = default);

    /// <summary>
    /// Called when a node completes execution.
    /// </summary>
    /// <param name="projectPath">The path to the project directory.</param>
    /// <param name="executionId">The execution ID.</param>
    /// <param name="nodeId">The node ID that completed.</param>
    /// <param name="output">Output data from the node.</param>
    /// <param name="ct">Cancellation token.</param>
    Task OnNodeCompletedAsync(
        string projectPath,
        string executionId,
        string nodeId,
        Dictionary<string, object>? output,
        CancellationToken ct = default);

    /// <summary>
    /// Called when a node fails execution.
    /// </summary>
    /// <param name="projectPath">The path to the project directory.</param>
    /// <param name="executionId">The execution ID.</param>
    /// <param name="nodeId">The node ID that failed.</param>
    /// <param name="errorMessage">Error message describing the failure.</param>
    /// <param name="ct">Cancellation token.</param>
    Task OnNodeFailedAsync(
        string projectPath,
        string executionId,
        string nodeId,
        string errorMessage,
        CancellationToken ct = default);
}
