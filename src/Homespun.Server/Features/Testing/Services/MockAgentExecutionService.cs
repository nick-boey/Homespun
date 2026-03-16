using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Shared.Models.Sessions;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Mock implementation of IAgentExecutionService for testing.
/// Provides minimal in-memory session tracking with synthetic responses.
/// </summary>
public class MockAgentExecutionService : IAgentExecutionService
{
    private readonly ILogger<MockAgentExecutionService> _logger;
    private readonly ConcurrentDictionary<string, MockSession> _sessions = new();

    private record MockSession(
        string SessionId,
        string WorkingDirectory,
        SessionMode Mode,
        string Model,
        DateTime CreatedAt)
    {
        public string? ConversationId { get; set; }
        public DateTime LastActivityAt { get; set; } = CreatedAt;
    }

    public MockAgentExecutionService(ILogger<MockAgentExecutionService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SdkMessage> StartSessionAsync(
        AgentStartRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid().ToString();
        _logger.LogInformation("[Mock] Starting session {SessionId} in directory {WorkingDirectory}",
            sessionId, request.WorkingDirectory);

        var session = new MockSession(
            sessionId,
            request.WorkingDirectory,
            request.Mode,
            request.Model,
            DateTime.UtcNow);

        _sessions[sessionId] = session;

        // Yield synthetic session_started message
        yield return new SdkSystemMessage(sessionId, null, "session_started", request.Model, null);

        // Simulate brief processing delay
        await Task.Delay(100, cancellationToken);

        // Yield a synthetic assistant response
        var responseContent = new List<SdkContentBlock>
        {
            new SdkTextBlock("[Mock] Session started successfully.")
        };
        var apiMessage = new SdkApiMessage("assistant", responseContent);
        yield return new SdkAssistantMessage(sessionId, Guid.NewGuid().ToString(), apiMessage, null);

        // Yield synthetic result
        yield return new SdkResultMessage(
            SessionId: sessionId,
            Uuid: Guid.NewGuid().ToString(),
            Subtype: "success",
            DurationMs: 100,
            DurationApiMs: 50,
            IsError: false,
            NumTurns: 1,
            TotalCostUsd: 0m,
            Result: null);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SdkMessage> SendMessageAsync(
        AgentMessageRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Mock] SendMessage to session {SessionId}: {Message}",
            request.SessionId, request.Message.Length > 50 ? request.Message[..50] + "..." : request.Message);

        if (!_sessions.TryGetValue(request.SessionId, out var session))
        {
            _logger.LogWarning("[Mock] Session {SessionId} not found", request.SessionId);
            yield break;
        }

        session.LastActivityAt = DateTime.UtcNow;

        // Simulate processing delay
        await Task.Delay(100, cancellationToken);

        // Yield synthetic assistant response
        var responseContent = new List<SdkContentBlock>
        {
            new SdkTextBlock($"[Mock] Received your message: {request.Message}")
        };
        var apiMessage = new SdkApiMessage("assistant", responseContent);
        yield return new SdkAssistantMessage(request.SessionId, Guid.NewGuid().ToString(), apiMessage, null);

        // Yield synthetic result
        yield return new SdkResultMessage(
            SessionId: request.SessionId,
            Uuid: Guid.NewGuid().ToString(),
            Subtype: "success",
            DurationMs: 100,
            DurationApiMs: 50,
            IsError: false,
            NumTurns: 1,
            TotalCostUsd: 0m,
            Result: null);
    }

    /// <inheritdoc />
    public Task StopSessionAsync(string sessionId, bool forceStopContainer = false, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Mock] Stopping session {SessionId}", sessionId);
        _sessions.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task InterruptSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Mock] Interrupting session {SessionId}", sessionId);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<AgentSessionStatus?> GetSessionStatusAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            return Task.FromResult<AgentSessionStatus?>(new AgentSessionStatus(
                session.SessionId,
                session.WorkingDirectory,
                session.Mode,
                session.Model,
                session.ConversationId,
                session.CreatedAt,
                session.LastActivityAt));
        }

        return Task.FromResult<AgentSessionStatus?>(null);
    }

    /// <inheritdoc />
    public Task<string?> ReadFileFromAgentAsync(string sessionId, string filePath, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Mock] ReadFile from session {SessionId}: {FilePath}", sessionId, filePath);
        return Task.FromResult<string?>(null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AgentSessionStatus>> ListSessionsAsync(CancellationToken cancellationToken = default)
    {
        var statuses = _sessions.Values.Select(session => new AgentSessionStatus(
            session.SessionId,
            session.WorkingDirectory,
            session.Mode,
            session.Model,
            session.ConversationId,
            session.CreatedAt,
            session.LastActivityAt
        )).ToList().AsReadOnly();

        return Task.FromResult<IReadOnlyList<AgentSessionStatus>>(statuses);
    }

    /// <inheritdoc />
    public Task<int> CleanupOrphanedContainersAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Mock] CleanupOrphanedContainers (no-op)");
        return Task.FromResult(0);
    }

    /// <inheritdoc />
    public Task<bool> AnswerQuestionAsync(string sessionId, Dictionary<string, string> answers,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Mock] AnswerQuestion for session {SessionId}", sessionId);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<bool> ApprovePlanAsync(string sessionId, bool approved, bool keepContext, string? feedback = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Mock] ApprovePlan for session {SessionId}: approved={Approved}", sessionId, approved);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public Task<CloneContainerState?> GetCloneContainerStateAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] GetCloneContainerState for {WorkingDirectory}", workingDirectory);
        return Task.FromResult<CloneContainerState?>(null);
    }

    /// <inheritdoc />
    public Task TerminateCloneSessionAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Mock] TerminateCloneSession for {WorkingDirectory}", workingDirectory);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] ListContainers");
        return Task.FromResult<IReadOnlyList<ContainerInfo>>(Array.Empty<ContainerInfo>());
    }

    /// <inheritdoc />
    public Task<bool> StopContainerByIdAsync(string containerId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Mock] StopContainerById {ContainerId}", containerId);
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<ContainerRestartResult?> RestartContainerAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Mock] RestartContainer for session {SessionId}", sessionId);
        return Task.FromResult<ContainerRestartResult?>(null);
    }

    /// <inheritdoc />
    public Task<bool> SetSessionModeAsync(string sessionId, SessionMode mode, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Mock] SetSessionMode for session {SessionId}: {Mode}", sessionId, mode);

        if (_sessions.TryGetValue(sessionId, out _))
        {
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<bool> SetSessionModelAsync(string sessionId, string model, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Mock] SetSessionModel for session {SessionId}: {Model}", sessionId, model);

        if (_sessions.TryGetValue(sessionId, out _))
        {
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }
}
