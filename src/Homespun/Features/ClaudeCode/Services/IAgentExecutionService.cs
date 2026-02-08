using Homespun.ClaudeAgentSdk;
using Homespun.Features.ClaudeCode.Data;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Represents the result of starting an agent session.
/// </summary>
public record AgentSessionInfo(
    string SessionId,
    string? ConversationId
);

/// <summary>
/// Request to start an agent session.
/// </summary>
public record AgentStartRequest(
    string WorkingDirectory,
    SessionMode Mode,
    string Model,
    string Prompt,
    string? SystemPrompt = null,
    string? ResumeSessionId = null,
    PermissionMode? PermissionMode = null
);

/// <summary>
/// Request to send a message to an agent.
/// </summary>
public record AgentMessageRequest(
    string SessionId,
    string Message,
    string? Model = null,
    PermissionMode? PermissionMode = null
);

/// <summary>
/// Request to answer a pending question.
/// </summary>
public record AgentAnswerRequest(
    string SessionId,
    string ToolUseId,
    Dictionary<string, string> Answers
);

/// <summary>
/// Base class for agent events.
/// </summary>
public abstract record AgentEvent(string SessionId);

/// <summary>
/// Event indicating session has started.
/// </summary>
public record AgentSessionStartedEvent(
    string SessionId,
    string? ConversationId
) : AgentEvent(SessionId);

/// <summary>
/// Event containing a content block from the agent.
/// </summary>
public record AgentContentBlockEvent(
    string SessionId,
    ClaudeContentType Type,
    string? Text,
    string? ToolName,
    string? ToolInput,
    string? ToolUseId,
    bool? ToolSuccess,
    int Index
) : AgentEvent(SessionId);

/// <summary>
/// Event containing a complete message.
/// </summary>
public record AgentMessageEvent(
    string SessionId,
    ClaudeMessageRole Role,
    List<AgentContentBlockEvent> Content
) : AgentEvent(SessionId);

/// <summary>
/// Event containing the result of agent execution.
/// </summary>
public record AgentResultEvent(
    string SessionId,
    decimal TotalCostUsd,
    int DurationMs,
    string? ConversationId
) : AgentEvent(SessionId);

/// <summary>
/// Event indicating a question from the agent.
/// </summary>
public record AgentQuestionEvent(
    string SessionId,
    string QuestionId,
    string ToolUseId,
    List<AgentQuestion> Questions
) : AgentEvent(SessionId);

/// <summary>
/// A question to present to the user.
/// </summary>
public record AgentQuestion(
    string Question,
    string Header,
    List<AgentQuestionOption> Options,
    bool MultiSelect
);

/// <summary>
/// An option for a question.
/// </summary>
public record AgentQuestionOption(
    string Label,
    string Description
);

/// <summary>
/// Event indicating the session has ended.
/// </summary>
public record AgentSessionEndedEvent(
    string SessionId,
    string? Reason
) : AgentEvent(SessionId);

/// <summary>
/// Event indicating an error occurred.
/// </summary>
public record AgentErrorEvent(
    string SessionId,
    string Message,
    string? Code,
    bool IsRecoverable
) : AgentEvent(SessionId);

/// <summary>
/// Status of an agent session.
/// </summary>
public record AgentSessionStatus(
    string SessionId,
    string WorkingDirectory,
    SessionMode Mode,
    string Model,
    string? ConversationId,
    DateTime CreatedAt,
    DateTime LastActivityAt
);

/// <summary>
/// Service for executing Claude agents in various environments (local, Docker, Azure).
/// </summary>
public interface IAgentExecutionService
{
    /// <summary>
    /// Starts a new agent session and streams events.
    /// </summary>
    /// <param name="request">Request parameters for starting the session.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of agent events.</returns>
    IAsyncEnumerable<AgentEvent> StartSessionAsync(
        AgentStartRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message to an existing session and streams events.
    /// </summary>
    /// <param name="request">Request containing session ID and message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Async enumerable of agent events.</returns>
    IAsyncEnumerable<AgentEvent> SendMessageAsync(
        AgentMessageRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Answers a pending question by sending a tool result to the running CLI process.
    /// Events continue flowing through the original StartSessionAsync/SendMessageAsync stream.
    /// </summary>
    /// <param name="request">Request containing session ID, tool use ID, and answers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task AnswerQuestionAsync(
        AgentAnswerRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops an agent session.
    /// </summary>
    /// <param name="sessionId">The session ID to stop.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StopSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Interrupts (cancels) an agent session's current execution without fully stopping it.
    /// The session remains in a state where new messages can be sent.
    /// </summary>
    /// <param name="sessionId">The session ID to interrupt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task InterruptSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the status of an agent session.
    /// </summary>
    /// <param name="sessionId">The session ID to check.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Session status, or null if not found.</returns>
    Task<AgentSessionStatus?> GetSessionStatusAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a file from the agent's filesystem. For local agents, reads directly from disk.
    /// For container-based agents, fetches the file via the worker container's API.
    /// </summary>
    /// <param name="sessionId">The agent session ID.</param>
    /// <param name="filePath">Absolute path to the file within the agent's filesystem.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The file content, or null if the file is not found or the session doesn't exist.</returns>
    Task<string?> ReadFileFromAgentAsync(string sessionId, string filePath, CancellationToken cancellationToken = default);
}
