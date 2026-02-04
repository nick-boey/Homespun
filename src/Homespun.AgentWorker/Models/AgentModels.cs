using System.Text.Json.Serialization;

namespace Homespun.AgentWorker.Models;

/// <summary>
/// Request to start a new agent session.
/// </summary>
public class StartSessionRequest
{
    /// <summary>
    /// Working directory for the agent.
    /// </summary>
    public required string WorkingDirectory { get; set; }

    /// <summary>
    /// Session mode (Plan or Build).
    /// </summary>
    public required string Mode { get; set; }

    /// <summary>
    /// Claude model to use.
    /// </summary>
    public required string Model { get; set; }

    /// <summary>
    /// Initial prompt/message to send.
    /// </summary>
    public required string Prompt { get; set; }

    /// <summary>
    /// Optional system prompt.
    /// </summary>
    public string? SystemPrompt { get; set; }

    /// <summary>
    /// Optional session ID to resume from.
    /// </summary>
    public string? ResumeSessionId { get; set; }
}

/// <summary>
/// Request to send a message to an existing session.
/// </summary>
public class SendMessageRequest
{
    /// <summary>
    /// The message to send.
    /// </summary>
    public required string Message { get; set; }

    /// <summary>
    /// Optional model override for this message.
    /// </summary>
    public string? Model { get; set; }
}

/// <summary>
/// Request to answer a pending question.
/// </summary>
public class AnswerQuestionRequest
{
    /// <summary>
    /// Dictionary mapping question text to answer text.
    /// </summary>
    public required Dictionary<string, string> Answers { get; set; }
}

/// <summary>
/// Types of SSE events emitted by the worker.
/// </summary>
public static class SseEventTypes
{
    public const string SessionStarted = "SessionStarted";
    public const string ContentBlockReceived = "ContentBlockReceived";
    public const string MessageReceived = "MessageReceived";
    public const string ResultReceived = "ResultReceived";
    public const string QuestionReceived = "QuestionReceived";
    public const string SessionEnded = "SessionEnded";
    public const string Error = "Error";
}

/// <summary>
/// Base class for SSE event data.
/// </summary>
public abstract class SseEventData
{
    /// <summary>
    /// The session ID this event belongs to.
    /// </summary>
    public required string SessionId { get; set; }
}

/// <summary>
/// Event data for session started.
/// </summary>
public class SessionStartedData : SseEventData
{
    /// <summary>
    /// The Claude conversation ID for resumption.
    /// </summary>
    public string? ConversationId { get; set; }
}

/// <summary>
/// Event data for content block received.
/// </summary>
public class ContentBlockReceivedData : SseEventData
{
    /// <summary>
    /// The content block type (Text, Thinking, ToolUse, ToolResult).
    /// </summary>
    public required string Type { get; set; }

    /// <summary>
    /// Text content (for Text and Thinking blocks).
    /// </summary>
    public string? Text { get; set; }

    /// <summary>
    /// Tool name (for ToolUse and ToolResult blocks).
    /// </summary>
    public string? ToolName { get; set; }

    /// <summary>
    /// Tool input JSON (for ToolUse blocks).
    /// </summary>
    public string? ToolInput { get; set; }

    /// <summary>
    /// Tool use ID for linking tool results.
    /// </summary>
    public string? ToolUseId { get; set; }

    /// <summary>
    /// Whether the tool succeeded (for ToolResult blocks).
    /// </summary>
    public bool? ToolSuccess { get; set; }

    /// <summary>
    /// Index of the content block within the message.
    /// </summary>
    public int Index { get; set; }
}

/// <summary>
/// Event data for message received.
/// </summary>
public class MessageReceivedData : SseEventData
{
    /// <summary>
    /// Message role (User or Assistant).
    /// </summary>
    public required string Role { get; set; }

    /// <summary>
    /// Content blocks in this message.
    /// </summary>
    public required List<ContentBlockReceivedData> Content { get; set; }
}

/// <summary>
/// Event data for result received.
/// </summary>
public class ResultReceivedData : SseEventData
{
    /// <summary>
    /// Total cost in USD.
    /// </summary>
    public decimal TotalCostUsd { get; set; }

    /// <summary>
    /// Duration in milliseconds.
    /// </summary>
    public int DurationMs { get; set; }

    /// <summary>
    /// The Claude conversation ID for resumption.
    /// </summary>
    public string? ConversationId { get; set; }
}

/// <summary>
/// Option for a user question.
/// </summary>
public class QuestionOptionData
{
    /// <summary>
    /// Display label for the option.
    /// </summary>
    public required string Label { get; set; }

    /// <summary>
    /// Description of what this option means.
    /// </summary>
    public required string Description { get; set; }
}

/// <summary>
/// A single question for the user.
/// </summary>
public class UserQuestionData
{
    /// <summary>
    /// The question text.
    /// </summary>
    public required string Question { get; set; }

    /// <summary>
    /// Short header/label for the question.
    /// </summary>
    public required string Header { get; set; }

    /// <summary>
    /// Available options.
    /// </summary>
    public required List<QuestionOptionData> Options { get; set; }

    /// <summary>
    /// Whether multiple selections are allowed.
    /// </summary>
    public bool MultiSelect { get; set; }
}

/// <summary>
/// Event data for question received.
/// </summary>
public class QuestionReceivedData : SseEventData
{
    /// <summary>
    /// Unique ID for this pending question.
    /// </summary>
    public required string QuestionId { get; set; }

    /// <summary>
    /// Tool use ID associated with the question.
    /// </summary>
    public required string ToolUseId { get; set; }

    /// <summary>
    /// The questions to present to the user.
    /// </summary>
    public required List<UserQuestionData> Questions { get; set; }
}

/// <summary>
/// Event data for session ended.
/// </summary>
public class SessionEndedData : SseEventData
{
    /// <summary>
    /// Optional reason for session end.
    /// </summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Event data for errors.
/// </summary>
public class ErrorData : SseEventData
{
    /// <summary>
    /// Error message.
    /// </summary>
    public required string Message { get; set; }

    /// <summary>
    /// Error code for categorization.
    /// </summary>
    public string? Code { get; set; }

    /// <summary>
    /// Whether the error is recoverable.
    /// </summary>
    public bool IsRecoverable { get; set; }
}
