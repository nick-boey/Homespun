using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Homespun.Shared.Models.Sessions;

namespace Homespun.Client.Services;

/// <summary>
/// Client-side state service for AG-UI events.
/// Maintains state for in-flight messages and tool calls, reconstructs
/// ClaudeMessage/ClaudeMessageContent objects, and emits events for UI consumption.
/// Supports real-time streaming for live UI updates.
/// </summary>
public class AGUIEventStateService : IDisposable
{
    /// <summary>
    /// State for a session's in-flight AG-UI events.
    /// </summary>
    private class SessionState
    {
        public ConcurrentDictionary<string, InFlightMessage> InFlightMessages { get; } = new();
        public ConcurrentDictionary<string, InFlightToolCall> InFlightToolCalls { get; } = new();
        /// <summary>Maps tool use ID to tool name for result correlation.</summary>
        public ConcurrentDictionary<string, string> ToolNames { get; } = new();
        /// <summary>Tracks the index for content blocks in the current message.</summary>
        public int ContentBlockIndex { get; set; } = 0;
    }

    /// <summary>
    /// Tracks an in-flight text message being assembled from AG-UI events.
    /// </summary>
    private class InFlightMessage
    {
        public string MessageId { get; init; } = "";
        public string Role { get; init; } = "assistant";
        public StringBuilder Content { get; } = new();
        public int ContentBlockIndex { get; set; } = -1;
    }

    /// <summary>
    /// Tracks an in-flight tool call being assembled from AG-UI events.
    /// </summary>
    private class InFlightToolCall
    {
        public string ToolCallId { get; init; } = "";
        public string ToolName { get; init; } = "";
        public string? ParentMessageId { get; init; }
        public StringBuilder Args { get; } = new();
        public int ContentBlockIndex { get; set; } = -1;
    }

    private readonly ConcurrentDictionary<string, SessionState> _sessions = new();

    #region Events

    /// <summary>
    /// Fired when a text message starts streaming.
    /// Parameters: sessionId, messageId, role
    /// </summary>
    public event Action<string, string, string>? OnTextMessageStarted;

    /// <summary>
    /// Fired when streaming text content is received.
    /// Parameters: sessionId, messageId, delta, contentBlockIndex
    /// </summary>
    public event Action<string, string, string, int>? OnTextMessageDelta;

    /// <summary>
    /// Fired when a message is completed (TextMessageEnd received).
    /// </summary>
    public event Action<string, ClaudeMessage>? OnMessageCompleted;

    /// <summary>
    /// Fired when a tool call starts streaming.
    /// Parameters: sessionId, toolCallId, toolName, contentBlockIndex
    /// </summary>
    public event Action<string, string, string, int>? OnToolCallStarted;

    /// <summary>
    /// Fired when streaming tool call arguments are received.
    /// Parameters: sessionId, toolCallId, delta, contentBlockIndex
    /// </summary>
    public event Action<string, string, string, int>? OnToolCallArgsDelta;

    /// <summary>
    /// Fired when a tool call is completed (ToolCallEnd received).
    /// </summary>
    public event Action<string, ClaudeMessageContent>? OnToolCallCompleted;

    /// <summary>
    /// Fired when a tool result is received.
    /// </summary>
    public event Action<string, ClaudeMessageContent>? OnToolResultReceived;

    /// <summary>
    /// Fired when an agent run starts.
    /// </summary>
    public event Action<string>? OnRunStarted;

    /// <summary>
    /// Fired when an agent run finishes successfully.
    /// </summary>
    public event Action<string>? OnRunFinished;

    /// <summary>
    /// Fired when an agent run encounters an error.
    /// </summary>
    public event Action<string, string>? OnRunError;

    /// <summary>
    /// Fired when a question is pending (custom event).
    /// </summary>
    public event Action<string, PendingQuestion>? OnQuestionPending;

    /// <summary>
    /// Fired when a plan is pending (custom event).
    /// </summary>
    public event Action<string, string, string?>? OnPlanPending;

    /// <summary>
    /// Fired when a question has been answered (custom event).
    /// </summary>
    public event Action<string>? OnQuestionAnswered;

    #endregion

    #region State Access Methods

    /// <summary>
    /// Checks if a message is currently in-flight.
    /// </summary>
    public bool HasInFlightMessage(string sessionId, string messageId)
    {
        if (_sessions.TryGetValue(sessionId, out var state))
        {
            return state.InFlightMessages.ContainsKey(messageId);
        }
        return false;
    }

