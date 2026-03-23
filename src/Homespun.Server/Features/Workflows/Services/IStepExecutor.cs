using Homespun.Shared.Models.Workflows;

namespace Homespun.Features.Workflows.Services;

/// <summary>
/// Result of executing a workflow step.
/// </summary>
public class StepResult
{
    /// <summary>
    /// Whether the step executed successfully.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Output data from the step execution.
    /// </summary>
    public Dictionary<string, object>? Output { get; set; }

    /// <summary>
    /// Error message if the step failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Whether the step requires an external callback to complete (e.g., agent steps).
    /// </summary>
    public bool RequiresCallback { get; set; }

    /// <summary>
    /// Whether the step requires user input to proceed (e.g., gate steps).
    /// </summary>
    public bool RequiresInput { get; set; }

    public static StepResult Completed(Dictionary<string, object>? output = null) =>
        new() { Success = true, Output = output };

    public static StepResult Failed(string error) =>
        new() { Success = false, ErrorMessage = error };

    public static StepResult WaitingForCallback() =>
        new() { Success = true, RequiresCallback = true };

    public static StepResult WaitingForInput() =>
        new() { Success = true, RequiresInput = true };
}

/// <summary>
/// Interface for executing a specific type of workflow step.
/// </summary>
public interface IStepExecutor
{
    /// <summary>
    /// The step type this executor handles.
    /// </summary>
    WorkflowStepType StepType { get; }

    /// <summary>
    /// Executes the step and returns a result.
    /// </summary>
    Task<StepResult> ExecuteAsync(WorkflowStep step, WorkflowContext context, CancellationToken ct);
}
