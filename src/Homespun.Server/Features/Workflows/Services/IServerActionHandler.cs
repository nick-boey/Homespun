using Homespun.Shared.Models.Workflows;

namespace Homespun.Features.Workflows.Services;

/// <summary>
/// Interface for handling a specific server action type.
/// Implementations are dispatched by <see cref="ServerActionStepExecutor"/> based on the action type in step config.
/// </summary>
public interface IServerActionHandler
{
    /// <summary>
    /// The action type this handler processes (e.g., "ci_merge").
    /// </summary>
    string ActionType { get; }

    /// <summary>
    /// Executes the server action and returns a result.
    /// </summary>
    Task<StepResult> ExecuteAsync(WorkflowStep step, WorkflowContext context, CancellationToken ct);
}
