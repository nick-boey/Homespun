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
/// Status of a node execution within a workflow.
/// </summary>
public enum NodeExecutionStatus
{
    /// <summary>Node is pending execution.</summary>
    Pending,

    /// <summary>Node is queued for execution.</summary>
    Queued,

    /// <summary>Node is currently executing.</summary>
    Running,

    /// <summary>Node is waiting for input (e.g., gate approval).</summary>
    WaitingForInput,

    /// <summary>Node completed successfully.</summary>
    Completed,

    /// <summary>Node failed.</summary>
    Failed,

    /// <summary>Node was skipped.</summary>
    Skipped,

    /// <summary>Node was cancelled.</summary>
    Cancelled
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
    /// Execution state for each node.
    /// </summary>
    public List<NodeExecution> NodeExecutions { get; set; } = [];

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
/// Execution state for a single node within a workflow execution.
/// </summary>
public class NodeExecution
{
    /// <summary>
    /// The node ID from the workflow definition.
    /// </summary>
    public required string NodeId { get; set; }

    /// <summary>
    /// Current status of this node's execution.
    /// </summary>
    public NodeExecutionStatus Status { get; set; } = NodeExecutionStatus.Pending;

    /// <summary>
    /// Number of retry attempts made.
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// When execution of this node started.
    /// </summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// When execution of this node completed.
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Duration in milliseconds.
    /// </summary>
    public long? DurationMs { get; set; }

    /// <summary>
    /// Output data from this node's execution.
    /// </summary>
    public Dictionary<string, object>? Output { get; set; }

    /// <summary>
    /// Error message if node failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Logs/messages from node execution.
    /// </summary>
    public List<NodeExecutionLog> Logs { get; set; } = [];

    /// <summary>
    /// Associated session ID (for agent nodes).
    /// </summary>
    public string? SessionId { get; set; }
}

/// <summary>
/// Log entry from node execution.
/// </summary>
public class NodeExecutionLog
{
    /// <summary>
    /// Timestamp of the log entry.
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Log level.
    /// </summary>
    public NodeLogLevel Level { get; set; }

    /// <summary>
    /// Log message.
    /// </summary>
    public required string Message { get; set; }

    /// <summary>
    /// Additional data associated with the log entry.
    /// </summary>
    public Dictionary<string, object>? Data { get; set; }
}

/// <summary>
/// Log levels for node execution logs.
/// </summary>
public enum NodeLogLevel
{
    /// <summary>Debug information.</summary>
    Debug,

    /// <summary>Informational message.</summary>
    Info,

    /// <summary>Warning message.</summary>
    Warning,

    /// <summary>Error message.</summary>
    Error
}
