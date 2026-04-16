namespace Homespun.Features.ClaudeCode.Data;

/// <summary>
/// Base type for the minimal set of server-internal control-plane messages that the
/// Docker worker still surfaces alongside its A2A event stream.
///
/// <para>
/// Assistant/user/stream-event content has been retired — all content flows through
/// <see cref="Homespun.Features.ClaudeCode.Services.SessionEventIngestor"/> as
/// AG-UI envelopes. Only the four variants below (<see cref="SdkSystemMessage"/>,
/// <see cref="SdkResultMessage"/>, <see cref="SdkQuestionPendingMessage"/>,
/// <see cref="SdkPlanPendingMessage"/>) remain as orchestration primitives consumed by
/// <c>MessageProcessingService</c>.
/// </para>
/// </summary>
public abstract record SdkMessage(string Type, string SessionId);

/// <summary>
/// Result message indicating the session turn has completed.
/// </summary>
public record SdkResultMessage(
    string SessionId,
    string? Uuid,
    string? Subtype,
    int DurationMs,
    int DurationApiMs,
    bool IsError,
    int NumTurns,
    decimal TotalCostUsd,
    string? Result,
    List<string>? Errors = null
) : SdkMessage("result", SessionId);

/// <summary>
/// System lifecycle message (session_started, etc.).
/// </summary>
public record SdkSystemMessage(
    string SessionId,
    string? Uuid,
    string? Subtype,
    string? Model,
    List<string>? Tools
) : SdkMessage("system", SessionId);

/// <summary>
/// Control event emitted by the worker when <c>AskUserQuestion</c> is intercepted in canUseTool.
/// Carries the raw questions JSON so the server can parse and display them.
/// </summary>
public record SdkQuestionPendingMessage(
    string SessionId,
    string QuestionsJson
) : SdkMessage("question_pending", SessionId);

/// <summary>
/// Control event emitted by the worker when <c>ExitPlanMode</c> is intercepted in canUseTool.
/// Carries the plan content so the server can display it and wait for user approval.
/// </summary>
public record SdkPlanPendingMessage(
    string SessionId,
    string PlanJson
) : SdkMessage("plan_pending", SessionId);
