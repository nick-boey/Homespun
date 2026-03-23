using System.Collections.Concurrent;

namespace Homespun.Features.Workflows.Services;

/// <summary>
/// Bridges agent session events to the workflow execution engine.
/// Tracks which sessions are running as part of workflow steps and routes
/// session lifecycle events (completion, failure, workflow_signal tool calls)
/// to the workflow execution service.
/// </summary>
public sealed class WorkflowSessionCallback : IWorkflowSessionCallback
{
    private readonly IWorkflowExecutionService _executionService;
    private readonly ILogger<WorkflowSessionCallback> _logger;

    private readonly ConcurrentDictionary<string, WorkflowSessionContext> _sessionContexts = new();

    public WorkflowSessionCallback(
        IWorkflowExecutionService executionService,
        ILogger<WorkflowSessionCallback> logger)
    {
        _executionService = executionService;
        _logger = logger;
    }

    /// <inheritdoc />
    public void RegisterSession(string sessionId, WorkflowSessionContext context)
    {
        _sessionContexts[sessionId] = context;

        _logger.LogInformation(
            "Registered session {SessionId} for workflow step {StepId} in execution {ExecutionId}",
            sessionId, context.StepId, context.ExecutionId);
    }

    /// <inheritdoc />
    public void UnregisterSession(string sessionId)
    {
        _sessionContexts.TryRemove(sessionId, out _);
    }

    /// <inheritdoc />
    public WorkflowSessionContext? GetSessionContext(string sessionId)
    {
        return _sessionContexts.GetValueOrDefault(sessionId);
    }

    /// <inheritdoc />
    public bool IsWorkflowSession(string sessionId)
    {
        return _sessionContexts.ContainsKey(sessionId);
    }

    /// <inheritdoc />
    public async Task HandleWorkflowSignalAsync(string sessionId, WorkflowSignalResult signal, CancellationToken ct = default)
    {
        if (!_sessionContexts.TryRemove(sessionId, out var context))
        {
            _logger.LogWarning(
                "Received workflow_signal for unregistered session {SessionId}", sessionId);
            return;
        }

        _logger.LogInformation(
            "Processing workflow_signal from session {SessionId}: status={Status}, step={StepId}, execution={ExecutionId}",
            sessionId, signal.Status, context.StepId, context.ExecutionId);

        if (signal.Status == "fail")
        {
            var errorMessage = signal.Message ?? "Step reported failure via workflow_signal";
            await _executionService.OnStepFailedAsync(
                context.ProjectPath, context.ExecutionId, context.StepId, errorMessage, ct);
        }
        else
        {
            var output = signal.Data ?? new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(signal.Message))
            {
                output["message"] = signal.Message;
            }

            await _executionService.OnStepCompletedAsync(
                context.ProjectPath, context.ExecutionId, context.StepId, output, ct);
        }
    }

    /// <inheritdoc />
    public async Task HandleSessionCompletedAsync(string sessionId, CancellationToken ct = default)
    {
        if (!_sessionContexts.TryRemove(sessionId, out var context))
        {
            return;
        }

        _logger.LogInformation(
            "Session {SessionId} completed normally, treating as implicit success for step {StepId}",
            sessionId, context.StepId);

        var output = new Dictionary<string, object>
        {
            ["message"] = "Session completed normally without explicit workflow_signal"
        };

        await _executionService.OnStepCompletedAsync(
            context.ProjectPath, context.ExecutionId, context.StepId, output, ct);
    }

    /// <inheritdoc />
    public async Task HandleSessionFailedAsync(string sessionId, string errorMessage, CancellationToken ct = default)
    {
        if (!_sessionContexts.TryRemove(sessionId, out var context))
        {
            return;
        }

        _logger.LogWarning(
            "Session {SessionId} failed, reporting step failure for step {StepId}: {Error}",
            sessionId, context.StepId, errorMessage);

        await _executionService.OnStepFailedAsync(
            context.ProjectPath, context.ExecutionId, context.StepId, errorMessage, ct);
    }
}