    /// <summary>
    /// Gets the accumulated content for an in-flight message.
    /// </summary>
    public string? GetInFlightMessageContent(string sessionId, string messageId)
    {
        if (_sessions.TryGetValue(sessionId, out var state) &&
            state.InFlightMessages.TryGetValue(messageId, out var msg))
        {
            return msg.Content.ToString();
        }
        return null;
    }

    /// <summary>
    /// Checks if a tool call is currently in-flight.
    /// </summary>
    public bool HasInFlightToolCall(string sessionId, string toolCallId)
    {
        if (_sessions.TryGetValue(sessionId, out var state))
        {
            return state.InFlightToolCalls.ContainsKey(toolCallId);
        }
        return false;
    }

    /// <summary>
    /// Gets the accumulated args for an in-flight tool call.
    /// </summary>
    public string? GetInFlightToolCallArgs(string sessionId, string toolCallId)
    {
        if (_sessions.TryGetValue(sessionId, out var state) &&
            state.InFlightToolCalls.TryGetValue(toolCallId, out var toolCall))
        {
            return toolCall.Args.ToString();
        }
        return null;
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles a TextMessageStartEvent.
    /// </summary>
    public void HandleTextMessageStart(string sessionId, TextMessageStartEvent evt)
    {
        var state = GetOrCreateSessionState(sessionId);
        var index = state.ContentBlockIndex++;
        state.InFlightMessages[evt.MessageId] = new InFlightMessage
        {
            MessageId = evt.MessageId,
            Role = evt.Role,
            ContentBlockIndex = index
        };

        OnTextMessageStarted?.Invoke(sessionId, evt.MessageId, evt.Role);
    }

    /// <summary>
    /// Handles a TextMessageContentEvent.
    /// </summary>
    public void HandleTextMessageContent(string sessionId, TextMessageContentEvent evt)
    {
        if (_sessions.TryGetValue(sessionId, out var state) &&
            state.InFlightMessages.TryGetValue(evt.MessageId, out var msg))
        {
            msg.Content.Append(evt.Delta);
            OnTextMessageDelta?.Invoke(sessionId, evt.MessageId, evt.Delta, msg.ContentBlockIndex);
        }
    }

    /// <summary>
    /// Handles a TextMessageEndEvent.
    /// </summary>
    public void HandleTextMessageEnd(string sessionId, TextMessageEndEvent evt)
    {
        if (_sessions.TryGetValue(sessionId, out var state) &&
            state.InFlightMessages.TryRemove(evt.MessageId, out var msg))
        {
            var role = msg.Role == "assistant" ? ClaudeMessageRole.Assistant :
                       msg.Role == "user" ? ClaudeMessageRole.User :
                       ClaudeMessageRole.Assistant;

            var message = new ClaudeMessage
            {
                Id = msg.MessageId,
                SessionId = sessionId,
                Role = role,
                Content = new List<ClaudeMessageContent>
                {
                    new ClaudeMessageContent
                    {
                        Type = ClaudeContentType.Text,
                        Text = msg.Content.ToString(),
                        Index = msg.ContentBlockIndex
                    }
                }
            };

            OnMessageCompleted?.Invoke(sessionId, message);
        }
    }

    /// <summary>
    /// Handles a ToolCallStartEvent.
    /// </summary>
    public void HandleToolCallStart(string sessionId, ToolCallStartEvent evt)
    {
        var state = GetOrCreateSessionState(sessionId);
        var index = state.ContentBlockIndex++;
        state.InFlightToolCalls[evt.ToolCallId] = new InFlightToolCall
        {
            ToolCallId = evt.ToolCallId,
            ToolName = evt.ToolCallName,
            ParentMessageId = evt.ParentMessageId,
            ContentBlockIndex = index
        };

        // Store tool name for result correlation
        state.ToolNames[evt.ToolCallId] = evt.ToolCallName;

        OnToolCallStarted?.Invoke(sessionId, evt.ToolCallId, evt.ToolCallName, index);
    }

    /// <summary>
    /// Handles a ToolCallArgsEvent.
    /// </summary>
    public void HandleToolCallArgs(string sessionId, ToolCallArgsEvent evt)
    {
        if (_sessions.TryGetValue(sessionId, out var state) &&
            state.InFlightToolCalls.TryGetValue(evt.ToolCallId, out var toolCall))
        {
            toolCall.Args.Append(evt.Delta);
            OnToolCallArgsDelta?.Invoke(sessionId, evt.ToolCallId, evt.Delta, toolCall.ContentBlockIndex);
        }
    }

    /// <summary>
    /// Handles a ToolCallEndEvent.
    /// </summary>
    public void HandleToolCallEnd(string sessionId, ToolCallEndEvent evt)
    {
        if (_sessions.TryGetValue(sessionId, out var state) &&
            state.InFlightToolCalls.TryRemove(evt.ToolCallId, out var toolCall))
        {
            var content = new ClaudeMessageContent
            {
                Type = ClaudeContentType.ToolUse,
                ToolUseId = toolCall.ToolCallId,
                ToolName = toolCall.ToolName,
                ToolInput = toolCall.Args.ToString(),
                Index = toolCall.ContentBlockIndex
            };

            OnToolCallCompleted?.Invoke(sessionId, content);
        }
    }

    /// <summary>
    /// Handles a ToolCallResultEvent.
    /// </summary>
    public void HandleToolCallResult(string sessionId, ToolCallResultEvent evt)
    {
        string? toolName = null;
        if (_sessions.TryGetValue(sessionId, out var state))
        {
            state.ToolNames.TryGetValue(evt.ToolCallId, out toolName);
        }

        var content = new ClaudeMessageContent
        {
            Type = ClaudeContentType.ToolResult,
            ToolUseId = evt.ToolCallId,
            ToolResult = evt.Content,
            ToolName = toolName
        };

        OnToolResultReceived?.Invoke(sessionId, content);
    }

    /// <summary>
    /// Handles a RunStartedEvent.
    /// </summary>
    public void HandleRunStarted(string sessionId, RunStartedEvent evt)
    {
        // Ensure session state exists and reset content block index
        var state = GetOrCreateSessionState(sessionId);
        state.ContentBlockIndex = 0;
        OnRunStarted?.Invoke(sessionId);
    }

    /// <summary>
    /// Handles a RunFinishedEvent.
    /// </summary>
    public void HandleRunFinished(string sessionId, RunFinishedEvent evt)
    {
        // Clear in-flight state for the session
        ClearSessionState(sessionId);
        OnRunFinished?.Invoke(sessionId);
    }

    /// <summary>
    /// Handles a RunErrorEvent.
    /// </summary>
    public void HandleRunError(string sessionId, RunErrorEvent evt)
    {
        // Clear in-flight state for the session
        ClearSessionState(sessionId);
        OnRunError?.Invoke(sessionId, evt.Message);
    }

    /// <summary>
    /// Handles a CustomEvent.
    /// </summary>
    public void HandleCustomEvent(string sessionId, CustomEvent evt)
    {
        switch (evt.Name)
        {
            case AGUICustomEventName.QuestionPending:
                if (evt.Value is PendingQuestion question)
                {
                    OnQuestionPending?.Invoke(sessionId, question);
                }
                else if (evt.Value is JsonElement jsonElement)
                {
                    try
                    {
                        var parsedQuestion = JsonSerializer.Deserialize<PendingQuestion>(jsonElement.GetRawText());
                        if (parsedQuestion != null)
                        {
                            OnQuestionPending?.Invoke(sessionId, parsedQuestion);
                        }
                    }
                    catch
                    {
                        // Ignore deserialization errors
                    }
                }
                break;

            case AGUICustomEventName.PlanPending:
                if (evt.Value is AGUIPlanPendingData planData)
                {
                    OnPlanPending?.Invoke(sessionId, planData.PlanContent, planData.PlanFilePath);
                }
                else if (evt.Value is JsonElement planJsonElement)
                {
                    try
                    {
                        var parsedPlan = JsonSerializer.Deserialize<AGUIPlanPendingData>(planJsonElement.GetRawText());
                        if (parsedPlan != null)
                        {
                            OnPlanPending?.Invoke(sessionId, parsedPlan.PlanContent, parsedPlan.PlanFilePath);
                        }
                    }
                    catch
                    {
                        // Ignore deserialization errors
                    }
                }
                break;
        }
    }

    #endregion

    #region Helper Methods

    private SessionState GetOrCreateSessionState(string sessionId)
    {
        return _sessions.GetOrAdd(sessionId, _ => new SessionState());
    }

    private void ClearSessionState(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var state))
        {
            state.InFlightMessages.Clear();
            state.InFlightToolCalls.Clear();
        }
    }

    #endregion

    public void Dispose()
    {
        _sessions.Clear();
    }
}
