using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Shared.Models.Sessions;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Minimal mock of <see cref="IAgentExecutionService"/> for offline/demo/mock-mode.
///
/// <para>
/// Since the pipeline was migrated to A2A-native messaging, the server only consumes
/// control-plane <see cref="SdkMessage"/> variants (<c>SdkSystemMessage</c>,
/// <c>SdkResultMessage</c>, <c>SdkQuestionPendingMessage</c>, <c>SdkPlanPendingMessage</c>)
/// from the worker — everything content-bearing flows through
/// <c>SessionEventIngestor</c> as A2A/AG-UI envelopes. This mock reflects that: it emits
/// only a <c>session_started</c> system message and a terminating <c>SdkResultMessage</c>
/// for each turn, so the message loop in <c>MessageProcessingService</c> completes cleanly.
/// Anything richer belongs on the A2A stream in a separate mock layer.
/// </para>
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

        _sessions[sessionId] = new MockSession(
            sessionId,
            request.WorkingDirectory,
            request.Mode,
            request.Model,
            DateTime.UtcNow);

        yield return new SdkSystemMessage(sessionId, null, "session_started", request.Model, null);

        await Task.Delay(50, cancellationToken);

        yield return new SdkResultMessage(
            SessionId: sessionId,
            Uuid: null,
            Subtype: "success",
            DurationMs: 50,
            DurationApiMs: 0,
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
        _logger.LogDebug("[Mock] SendMessage to session {SessionId}", request.SessionId);

        if (_sessions.TryGetValue(request.SessionId, out var session))
        {
            session.LastActivityAt = DateTime.UtcNow;
        }

        await Task.Delay(50, cancellationToken);

        yield return new SdkResultMessage(
            SessionId: request.SessionId,
            Uuid: null,
            Subtype: "success",
            DurationMs: 50,
            DurationApiMs: 0,
            IsError: false,
            NumTurns: 1,
            TotalCostUsd: 0m,
            Result: null);
    }

    /// <inheritdoc />
    public Task StopSessionAsync(string sessionId, bool forceStopContainer = false, CancellationToken cancellationToken = default)
    {
        _sessions.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task InterruptSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> AnswerQuestionAsync(string sessionId, Dictionary<string, string> answers,
        CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    /// <inheritdoc />
    public Task<bool> ApprovePlanAsync(string sessionId, bool approved, bool keepContext, string? feedback = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    /// <inheritdoc />
    public Task<bool> SetSessionModeAsync(string sessionId, SessionMode mode, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    /// <inheritdoc />
    public Task<bool> SetSessionModelAsync(string sessionId, string model, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    /// <inheritdoc />
    public Task<CloneContainerState?> GetCloneContainerStateAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
        => Task.FromResult<CloneContainerState?>(null);

    /// <inheritdoc />
    public Task TerminateCloneSessionAsync(string workingDirectory, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <inheritdoc />
    public Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ContainerInfo>>(Array.Empty<ContainerInfo>());

    /// <inheritdoc />
    public Task<bool> StopContainerByIdAsync(string containerId, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    /// <inheritdoc />
    public Task<ContainerRestartResult?> RestartContainerAsync(string sessionId, CancellationToken cancellationToken = default)
        => Task.FromResult<ContainerRestartResult?>(null);

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
        => Task.FromResult<string?>(null);

    /// <inheritdoc />
    public Task<IReadOnlyList<AgentSessionStatus>> ListSessionsAsync(CancellationToken cancellationToken = default)
    {
        var statuses = _sessions.Values.Select(s => new AgentSessionStatus(
            s.SessionId,
            s.WorkingDirectory,
            s.Mode,
            s.Model,
            s.ConversationId,
            s.CreatedAt,
            s.LastActivityAt)).ToList().AsReadOnly();
        return Task.FromResult<IReadOnlyList<AgentSessionStatus>>(statuses);
    }

    /// <inheritdoc />
    public Task<int> CleanupOrphanedContainersAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(0);
}
