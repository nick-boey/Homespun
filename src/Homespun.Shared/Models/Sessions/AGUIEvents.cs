using System.Text.Json.Serialization;

namespace Homespun.Shared.Models.Sessions;

/// <summary>
/// AG-UI event type constants for SignalR method naming.
/// These are prefixed with "AGUI_" to distinguish from legacy events.
/// </summary>
public static class AGUIEventType
{
    // Lifecycle events
    public const string RunStarted = "AGUI_RunStarted";
    public const string RunFinished = "AGUI_RunFinished";
    public const string RunError = "AGUI_RunError";

    // Text message events
    public const string TextMessageStart = "AGUI_TextMessageStart";
    public const string TextMessageContent = "AGUI_TextMessageContent";
    public const string TextMessageEnd = "AGUI_TextMessageEnd";

    // Tool call events
    public const string ToolCallStart = "AGUI_ToolCallStart";
    public const string ToolCallArgs = "AGUI_ToolCallArgs";
    public const string ToolCallEnd = "AGUI_ToolCallEnd";
    public const string ToolCallResult = "AGUI_ToolCallResult";

    // State events
    public const string StateSnapshot = "AGUI_StateSnapshot";
    public const string StateDelta = "AGUI_StateDelta";

    // Custom events
    public const string Custom = "AGUI_Custom";

    /// <summary>
    /// Single SignalR method name used for all AG-UI envelopes broadcast to clients. Every
    /// canonical AG-UI event and every Homespun <c>Custom</c> variant flows through this one
    /// method wrapped in <see cref="SessionEventEnvelope"/>.
    /// </summary>
    public const string ReceiveSessionEvent = "ReceiveSessionEvent";
}

/// <summary>
/// Custom AG-UI event name catalog for Homespun-specific concerns that don't fit the
/// canonical AG-UI events. Every name is Homespun-namespaced (lowercase, dot-separated) so
/// that any future non-Homespun AG-UI consumer can safely ignore unrecognized names while
/// remaining spec-conformant.
///
/// <para>
/// See <c>docs/session-events.md</c> for the payload shape and semantic meaning of each
/// custom event.
/// </para>
/// </summary>
public static class AGUICustomEventName
{
    /// <summary>
    /// Agent-side reasoning block emitted before a text or tool-use block.
    /// Payload: <c>{ text: string, parentMessageId?: string }</c>.
    /// </summary>
    public const string Thinking = "thinking";

    /// <summary>
    /// A hook has begun executing (SessionStart, PreToolUse, PostToolUse, etc.).
    /// Payload: <c>{ hookId: string, hookName: string, hookEvent: string }</c>.
    /// </summary>
    public const string HookStarted = "hook.started";

    /// <summary>
    /// A hook has finished executing.
    /// Payload: <c>{ hookId: string, hookName: string, output: string?, exitCode: int?, outcome: string }</c>.
    /// </summary>
    public const string HookResponse = "hook.response";

    /// <summary>
    /// Worker-emitted system init event describing the session's model, tools, and permission mode.
    /// Payload: <c>{ model: string?, tools: string[]?, permissionMode: string? }</c>.
    /// </summary>
    public const string SystemInit = "system.init";

    /// <summary>
    /// Claude is asking the user a question (SDK input-required, inputType=question).
    /// Payload: <c>PendingQuestion</c>.
    /// </summary>
    public const string QuestionPending = "question.pending";

    /// <summary>
    /// Claude is presenting a plan for approval (SDK input-required, inputType=plan-approval).
    /// Payload: <c>AGUIPlanPendingData</c>.
    /// </summary>
    public const string PlanPending = "plan.pending";

    /// <summary>
    /// Marks that the session has resumed from a paused/input-required state.
    /// Payload: <c>{}</c>.
    /// </summary>
    public const string StatusResumed = "status.resumed";

    /// <summary>
    /// A higher-level workflow (issue-agent modification, rebase, etc.) completed.
    /// Payload: <c>{ status: string, outputs?: object, artifacts?: object[] }</c>.
    /// </summary>
    public const string WorkflowComplete = "workflow.complete";

    /// <summary>
    /// Echo of a user-submitted message so other tabs see it. Emitted by the server when a
    /// user message is accepted from the hub.
    /// Payload: <c>{ text: string }</c>.
    /// </summary>
    public const string UserMessage = "user.message";

    /// <summary>
    /// Fallback for A2A event variants the translator does not recognize. Payload preserves
    /// the original A2A event so clients can render or log it without loss.
    /// Payload: <c>{ original: object }</c>.
    /// </summary>
    public const string Raw = "raw";

    /// <summary>
    /// Emitted when a session's context is cleared (separate from the A2A translation path —
    /// this is a Homespun-internal lifecycle event).
    /// Payload: <c>{ sessionId: string }</c>.
    /// </summary>
    public const string ContextCleared = "context.cleared";
}

