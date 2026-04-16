using Homespun.Features.ClaudeCode.Hubs;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Shared.Models.Sessions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Mock implementation of <see cref="IClaudeSessionService"/> for demo/mock-mode.
/// Simulates session lifecycle transitions (start, send, answer, plan approve, mode/model
/// updates) but does NOT fabricate message content — mock mode now relies on the A2A event
/// stream and <see cref="SessionEventIngestor"/> for any rendered message content.
/// </summary>
public class MockClaudeSessionService : IClaudeSessionService
{
    private readonly IClaudeSessionStore _sessionStore;
    private readonly IHubContext<ClaudeCodeHub> _hubContext;
    private readonly ILogger<MockClaudeSessionService> _logger;

    public MockClaudeSessionService(
        IClaudeSessionStore sessionStore,
        IHubContext<ClaudeCodeHub> hubContext,
        ILogger<MockClaudeSessionService> logger)
    {
        _sessionStore = sessionStore;
        _hubContext = hubContext;
        _logger = logger;
    }

    public Task<ClaudeSession> StartSessionAsync(
        string entityId,
        string projectId,
        string workingDirectory,
        SessionMode mode,
        string model,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] StartSession for entity {EntityId} in project {ProjectId}, mode: {Mode}",
            entityId, projectId, mode);

        var session = new ClaudeSession
        {
            Id = Guid.NewGuid().ToString(),
            EntityId = entityId,
            ProjectId = projectId,
            WorkingDirectory = workingDirectory,
            Mode = mode,
            Model = model,
            SystemPrompt = systemPrompt,
            Status = ClaudeSessionStatus.WaitingForInput,
            CreatedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow
        };

        _sessionStore.Add(session);

        return Task.FromResult(session);
    }

    public async Task SendMessageAsync(
        string sessionId,
        string message,
        CancellationToken cancellationToken = default)
    {
        await SendMessageAsync(sessionId, message, SessionMode.Build, cancellationToken);
    }

    public Task SendMessageAsync(
        string sessionId,
        string message,
        SessionMode mode,
        CancellationToken cancellationToken = default)
    {
        return SendMessageAsync(sessionId, message, mode, null, cancellationToken);
    }

    public async Task SendMessageAsync(
        string sessionId,
        string message,
        SessionMode mode,
        string? model,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] SendMessage to session {SessionId} with model {Model}: {Message}",
            sessionId, model ?? "default", message);

        var session = _sessionStore.GetById(sessionId);
        if (session == null)
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        session.Status = ClaudeSessionStatus.Running;
        session.LastActivityAt = DateTime.UtcNow;
        _sessionStore.Update(session);
        await _hubContext.BroadcastSessionStatusChanged(sessionId, session.Status);

        // Simulate processing delay
        await Task.Delay(300, cancellationToken);

        session.Status = ClaudeSessionStatus.WaitingForInput;
        session.LastActivityAt = DateTime.UtcNow;
        session.TotalCostUsd += 0.01m;
        session.TotalDurationMs += 500;
        _sessionStore.Update(session);
        await _hubContext.BroadcastSessionStatusChanged(sessionId, session.Status);
    }

    public Task ClearContextAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] ClearContext for session {SessionId}", sessionId);

        var session = _sessionStore.GetById(sessionId);
        if (session != null)
        {
            session.LastActivityAt = DateTime.UtcNow;
            _sessionStore.Update(session);
        }

        return Task.CompletedTask;
    }

    public async Task ExecutePlanAsync(string sessionId, bool clearContext = true, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] ExecutePlan for session {SessionId}, clearContext={ClearContext}",
            sessionId, clearContext);

        var session = _sessionStore.GetById(sessionId);
        if (session == null || string.IsNullOrEmpty(session.PlanContent))
        {
            _logger.LogWarning("[Mock] Cannot execute plan: session {SessionId} not found or no plan content", sessionId);
            return;
        }

        if (clearContext)
        {
            await ClearContextAsync(sessionId, cancellationToken);
            var contextClearedEvent = AGUIEventFactory.CreateCustomEvent(AGUICustomEventName.ContextCleared, sessionId);
            await _hubContext.BroadcastAGUICustomEvent(sessionId, contextClearedEvent);
        }

        session.Status = ClaudeSessionStatus.Running;
        _sessionStore.Update(session);
        await _hubContext.BroadcastSessionStatusChanged(sessionId, session.Status);

        await Task.Delay(300, cancellationToken);

        session.Status = ClaudeSessionStatus.WaitingForInput;
        session.LastActivityAt = DateTime.UtcNow;
        _sessionStore.Update(session);
        await _hubContext.BroadcastSessionStatusChanged(sessionId, session.Status);
    }

    public Task StopSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] StopSession {SessionId}", sessionId);

        var session = _sessionStore.GetById(sessionId);
        if (session != null)
        {
            session.Status = ClaudeSessionStatus.Stopped;
            session.LastActivityAt = DateTime.UtcNow;
            _sessionStore.Update(session);
        }

        return Task.CompletedTask;
    }

    public async Task<int> StopAllSessionsForEntityAsync(string entityId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] StopAllSessionsForEntity {EntityId}", entityId);

        var sessions = _sessionStore.GetAllByEntityId(entityId);
        var stoppedCount = 0;

        foreach (var session in sessions)
        {
            await StopSessionAsync(session.Id, cancellationToken);
            stoppedCount++;
        }

        return stoppedCount;
    }

    public Task InterruptSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] InterruptSession {SessionId}", sessionId);

        var session = _sessionStore.GetById(sessionId);
        if (session != null)
        {
            session.Status = ClaudeSessionStatus.WaitingForInput;
            session.PendingQuestion = null;
            session.LastActivityAt = DateTime.UtcNow;
            _sessionStore.Update(session);
        }

        return Task.CompletedTask;
    }

    public ClaudeSession? GetSession(string sessionId) => _sessionStore.GetById(sessionId);

    public ClaudeSession? GetSessionByEntityId(string entityId) => _sessionStore.GetByEntityId(entityId);

    public IReadOnlyList<ClaudeSession> GetSessionsForProject(string projectId) => _sessionStore.GetByProjectId(projectId);

    public IReadOnlyList<ClaudeSession> GetAllSessions() => _sessionStore.GetAll();

    public Task<ClaudeSession> ResumeSessionAsync(
        string sessionId,
        string entityId,
        string projectId,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] ResumeSession {SessionId} for entity {EntityId}", sessionId, entityId);

        var session = new ClaudeSession
        {
            Id = Guid.NewGuid().ToString(),
            EntityId = entityId,
            ProjectId = projectId,
            WorkingDirectory = workingDirectory,
            Mode = SessionMode.Build,
            Model = "opus",
            Status = ClaudeSessionStatus.WaitingForInput,
            CreatedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow
        };

        _sessionStore.Add(session);

        return Task.FromResult(session);
    }

    public Task<IReadOnlyList<ResumableSession>> GetResumableSessionsAsync(
        string entityId,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] GetResumableSessions for entity {EntityId}", entityId);

        var sessions = new List<ResumableSession>
        {
            new ResumableSession(
                SessionId: Guid.NewGuid().ToString(),
                LastActivityAt: DateTime.UtcNow.AddHours(-1),
                Mode: SessionMode.Build,
                Model: "opus",
                MessageCount: 5
            )
        };

        return Task.FromResult<IReadOnlyList<ResumableSession>>(sessions);
    }

    public async Task AnswerQuestionAsync(
        string sessionId,
        Dictionary<string, string> answers,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] AnswerQuestion for session {SessionId} with {AnswerCount} answers",
            sessionId, answers.Count);

        var session = _sessionStore.GetById(sessionId);
        if (session == null)
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        session.PendingQuestion = null;
        session.Status = ClaudeSessionStatus.Running;
        _sessionStore.Update(session);
        await _hubContext.BroadcastSessionStatusChanged(sessionId, session.Status);

        await Task.Delay(300, cancellationToken);

        session.Status = ClaudeSessionStatus.WaitingForInput;
        session.LastActivityAt = DateTime.UtcNow;
        _sessionStore.Update(session);
        await _hubContext.BroadcastSessionStatusChanged(sessionId, session.Status);
    }

    public async Task ApprovePlanAsync(
        string sessionId,
        bool approved,
        bool keepContext,
        string? feedback = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] ApprovePlan for session {SessionId}: approved={Approved}, keepContext={KeepContext}",
            sessionId, approved, keepContext);

        var session = _sessionStore.GetById(sessionId);
        if (session == null)
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        session.PlanContent = null;
        session.PlanFilePath = null;
        session.HasPendingPlanApproval = false;

        if (approved)
        {
            await ExecutePlanAsync(sessionId, clearContext: !keepContext, cancellationToken);
        }
        else
        {
            session.Status = ClaudeSessionStatus.Running;
            _sessionStore.Update(session);
            await _hubContext.BroadcastSessionStatusChanged(sessionId, session.Status);

            session.Status = ClaudeSessionStatus.WaitingForInput;
            _sessionStore.Update(session);
            await _hubContext.BroadcastSessionStatusChanged(sessionId, session.Status);
        }
    }

    public Task<AgentStartCheckResult> CheckCloneStateAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] CheckCloneState for working directory {WorkingDirectory}", workingDirectory);
        return Task.FromResult(new AgentStartCheckResult(AgentStartAction.StartNew, null, null));
    }

    public Task<ClaudeSession> StartSessionWithTerminationAsync(
        string entityId,
        string projectId,
        string workingDirectory,
        SessionMode mode,
        string model,
        bool terminateExisting,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] StartSessionWithTermination for entity {EntityId}, terminateExisting={TerminateExisting}",
            entityId, terminateExisting);
        return StartSessionAsync(entityId, projectId, workingDirectory, mode, model, systemPrompt, cancellationToken);
    }

    public Task<ClaudeSession?> RestartSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] RestartSession for session {SessionId}", sessionId);

        var session = _sessionStore.GetById(sessionId);
        if (session == null)
        {
            return Task.FromResult<ClaudeSession?>(null);
        }

        session.Status = ClaudeSessionStatus.WaitingForInput;
        session.ErrorMessage = null;

        return Task.FromResult<ClaudeSession?>(session);
    }

    public async Task<string> AcceptIssueChangesAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var session = _sessionStore.GetById(sessionId);
        if (session == null)
        {
            throw new KeyNotFoundException($"Session with ID {sessionId} not found");
        }

        await StopSessionAsync(sessionId, cancellationToken);
        return $"/projects/{session.ProjectId}/issues/{session.EntityId}";
    }

    public async Task<string> CancelIssueChangesAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var session = _sessionStore.GetById(sessionId);
        if (session == null)
        {
            throw new KeyNotFoundException($"Session with ID {sessionId} not found");
        }

        await StopSessionAsync(sessionId, cancellationToken);
        return $"/projects/{session.ProjectId}/issues/{session.EntityId}";
    }

    public async Task SetSessionModeAsync(string sessionId, SessionMode mode, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] SetSessionMode for session {SessionId} to {Mode}", sessionId, mode);

        var session = _sessionStore.GetById(sessionId);
        if (session == null)
        {
            throw new KeyNotFoundException($"Session with ID {sessionId} not found");
        }

        session.Mode = mode;
        session.LastActivityAt = DateTime.UtcNow;
        _sessionStore.Update(session);

        await _hubContext.BroadcastSessionModeModelChanged(sessionId, session.Mode, session.Model);
    }

    public async Task SetSessionModelAsync(string sessionId, string model, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] SetSessionModel for session {SessionId} to {Model}", sessionId, model);

        var session = _sessionStore.GetById(sessionId);
        if (session == null)
        {
            throw new KeyNotFoundException($"Session with ID {sessionId} not found");
        }

        session.Model = model;
        session.LastActivityAt = DateTime.UtcNow;
        _sessionStore.Update(session);

        await _hubContext.BroadcastSessionModeModelChanged(sessionId, session.Mode, session.Model);
    }

    public async Task<ClaudeSession> ClearContextAndStartNewAsync(
        string currentSessionId,
        string? initialPrompt = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] ClearContextAndStartNew for session {SessionId}", currentSessionId);

        var currentSession = _sessionStore.GetById(currentSessionId);
        if (currentSession == null)
        {
            throw new KeyNotFoundException($"Session with ID {currentSessionId} not found");
        }

        await StopSessionAsync(currentSessionId, cancellationToken);

        var newSession = await StartSessionAsync(
            currentSession.EntityId,
            currentSession.ProjectId,
            currentSession.WorkingDirectory,
            currentSession.Mode,
            currentSession.Model,
            currentSession.SystemPrompt,
            cancellationToken);

        if (!string.IsNullOrEmpty(initialPrompt))
        {
            await SendMessageAsync(newSession.Id, initialPrompt, newSession.Mode, newSession.Model, cancellationToken);
        }

        await _hubContext.BroadcastSessionContextCleared(currentSessionId, newSession);

        return newSession;
    }
}
