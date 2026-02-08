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
    string? IssueId = null,
    string? ProjectId = null,
    string? ProjectName = null
);

/// <summary>
/// Request to send a message to an agent.
/// </summary>
public record AgentMessageRequest(
    string SessionId,
    string Message,
    string? Model = null
);

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
/// Returns raw SdkMessage types from the Claude SDK. All content block assembly,
/// question parsing, and message formatting is handled by the consumer (ClaudeSessionService).
/// </summary>
public interface IAgentExecutionService
{
    /// <summary>
    /// Starts a new agent session and streams SDK messages.
    /// </summary>
    IAsyncEnumerable<SdkMessage> StartSessionAsync(
        AgentStartRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a message to an existing session and streams SDK messages.
    /// </summary>
    IAsyncEnumerable<SdkMessage> SendMessageAsync(
        AgentMessageRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops an agent session.
    /// </summary>
    Task StopSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Interrupts an agent session's current execution without fully stopping it.
    /// </summary>
    Task InterruptSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the status of an agent session.
    /// </summary>
    Task<AgentSessionStatus?> GetSessionStatusAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a file from the agent's filesystem.
    /// </summary>
    Task<string?> ReadFileFromAgentAsync(string sessionId, string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all currently tracked sessions in the execution service.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of session statuses for all tracked sessions.</returns>
    Task<IReadOnlyList<AgentSessionStatus>> ListSessionsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers and stops containers/sessions not tracked in the session store.
    /// For Docker mode: runs <c>docker ps</c> to find orphaned homespun-agent containers.
    /// Returns count of orphaned items cleaned up.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Count of orphaned containers/sessions cleaned up.</returns>
    Task<int> CleanupOrphanedContainersAsync(CancellationToken cancellationToken = default);
}