#region AG-UI Event Types
// These event types match the AG-UI protocol specification.
// See: https://docs.ag-ui.com/concepts/events

/// <summary>
/// Base class for all AG-UI events. Serves as the discriminated union type embedded in
/// <see cref="SessionEventEnvelope.Event"/>.
///
/// <para>
/// The <see cref="JsonPolymorphicAttribute"/> enables correct polymorphic serialization
/// when a concrete event is assigned to an <see cref="AGUIBaseEvent"/>-typed field (as in
/// the envelope and the replay endpoint response). The discriminator is the <c>type</c>
/// property, which matches each concrete type's <see cref="Type"/> override — so the
/// serialized payload shape is identical whether the runtime or declared type drives
/// serialization.
/// </para>
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type", UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor)]
[JsonDerivedType(typeof(RunStartedEvent), typeDiscriminator: "RUN_STARTED")]
[JsonDerivedType(typeof(RunFinishedEvent), typeDiscriminator: "RUN_FINISHED")]
[JsonDerivedType(typeof(RunErrorEvent), typeDiscriminator: "RUN_ERROR")]
[JsonDerivedType(typeof(TextMessageStartEvent), typeDiscriminator: "TEXT_MESSAGE_START")]
[JsonDerivedType(typeof(TextMessageContentEvent), typeDiscriminator: "TEXT_MESSAGE_CONTENT")]
[JsonDerivedType(typeof(TextMessageEndEvent), typeDiscriminator: "TEXT_MESSAGE_END")]
[JsonDerivedType(typeof(ToolCallStartEvent), typeDiscriminator: "TOOL_CALL_START")]
[JsonDerivedType(typeof(ToolCallArgsEvent), typeDiscriminator: "TOOL_CALL_ARGS")]
[JsonDerivedType(typeof(ToolCallEndEvent), typeDiscriminator: "TOOL_CALL_END")]
[JsonDerivedType(typeof(ToolCallResultEvent), typeDiscriminator: "TOOL_CALL_RESULT")]
[JsonDerivedType(typeof(StateSnapshotEvent), typeDiscriminator: "STATE_SNAPSHOT")]
[JsonDerivedType(typeof(StateDeltaEvent), typeDiscriminator: "STATE_DELTA")]
[JsonDerivedType(typeof(CustomEvent), typeDiscriminator: "CUSTOM")]
public abstract record AGUIBaseEvent
{
    /// <summary>
    /// The event type discriminator as a runtime-accessible string. Each concrete subtype
    /// overrides this with the canonical AG-UI spec value; the same value appears in the
    /// emitted JSON via the <see cref="JsonPolymorphicAttribute"/> discriminator. Marked
    /// <see cref="JsonIgnoreAttribute"/> so the JSON contains exactly one <c>type</c> field
    /// (supplied by the polymorphic discriminator, not this property).
    /// </summary>
    [JsonIgnore]
    public abstract string Type { get; }

    /// <summary>
    /// Unix timestamp in milliseconds the event was created on the server.
    /// </summary>
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
}

/// <summary>
/// Emitted when a new agent run starts.
/// </summary>
public record RunStartedEvent : AGUIBaseEvent
{
    [JsonIgnore]
    public override string Type => "RUN_STARTED";

    /// <summary>
    /// The thread/session ID.
    /// </summary>
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    /// <summary>
    /// Unique identifier for this run.
    /// </summary>
    [JsonPropertyName("runId")]
    public required string RunId { get; init; }
}

/// <summary>
/// Emitted when an agent run completes successfully.
/// </summary>
public record RunFinishedEvent : AGUIBaseEvent
{
    [JsonIgnore]
    public override string Type => "RUN_FINISHED";

    /// <summary>
    /// The thread/session ID.
    /// </summary>
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; init; }

    /// <summary>
    /// The run ID that finished.
    /// </summary>
    [JsonPropertyName("runId")]
    public required string RunId { get; init; }

    /// <summary>
    /// Optional result data.
    /// </summary>
    [JsonPropertyName("result")]
    public object? Result { get; init; }
}

/// <summary>
/// Emitted when an agent run encounters an error.
/// </summary>
public record RunErrorEvent : AGUIBaseEvent
{
    [JsonIgnore]
    public override string Type => "RUN_ERROR";

    /// <summary>
    /// Error message.
    /// </summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>
    /// Optional error code.
    /// </summary>
    [JsonPropertyName("code")]
    public string? Code { get; init; }
}

/// <summary>
/// Emitted when a new text message starts streaming.
/// </summary>
public record TextMessageStartEvent : AGUIBaseEvent
{
    [JsonIgnore]
    public override string Type => "TEXT_MESSAGE_START";

