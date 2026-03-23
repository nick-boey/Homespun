namespace Homespun.Features.Workflows.Services;

/// <summary>
/// Context for a session running within a workflow, identifying the execution and step.
/// </summary>
public class WorkflowSessionContext
{
    /// <summary>
    /// The workflow execution ID.
    /// </summary>
    public required string ExecutionId { get; init; }

    /// <summary>
    /// The step ID being executed.
    /// </summary>
    public required string StepId { get; init; }

    /// <summary>
    /// The workflow definition ID.
    /// </summary>
    public required string WorkflowId { get; init; }

    /// <summary>
    /// The project path for the workflow.
    /// </summary>
    public required string ProjectPath { get; init; }
}

/// <summary>
/// Result data from a workflow_signal tool call.
/// </summary>
public class WorkflowSignalResult
{
    /// <summary>
    /// Step outcome: "success" or "fail".
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Output data for workflow context.
    /// </summary>
    public Dictionary<string, object>? Data { get; init; }

    /// <summary>
    /// Summary of what was done.
    /// </summary>
    public string? Message { get; init; }
}

/// <summary>
/// Bridges agent session events to the workflow execution engine.
/// Manages the mapping between session IDs and workflow contexts,
/// and routes session lifecycle events to the appropriate workflow callbacks.
/// </summary>
public interface IWorkflowSessionCallback
{
    /// <summary>
    /// Registers a session as part of a workflow execution step.
    /// </summary>
    void RegisterSession(string sessionId, WorkflowSessionContext context);

    /// <summary>
    /// Unregisters a session from workflow tracking.
    /// </summary>
    void UnregisterSession(string sessionId);

    /// <summary>
    /// Gets the workflow context for a session, if it is running within a workflow.
    /// </summary>
    WorkflowSessionContext? GetSessionContext(string sessionId);

    /// <summary>
    /// Whether a session is running within a workflow context.
    /// </summary>
    bool IsWorkflowSession(string sessionId);

    /// <summary>
    /// Handles a workflow_signal tool call from an agent session.
    /// Parses the result and invokes the appropriate workflow execution callback.
    /// </summary>
    Task HandleWorkflowSignalAsync(string sessionId, WorkflowSignalResult signal, CancellationToken ct = default);

    /// <summary>
    /// Handles a session ending normally without an explicit workflow_signal call.
    /// Treats as implicit success.
    /// </summary>
    Task HandleSessionCompletedAsync(string sessionId, CancellationToken ct = default);

    /// <summary>
    /// Handles a session ending with an error or crash.
    /// Treats as step failure.
    /// </summary>
    Task HandleSessionFailedAsync(string sessionId, string errorMessage, CancellationToken ct = default);
}
