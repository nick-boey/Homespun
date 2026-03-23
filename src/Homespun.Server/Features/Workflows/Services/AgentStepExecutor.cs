using Homespun.Shared.Models.Workflows;

namespace Homespun.Features.Workflows.Services;

/// <summary>
/// Executes agent steps by resolving prompts and marking the step as waiting for an external callback.
/// The actual agent session is started externally; this executor signals that the step requires a callback.
/// </summary>
public sealed class AgentStepExecutor : IStepExecutor
{
    private readonly ILogger<AgentStepExecutor> _logger;

    public AgentStepExecutor(ILogger<AgentStepExecutor> logger)
    {
        _logger = logger;
    }

    public WorkflowStepType StepType => WorkflowStepType.Agent;

    public Task<StepResult> ExecuteAsync(WorkflowStep step, WorkflowContext context, CancellationToken ct)
    {
        // Resolve prompt: use inline prompt or prompt template reference
        var prompt = step.Prompt;
        if (!string.IsNullOrEmpty(prompt))
        {
            prompt = ContextInterpolation.Interpolate(prompt, context);
        }

        _logger.LogInformation(
            "Agent step '{StepId}' started with prompt template '{PromptId}', waiting for completion callback",
            step.Id,
            step.PromptId ?? "(inline)");

        // Agent steps require an external callback to complete
        return Task.FromResult(StepResult.WaitingForCallback());
    }
}
