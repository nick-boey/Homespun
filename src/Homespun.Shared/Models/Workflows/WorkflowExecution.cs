namespace Homespun.Shared.Models.Workflows;

/// <summary>
/// Status of a workflow execution.
/// </summary>
public enum WorkflowExecutionStatus
{
    /// <summary>Execution is queued but not yet started.</summary>
    Queued,

    /// <summary>Execution is currently running.</summary>
    Running,

    /// <summary>Execution is paused (e.g., waiting for gate approval).</summary>
    Paused,

    /// <summary>Execution completed successfully.</summary>
    Completed,

    /// <summary>Execution failed.</summary>
    Failed,

    /// <summary>Execution was cancelled.</summary>
    Cancelled,

    /// <summary>Execution timed out.</summary>
    TimedOut
}

/// <summary>
/// Status of a step execution within a workflow.
/// </summary>
public enum StepExecutionStatus
{
    /// <summary>Step is pending execution.</summary>
    Pending,

    /// <summary>Step is currently executing.</summary>
    Running,

    /// <summary>Step is waiting for input (e.g., gate approval).</summary>
    WaitingForInput,

    /// <summary>Step completed successfully.</summary>
    Completed,

    /// <summary>Step failed.</summary>
    Failed,

    /// <summary>Step was skipped.</summary>
    Skipped
}

/// <summary>
/// Represents a single execution of a workflow.
/// </summary>
public class WorkflowExecution
{
    /// <summary>
    /// Unique identifier for this execution.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// The workflow definition ID.
    /// </summary>
    public required string WorkflowId { get; set; }

    /// <summary>
    /// The version of the workflow being executed.
    /// </summary>
    public int WorkflowVersion { get; set; }

    /// <summary>
    /// Project ID this execution belongs to.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// Current status of the execution.
    /// </summary>
    public WorkflowExecutionStatus Status { get; set; } = WorkflowExecutionStatus.Queued;

    /// <summary>
    /// The trigger that started this execution.
    /// </summary>
    public required ExecutionTriggerInfo Trigger { get; set; }

    /// <summary>
    /// Execution context containing variables and state.
    /// </summary>
    public WorkflowContext Context { get; set; } = new();

    /// <summary>
    /// Execution state for each step.
    /// </summary>
    public List<StepExecution> StepExecutions { get; set; } = [];

    /// <summary>
    /// Index of the currently executing step.
    /// </summary>
    public int CurrentStepIndex { get; set; }

    /// <summary>
    /// When the execution was created/queued.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the execution started running.
    /// </summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// When the execution completed (success, failure, or cancellation).
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Error message if execution failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// User who triggered the execution (for manual triggers).
    /// </summary>
    public string? TriggeredBy { get; set; }
}

/// <summary>
/// Information about what triggered a workflow execution.
/// </summary>
public class ExecutionTriggerInfo
{
    /// <summary>
    /// The type of trigger.
    /// </summary>
    public WorkflowTriggerType Type { get; set; }

    /// <summary>
    /// Event type (for event triggers).
    /// </summary>
    public WorkflowEventType? EventType { get; set; }

    /// <summary>
    /// Event payload data.
    /// </summary>
    public Dictionary<string, object>? EventData { get; set; }

    /// <summary>
    /// Timestamp when the trigger occurred.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Execution state for a single step within a workflow execution.
/// </summary>
public class StepExecution
{
    /// <summary>
    /// The step ID from the workflow definition.
    /// </summary>
    public required string StepId { get; set; }

    /// <summary>
    /// The index of this step in the workflow's step list.
    /// </summary>
    public int StepIndex { get; set; }

    /// <summary>
    /// Current status of this step's execution.
    /// </summary>
    public StepExecutionStatus Status { get; set; } = StepExecutionStatus.Pending;

    /// <summary>
    /// Number of retry attempts made.
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// When execution of this step started.
    /// </summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// When execution of this step completed.
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Duration in milliseconds.
    /// </summary>
    public long? DurationMs { get; set; }

    /// <summary>
    /// Output data from this step's execution.
    /// </summary>
    public Dictionary<string, object>? Output { get; set; }

    /// <summary>
    /// Associated session ID (for agent steps).
    /// </summary>
    public string? SessionId { get; set; }

    /// <summary>
    /// Error message if step failed.
    /// </summary>
    public string? ErrorMessage { get; set; }
}