    /// <summary>
    /// Unique message identifier.
    /// </summary>
    [JsonPropertyName("messageId")]
    public required string MessageId { get; init; }

    /// <summary>
    /// The role of the message sender (assistant, user, tool).
    /// </summary>
    [JsonPropertyName("role")]
    public required string Role { get; init; }
}

/// <summary>
/// Emitted for streaming text content within a message.
/// </summary>
public record TextMessageContentEvent : AGUIBaseEvent
{
    [JsonIgnore]
    public override string Type => "TEXT_MESSAGE_CONTENT";

    /// <summary>
    /// The message this content belongs to.
    /// </summary>
    [JsonPropertyName("messageId")]
    public required string MessageId { get; init; }

    /// <summary>
    /// The incremental text content.
    /// </summary>
    [JsonPropertyName("delta")]
    public required string Delta { get; init; }
}

/// <summary>
/// Emitted when a text message finishes streaming.
/// </summary>
public record TextMessageEndEvent : AGUIBaseEvent
{
    [JsonIgnore]
    public override string Type => "TEXT_MESSAGE_END";

    /// <summary>
    /// The message that finished.
    /// </summary>
    [JsonPropertyName("messageId")]
    public required string MessageId { get; init; }
}

/// <summary>
/// Emitted when a tool call starts.
/// </summary>
public record ToolCallStartEvent : AGUIBaseEvent
{
    [JsonIgnore]
    public override string Type => "TOOL_CALL_START";

    /// <summary>
    /// Unique identifier for this tool call.
    /// </summary>
    [JsonPropertyName("toolCallId")]
    public required string ToolCallId { get; init; }

    /// <summary>
    /// The name of the tool being called.
    /// </summary>
    [JsonPropertyName("toolCallName")]
    public required string ToolCallName { get; init; }

    /// <summary>
    /// The message ID that contains this tool call.
    /// </summary>
    [JsonPropertyName("parentMessageId")]
    public string? ParentMessageId { get; init; }
}

/// <summary>
/// Emitted for streaming tool call arguments.
/// </summary>
public record ToolCallArgsEvent : AGUIBaseEvent
{
    [JsonIgnore]
    public override string Type => "TOOL_CALL_ARGS";

    /// <summary>
    /// The tool call this belongs to.
    /// </summary>
    [JsonPropertyName("toolCallId")]
    public required string ToolCallId { get; init; }

    /// <summary>
    /// Incremental argument content (typically JSON).
    /// </summary>
    [JsonPropertyName("delta")]
    public required string Delta { get; init; }
}

/// <summary>
/// Emitted when a tool call finishes (before result is available).
/// </summary>
public record ToolCallEndEvent : AGUIBaseEvent
{
    [JsonIgnore]
    public override string Type => "TOOL_CALL_END";

    /// <summary>
    /// The tool call that finished.
    /// </summary>
    [JsonPropertyName("toolCallId")]
    public required string ToolCallId { get; init; }
}

/// <summary>
/// Emitted when a tool call result is available.
/// </summary>
public record ToolCallResultEvent : AGUIBaseEvent
{
    [JsonIgnore]
    public override string Type => "TOOL_CALL_RESULT";

    /// <summary>
    /// The tool call this result belongs to.
    /// </summary>
    [JsonPropertyName("toolCallId")]
    public required string ToolCallId { get; init; }

    /// <summary>
    /// The result content.
    /// </summary>
    [JsonPropertyName("content")]
    public required string Content { get; init; }

    /// <summary>
    /// Optional message ID for the result.
    /// </summary>
    [JsonPropertyName("messageId")]
    public string? MessageId { get; init; }

    /// <summary>
    /// The role (typically "tool").
    /// </summary>
    [JsonPropertyName("role")]
    public string Role { get; init; } = "tool";
}

/// <summary>
/// Emitted for state snapshot events.
/// </summary>
public record StateSnapshotEvent : AGUIBaseEvent
{
    [JsonIgnore]
    public override string Type => "STATE_SNAPSHOT";

    /// <summary>
    /// The full state snapshot.
    /// </summary>
    [JsonPropertyName("snapshot")]
    public required object Snapshot { get; init; }
}

/// <summary>
/// Emitted for incremental state updates.
/// </summary>
public record StateDeltaEvent : AGUIBaseEvent
{
    [JsonIgnore]
    public override string Type => "STATE_DELTA";

    /// <summary>
    /// The state delta.
    /// </summary>
    [JsonPropertyName("delta")]
    public required object Delta { get; init; }
}

/// <summary>
/// Custom event for application-specific events.
/// </summary>
public record CustomEvent : AGUIBaseEvent
{
    [JsonIgnore]
    public override string Type => "CUSTOM";

