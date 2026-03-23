using Homespun.Shared.Models.Workflows;

namespace Homespun.Features.Workflows.Services;

/// <summary>
/// Executes server action steps synchronously on the server.
/// Dispatches to action-specific handlers based on the step configuration.
/// </summary>
public sealed class ServerActionStepExecutor : IStepExecutor
{
    private readonly ILogger<ServerActionStepExecutor> _logger;

    public ServerActionStepExecutor(ILogger<ServerActionStepExecutor> logger)
    {
        _logger = logger;
    }

    public WorkflowStepType StepType => WorkflowStepType.ServerAction;

    public Task<StepResult> ExecuteAsync(WorkflowStep step, WorkflowContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        _logger.LogInformation("Executing server action step '{StepId}'", step.Id);

        // Server actions complete synchronously.
        // Future phases will dispatch to action-specific handlers based on step.Config.
        return Task.FromResult(StepResult.Completed());
    }
}
