using Homespun.Shared.Models.Workflows;

namespace Homespun.Features.Workflows.Services;

/// <summary>
/// Executes gate steps by marking them as waiting for user input/approval.
/// Pauses execution until the approval API is called externally.
/// </summary>
public sealed class GateStepExecutor : IStepExecutor
{
    private readonly ILogger<GateStepExecutor> _logger;

    public GateStepExecutor(ILogger<GateStepExecutor> logger)
    {
        _logger = logger;
    }

    public WorkflowStepType StepType => WorkflowStepType.Gate;

    public Task<StepResult> ExecuteAsync(WorkflowStep step, WorkflowContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        _logger.LogInformation("Gate step '{StepId}' waiting for approval", step.Id);

        return Task.FromResult(StepResult.WaitingForInput());
    }
}
