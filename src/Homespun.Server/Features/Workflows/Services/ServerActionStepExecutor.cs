using System.Text.Json;
using Homespun.Shared.Models.Workflows;

namespace Homespun.Features.Workflows.Services;

/// <summary>
/// Executes server action steps by dispatching to action-specific handlers based on step configuration.
/// </summary>
public sealed class ServerActionStepExecutor : IStepExecutor
{
    private readonly Dictionary<string, IServerActionHandler> _handlers;
    private readonly ILogger<ServerActionStepExecutor> _logger;

    public ServerActionStepExecutor(
        IEnumerable<IServerActionHandler> handlers,
        ILogger<ServerActionStepExecutor> logger)
    {
        _handlers = handlers.ToDictionary(h => h.ActionType, StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    public WorkflowStepType StepType => WorkflowStepType.ServerAction;

    public async Task<StepResult> ExecuteAsync(WorkflowStep step, WorkflowContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        _logger.LogInformation("Executing server action step '{StepId}'", step.Id);

        var actionType = GetActionType(step.Config);

        if (actionType is null)
        {
            // No action type configured — complete immediately (backwards compatible)
            return StepResult.Completed();
        }

        if (!_handlers.TryGetValue(actionType, out var handler))
        {
            return StepResult.Failed($"Unknown server action type '{actionType}'. Available types: {string.Join(", ", _handlers.Keys)}.");
        }

        return await handler.ExecuteAsync(step, context, ct);
    }

    private static string? GetActionType(JsonElement? config)
    {
        if (config is null || config.Value.ValueKind != JsonValueKind.Object)
            return null;

        return config.Value.TryGetProperty("actionType", out var prop) && prop.ValueKind == JsonValueKind.String
            ? prop.GetString()
            : null;
    }
}