    /// <summary>
    /// The custom event name.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// The custom event payload.
    /// </summary>
    [JsonPropertyName("value")]
    public required object Value { get; init; }
}

#endregion

#region Factory Methods

/// <summary>
/// Factory methods for creating AG-UI events from session data.
/// </summary>
public static class AGUIEventFactory
{
    /// <summary>
    /// Creates a RunStartedEvent for a session.
    /// </summary>
    public static RunStartedEvent CreateRunStarted(string sessionId, string? runId = null)
    {
        return new RunStartedEvent
        {
            ThreadId = sessionId,
            RunId = runId ?? Guid.NewGuid().ToString()
        };
    }

    /// <summary>
    /// Creates a RunFinishedEvent for a session.
    /// </summary>
    public static RunFinishedEvent CreateRunFinished(string sessionId, string runId, object? result = null)
    {
        return new RunFinishedEvent
        {
            ThreadId = sessionId,
            RunId = runId,
            Result = result
        };
    }

    /// <summary>
    /// Creates a RunErrorEvent for a session.
    /// </summary>
    public static RunErrorEvent CreateRunError(string message, string? code = null)
    {
        return new RunErrorEvent
        {
            Message = message,
            Code = code
        };
    }

    /// <summary>
    /// Creates a TextMessageStartEvent.
    /// </summary>
    public static TextMessageStartEvent CreateTextMessageStart(string messageId, string role = "assistant")
    {
        return new TextMessageStartEvent
        {
            MessageId = messageId,
            Role = role
        };
    }

    /// <summary>
    /// Creates a TextMessageContentEvent.
    /// </summary>
    public static TextMessageContentEvent CreateTextMessageContent(string messageId, string delta)
    {
        return new TextMessageContentEvent
        {
            MessageId = messageId,
            Delta = delta
        };
    }

    /// <summary>
    /// Creates a TextMessageEndEvent.
    /// </summary>
    public static TextMessageEndEvent CreateTextMessageEnd(string messageId)
    {
        return new TextMessageEndEvent
        {
            MessageId = messageId
        };
    }

    /// <summary>
    /// Creates a ToolCallStartEvent.
    /// </summary>
    public static ToolCallStartEvent CreateToolCallStart(string toolCallId, string toolCallName, string? parentMessageId = null)
    {
        return new ToolCallStartEvent
        {
            ToolCallId = toolCallId,
            ToolCallName = toolCallName,
            ParentMessageId = parentMessageId
        };
    }

    /// <summary>
    /// Creates a ToolCallArgsEvent.
    /// </summary>
    public static ToolCallArgsEvent CreateToolCallArgs(string toolCallId, string delta)
    {
        return new ToolCallArgsEvent
        {
            ToolCallId = toolCallId,
            Delta = delta
        };
    }

    /// <summary>
    /// Creates a ToolCallEndEvent.
    /// </summary>
    public static ToolCallEndEvent CreateToolCallEnd(string toolCallId)
    {
        return new ToolCallEndEvent
        {
            ToolCallId = toolCallId
        };
    }

    /// <summary>
    /// Creates a ToolCallResultEvent.
    /// </summary>
    public static ToolCallResultEvent CreateToolCallResult(string toolCallId, string content, string? messageId = null)
    {
        return new ToolCallResultEvent
        {
            ToolCallId = toolCallId,
            Content = content,
            MessageId = messageId
        };
    }

    /// <summary>
    /// Creates a CustomEvent.
    /// </summary>
    public static CustomEvent CreateCustomEvent(string name, object value)
    {
        return new CustomEvent
        {
            Name = name,
            Value = value
        };
    }

    /// <summary>
    /// Creates a QuestionPending custom event.
    /// </summary>
    public static CustomEvent CreateQuestionPending(PendingQuestion question)
    {
        return CreateCustomEvent(AGUICustomEventName.QuestionPending, question);
    }

    /// <summary>
    /// Creates a PlanPending custom event.
    /// </summary>
    public static CustomEvent CreatePlanPending(string planContent, string? planFilePath)
    {
        return CreateCustomEvent(AGUICustomEventName.PlanPending, new AGUIPlanPendingData
        {
            PlanContent = planContent,
            PlanFilePath = planFilePath
        });
    }
}

#endregion

#region Custom Event Data Types

/// <summary>
/// Plan pending event data for AG-UI custom events.
/// </summary>
public record AGUIPlanPendingData
{
    /// <summary>
    /// The plan content in markdown format.
    /// </summary>
    [JsonPropertyName("planContent")]
    public required string PlanContent { get; init; }

    /// <summary>
    /// The path to the plan file, if available.
    /// </summary>
    [JsonPropertyName("planFilePath")]
    public string? PlanFilePath { get; init; }
}

#endregion
