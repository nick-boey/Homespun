using Homespun.Features.ClaudeCode.Data;
using Homespun.Shared.Models.Sessions;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Handles question/answer flow, plan management (capture, approval, execution),
/// workflow signals, and ExitPlanMode processing.
/// </summary>
public interface IToolInteractionService
{
    Task AnswerQuestionAsync(string sessionId, Dictionary<string, string> answers, CancellationToken cancellationToken = default);

    Task ExecutePlanAsync(string sessionId, bool clearContext = true, CancellationToken cancellationToken = default);

    Task ApprovePlanAsync(string sessionId, bool approved, bool keepContext, string? feedback = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles a <c>workflow_signal</c> tool call emitted by the agent. <paramref name="toolInputJson"/>
    /// is the raw JSON the agent passed to the tool (contains <c>status</c>, optional <c>message</c>,
    /// optional <c>data</c>).
    /// </summary>
    Task HandleWorkflowSignalToolAsync(string sessionId, string? toolInputJson, CancellationToken cancellationToken);

    /// <summary>
    /// Fallback handler for <c>AskUserQuestion</c> tool calls that reach the server without
    /// being intercepted by the worker's <c>canUseTool</c>. The worker's interception path is
    /// the default; this handler exists for older workers or degraded modes.
    /// </summary>
    Task HandleAskUserQuestionTool(string sessionId, ClaudeSession session, string toolUseId, string? toolInputJson, CancellationToken cancellationToken);

    Task HandleQuestionPendingFromWorkerAsync(string sessionId, ClaudeSession session, SdkQuestionPendingMessage questionMsg, Guid turnId, CancellationToken cancellationToken);

    Task HandlePlanPendingFromWorkerAsync(string sessionId, ClaudeSession session, SdkPlanPendingMessage planMsg, Guid turnId, CancellationToken cancellationToken);

    /// <summary>
    /// Fallback handler for <c>ExitPlanMode</c> tool calls that reach the server without
    /// being intercepted by the worker's <c>canUseTool</c>.
    /// </summary>
    Task HandleExitPlanModeCompletedAsync(string sessionId, ClaudeSession session, string? toolInputJson, CancellationToken cancellationToken);

    /// <summary>
    /// Inspects a <c>Write</c> tool call's input and, if it looks like a plan file being
    /// written (path under <c>/plans/</c> or <c>/.claude/</c> with a <c>plan.md</c>-ish name),
    /// captures the plan content on the session so a later <c>plan_pending</c> event can use it.
    /// </summary>
    void TryCaptureWrittenPlanContent(ClaudeSession session, string? toolInputJson);
}
