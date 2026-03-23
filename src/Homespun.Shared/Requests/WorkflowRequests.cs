using Homespun.Shared.Models.Workflows;

namespace Homespun.Shared.Requests;

/// <summary>
/// Request model for creating a new workflow.
/// </summary>
public class CreateWorkflowRequest
{
    /// <summary>
    /// The project ID.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// Workflow title.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Workflow description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Initial steps for the workflow.
    /// </summary>
    public List<WorkflowStep>? Steps { get; set; }

    /// <summary>
    /// Trigger configuration.
    /// </summary>
    public WorkflowTrigger? Trigger { get; set; }

    /// <summary>
    /// Workflow settings.
    /// </summary>
    public WorkflowSettings? Settings { get; set; }

    /// <summary>
    /// Whether the workflow should be enabled immediately.
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Request model for updating an existing workflow.
/// </summary>
public class UpdateWorkflowRequest
{
    /// <summary>
    /// The project ID.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// Updated title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Updated description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Updated steps.
    /// </summary>
    public List<WorkflowStep>? Steps { get; set; }

    /// <summary>
    /// Updated trigger configuration.
    /// </summary>
    public WorkflowTrigger? Trigger { get; set; }

    /// <summary>
    /// Updated settings.
    /// </summary>
    public WorkflowSettings? Settings { get; set; }

    /// <summary>
    /// Whether the workflow is enabled.
    /// </summary>
    public bool? Enabled { get; set; }
}

/// <summary>
/// Request model for executing a workflow.
/// </summary>
public class ExecuteWorkflowRequest
{
    /// <summary>
    /// The project ID.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// Input data for the workflow execution.
    /// </summary>
    public Dictionary<string, object>? Input { get; set; }

    /// <summary>
    /// Environment variables for the execution.
    /// </summary>
    public Dictionary<string, string>? Environment { get; set; }

    /// <summary>
    /// Whether to execute in dry-run mode (validate without executing).
    /// </summary>
    public bool DryRun { get; set; } = false;
}

/// <summary>
/// Request model for cancelling a workflow execution.
/// </summary>
public class CancelWorkflowExecutionRequest
{
    /// <summary>
    /// The project ID.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// Reason for cancellation.
    /// </summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Request model for retrying a failed workflow execution.
/// </summary>
public class RetryWorkflowExecutionRequest
{
    /// <summary>
    /// The project ID.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// Whether to retry from the failed step or from the beginning.
    /// </summary>
    public bool FromFailedStep { get; set; } = true;

    /// <summary>
    /// Specific step ID to retry from (overrides FromFailedStep).
    /// </summary>
    public string? FromStepId { get; set; }
}

/// <summary>
/// Request model for signaling step completion or failure from a session.
/// </summary>
public class WorkflowStepSignalRequest
{
    /// <summary>
    /// The project ID.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// Step outcome: "success" or "fail".
    /// </summary>
    public required string Status { get; set; }

    /// <summary>
    /// Output data for workflow context.
    /// </summary>
    public Dictionary<string, object>? Data { get; set; }

    /// <summary>
    /// Summary of what was done.
    /// </summary>
    public string? Message { get; set; }
}

/// <summary>
/// Request model for approving a gate in a workflow execution.
/// </summary>
public class ApproveGateRequest
{
    /// <summary>
    /// The project ID.
    /// </summary>
    public required string ProjectId { get; set; }

    /// <summary>
    /// The selected approval option value.
    /// </summary>
    public required string Decision { get; set; }

    /// <summary>
    /// Optional comment with the approval.
    /// </summary>
    public string? Comment { get; set; }
}

/// <summary>
/// Response model for workflow execution.
/// </summary>
public class WorkflowExecutionResponse
{
    /// <summary>
    /// The execution ID.
    /// </summary>
    public required string ExecutionId { get; set; }

    /// <summary>
    /// The workflow ID.
    /// </summary>
    public required string WorkflowId { get; set; }

    /// <summary>
    /// Current execution status.
    /// </summary>
    public WorkflowExecutionStatus Status { get; set; }

    /// <summary>
    /// Message about the execution.
    /// </summary>
    public string? Message { get; set; }
}

/// <summary>
/// Response model for workflow list operations.
/// </summary>
public class WorkflowListResponse
{
    /// <summary>
    /// The workflows.
    /// </summary>
    public List<WorkflowSummary> Workflows { get; set; } = [];

    /// <summary>
    /// Total count (for pagination).
    /// </summary>
    public int TotalCount { get; set; }
}

/// <summary>
/// Summary information for a workflow.
/// </summary>
public class WorkflowSummary
{
    /// <summary>
    /// Workflow ID.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Workflow title.
    /// </summary>
    public required string Title { get; set; }

    /// <summary>
    /// Workflow description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether the workflow is enabled.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Trigger type.
    /// </summary>
    public WorkflowTriggerType? TriggerType { get; set; }

    /// <summary>
    /// Number of steps in the workflow.
    /// </summary>
    public int StepCount { get; set; }

    /// <summary>
    /// Current version.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// When the workflow was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Last execution status.
    /// </summary>
    public WorkflowExecutionStatus? LastExecutionStatus { get; set; }

    /// <summary>
    /// Last execution time.
    /// </summary>
    public DateTime? LastExecutedAt { get; set; }
}

/// <summary>
/// Response model for execution list operations.
/// </summary>
public class ExecutionListResponse
{
    /// <summary>
    /// The executions.
    /// </summary>
    public List<ExecutionSummary> Executions { get; set; } = [];

    /// <summary>
    /// Total count (for pagination).
    /// </summary>
    public int TotalCount { get; set; }
}

/// <summary>
/// Summary information for a workflow execution.
/// </summary>
public class ExecutionSummary
{
    /// <summary>
    /// Execution ID.
    /// </summary>
    public required string Id { get; set; }

    /// <summary>
    /// Workflow ID.
    /// </summary>
    public required string WorkflowId { get; set; }

    /// <summary>
    /// Workflow title.
    /// </summary>
    public required string WorkflowTitle { get; set; }

    /// <summary>
    /// Current status.
    /// </summary>
    public WorkflowExecutionStatus Status { get; set; }

    /// <summary>
    /// Trigger type that started this execution.
    /// </summary>
    public WorkflowTriggerType TriggerType { get; set; }

    /// <summary>
    /// When the execution was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the execution started.
    /// </summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// When the execution completed.
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// Duration in milliseconds.
    /// </summary>
    public long? DurationMs { get; set; }

    /// <summary>
    /// User who triggered the execution.
    /// </summary>
    public string? TriggeredBy { get; set; }

    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? ErrorMessage { get; set; }
}
