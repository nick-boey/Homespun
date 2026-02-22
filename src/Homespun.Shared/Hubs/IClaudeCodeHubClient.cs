using Homespun.Shared.Models.Sessions;

namespace Homespun.Shared.Hubs;

/// <summary>
/// Defines server-to-client SignalR messages for the Claude Code hub.
/// </summary>
public interface IClaudeCodeHubClient
{
    Task SessionStarted(ClaudeSession session);
    Task SessionStopped(string sessionId);
    Task SessionState(ClaudeSession session);
    Task MessageReceived(ClaudeMessage message);
    Task ContentBlockReceived(ClaudeMessageContent content);
    Task SessionStatusChanged(string sessionId, ClaudeSessionStatus status);

    /// <summary>
    /// Notifies clients when a session's mode or model changes.
    /// </summary>
    /// <param name="sessionId">The session ID</param>
    /// <param name="mode">The new session mode (Plan or Build)</param>
    /// <param name="model">The current model being used</param>
    Task SessionModeModelChanged(string sessionId, SessionMode mode, string model);

    Task SessionResultReceived(string sessionId, decimal totalCostUsd, long durationMs);
    Task StreamingContentStarted(ClaudeMessageContent content, int index);
    Task StreamingContentDelta(ClaudeMessageContent content, string delta, int index);
    Task StreamingContentStopped(ClaudeMessageContent content, int index);
    Task QuestionReceived(PendingQuestion question);
    Task QuestionAnswered();
    Task PlanReceived(string planContent, string? planFilePath);
    Task ContextCleared(string sessionId);

    /// <summary>
    /// Notifies clients when a session encounters an error.
    /// </summary>
    /// <param name="sessionId">The session ID</param>
    /// <param name="errorMessage">User-friendly error message</param>
    /// <param name="errorSubtype">SDK error subtype (e.g., error_max_turns, error_during_execution)</param>
    /// <param name="isRecoverable">Whether the session can be resumed by sending another message</param>
    Task SessionError(string sessionId, string errorMessage, string? errorSubtype, bool isRecoverable);

    #region AG-UI Events

    /// <summary>
    /// Notifies clients when an agent run starts.
    /// </summary>
    Task AGUIRunStarted(RunStartedEvent evt);

    /// <summary>
    /// Notifies clients when an agent run finishes successfully.
    /// </summary>
    Task AGUIRunFinished(RunFinishedEvent evt);

    /// <summary>
    /// Notifies clients when an agent run encounters an error.
    /// </summary>
    Task AGUIRunError(RunErrorEvent evt);

    /// <summary>
    /// Notifies clients when a text message starts streaming.
    /// </summary>
    Task AGUITextMessageStart(TextMessageStartEvent evt);

    /// <summary>
    /// Notifies clients with streaming text content.
    /// </summary>
    Task AGUITextMessageContent(TextMessageContentEvent evt);

    /// <summary>
    /// Notifies clients when a text message finishes streaming.
    /// </summary>
    Task AGUITextMessageEnd(TextMessageEndEvent evt);

    /// <summary>
    /// Notifies clients when a tool call starts.
    /// </summary>
    Task AGUIToolCallStart(ToolCallStartEvent evt);

    /// <summary>
    /// Notifies clients with streaming tool call arguments.
    /// </summary>
    Task AGUIToolCallArgs(ToolCallArgsEvent evt);

    /// <summary>
    /// Notifies clients when a tool call finishes.
    /// </summary>
    Task AGUIToolCallEnd(ToolCallEndEvent evt);

    /// <summary>
    /// Notifies clients when a tool call result is available.
    /// </summary>
    Task AGUIToolCallResult(ToolCallResultEvent evt);

    /// <summary>
    /// Sends a full state snapshot to clients.
    /// </summary>
    Task AGUIStateSnapshot(StateSnapshotEvent evt);

    /// <summary>
    /// Sends an incremental state delta to clients.
    /// </summary>
    Task AGUIStateDelta(StateDeltaEvent evt);

    /// <summary>
    /// Sends a custom AG-UI event to clients.
    /// </summary>
    Task AGUICustomEvent(CustomEvent evt);

    #endregion
}
