using Homespun.ClaudeAgentSdk;
using Homespun.Shared.Models.Sessions;

using SharedPermissionMode = Homespun.Shared.Models.Sessions.PermissionMode;

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
    SharedPermissionMode PermissionMode = SharedPermissionMode.BypassPermissions,
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
/// State of a container for a working directory.
/// </summary>
public record CloneContainerState(
    string WorkingDirectory,
    string ContainerIdentifier,
    string? ActiveSessionId,
    string? WorkerSessionId,
    ClaudeSessionStatus SessionStatus,
    DateTime? LastActivityAt,
    bool HasPendingQuestion,
    bool HasPendingPlanApproval
);

/// <summary>
/// Action to take when starting a session.
/// </summary>
public enum AgentStartAction
{
    /// <summary>No container or session stopped/error - start new session.</summary>
    StartNew,
    /// <summary>Active session exists (working/question/plan) - notify user.</summary>
    NotifyActive,
    /// <summary>Idle session exists - ask user to confirm termination.</summary>
    ConfirmTerminate,
    /// <summary>Container exists but no active session - reuse container.</summary>
    ReuseContainer
}

/// <summary>
/// Result of checking container state before starting.
/// </summary>
public record AgentStartCheckResult(
    AgentStartAction Action,
    CloneContainerState? ExistingState,
    string? Message
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

    /// <summary>
    /// Answers a pending question in a worker session (Docker/Azure).
    /// Returns true if the worker had a pending question and it was resolved.
    /// For local mode, returns false (local answers go through SendMessageAsync).
    /// </summary>
    Task<bool> AnswerQuestionAsync(string sessionId, Dictionary<string, string> answers,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Approves or rejects a pending plan in a worker session (Docker/Azure).
    /// Returns true if the worker had a pending plan approval and it was resolved.
    /// For local mode, returns false (local plan approval uses ExecutePlanAsync fallback).
    /// </summary>
    Task<bool> ApprovePlanAsync(string sessionId, bool approved, bool keepContext, string? feedback = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the active session state for a working directory.
    /// Returns null if no container exists for that working directory.
    /// </summary>
    Task<CloneContainerState?> GetCloneContainerStateAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Terminates any active session in a container, preparing for a new session.
    /// </summary>
    Task TerminateCloneSessionAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default);
}
