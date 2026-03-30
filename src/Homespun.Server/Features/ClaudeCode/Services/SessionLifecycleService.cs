using Homespun.Features.ClaudeCode.Hubs;
using Homespun.Features.Fleece.Services;
using Homespun.Shared.Models.Sessions;
using Microsoft.AspNetCore.SignalR;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Manages session lifecycle: start, resume, stop, interrupt, restart, dispose,
/// clone state checks, mode/model changes, and issue operations.
/// </summary>
public class SessionLifecycleService : ISessionLifecycleService
{
    private readonly IClaudeSessionStore _sessionStore;
    private readonly ILogger<SessionLifecycleService> _logger;
    private readonly IHubContext<ClaudeCodeHub> _hubContext;
    private readonly IClaudeSessionDiscovery _sessionDiscovery;
    private readonly ISessionMetadataStore _metadataStore;
    private readonly IHooksService _hooksService;
    private readonly IMessageCacheStore _messageCache;
    private readonly IAgentExecutionService _agentExecutionService;
    private readonly IAGUIEventService _agUIEventService;
    private readonly ISessionStateManager _stateManager;
    private readonly Lazy<IMessageProcessingService> _messageProcessing;

    public SessionLifecycleService(
        IClaudeSessionStore sessionStore,
        ILogger<SessionLifecycleService> logger,
        IHubContext<ClaudeCodeHub> hubContext,
        IClaudeSessionDiscovery sessionDiscovery,
        ISessionMetadataStore metadataStore,
        IHooksService hooksService,
        IMessageCacheStore messageCache,
        IAgentExecutionService agentExecutionService,
        IAGUIEventService agUIEventService,
        ISessionStateManager stateManager,
        Lazy<IMessageProcessingService> messageProcessing)
    {
        _sessionStore = sessionStore;
        _logger = logger;
        _hubContext = hubContext;
        _sessionDiscovery = sessionDiscovery;
        _metadataStore = metadataStore;
        _hooksService = hooksService;
        _messageCache = messageCache;
        _agentExecutionService = agentExecutionService;
        _agUIEventService = agUIEventService;
        _stateManager = stateManager;
        _messageProcessing = messageProcessing;
    }

    public async Task<ClaudeSession> StartSessionAsync(
        string entityId,
        string projectId,
        string workingDirectory,
        SessionMode mode,
        string model,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid().ToString();
        var session = new ClaudeSession
        {
            Id = sessionId,
            EntityId = entityId,
            ProjectId = projectId,
            WorkingDirectory = workingDirectory,
            Model = model,
            Mode = mode,
            Status = ClaudeSessionStatus.RunningHooks,
            CreatedAt = DateTime.UtcNow,
            SystemPrompt = systemPrompt
        };

        _sessionStore.Add(session);
        _logger.LogInformation("Created session {SessionId} for entity {EntityId} in mode {Mode}",
            sessionId, entityId, mode);

        // Create cancellation token source for this session
        _stateManager.GetOrCreateCts(sessionId);

        // Execute SessionStart hooks FIRST to collect output for system prompt
        await _hubContext.BroadcastSessionStatusChanged(sessionId, session.Status);

        var hookOutputs = new List<string>();
        try
        {
            var hookResults = await _hooksService.ExecuteSessionStartHooksAsync(
                sessionId, workingDirectory, cancellationToken);

            foreach (var result in hookResults)
            {
                if (result.Success && !string.IsNullOrWhiteSpace(result.Output))
                {
                    hookOutputs.Add(result.Output.Trim());
                }
            }

            _logger.LogInformation(
                "Session {SessionId} executed {Count} startup hook(s), {OutputCount} produced output",
                sessionId, hookResults.Count, hookOutputs.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error executing startup hooks for session {SessionId}", sessionId);
        }

        // Build combined system prompt from hook outputs and original system prompt
        var combinedSystemPrompt = BuildSystemPrompt(systemPrompt, hookOutputs);
        session.SystemPrompt = combinedSystemPrompt;

        if (hookOutputs.Count > 0)
        {
            _logger.LogInformation(
                "Session {SessionId} system prompt includes {Count} hook output(s) ({Length} chars total)",
                sessionId, hookOutputs.Count, combinedSystemPrompt?.Length ?? 0);
        }

        session.SystemPrompt = combinedSystemPrompt;
        session.Status = ClaudeSessionStatus.WaitingForInput;
        _logger.LogInformation("Session {SessionId} initialized and ready", sessionId);

        // Save metadata for future resumption
        var metadata = new SessionMetadata(
            SessionId: session.ConversationId ?? sessionId,
            EntityId: entityId,
            ProjectId: projectId,
            WorkingDirectory: workingDirectory,
            Mode: mode,
            Model: model,
            SystemPrompt: combinedSystemPrompt,
            CreatedAt: session.CreatedAt
        );
        await _metadataStore.SaveAsync(metadata, cancellationToken);

        // Initialize message cache for this session
        await _messageCache.InitializeSessionAsync(
            sessionId, entityId, projectId, mode, model, cancellationToken);

        // Notify clients about the new session
        await _hubContext.BroadcastSessionStarted(session);

        // Broadcast AG-UI run started event
        var runId = Guid.NewGuid().ToString();
        _stateManager.SetRunId(sessionId, runId);
        var runStartedEvent = _agUIEventService.CreateRunStarted(sessionId, runId);
        await _hubContext.BroadcastAGUIRunStarted(sessionId, runStartedEvent);

        return session;
    }

    public async Task<ClaudeSession> ResumeSessionAsync(
        string sessionId,
        string entityId,
        string projectId,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Resuming session {ClaudeSessionId} for entity {EntityId}", sessionId, entityId);

        var metadata = await _metadataStore.GetBySessionIdAsync(sessionId, cancellationToken);

        var newSessionId = Guid.NewGuid().ToString();
        var session = new ClaudeSession
        {
            Id = newSessionId,
            EntityId = entityId,
            ProjectId = projectId,
            WorkingDirectory = workingDirectory,
            ConversationId = sessionId,
            Model = metadata?.Model ?? "sonnet",
            Mode = metadata?.Mode ?? SessionMode.Build,
            SystemPrompt = metadata?.SystemPrompt,
            Status = ClaudeSessionStatus.Running,
            CreatedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow,
            Messages = []
        };

        _stateManager.GetOrCreateCts(newSessionId);
        _sessionStore.Add(session);

        _logger.LogInformation("Resumed session {NewSessionId} with ConversationId {ConversationId}",
            newSessionId, sessionId);

        await _hubContext.BroadcastSessionStarted(session);
        return session;
    }

    public async Task<IReadOnlyList<ResumableSession>> GetResumableSessionsAsync(
        string entityId,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Discovering resumable sessions for entity {EntityId} at {WorkingDirectory}",
            entityId, workingDirectory);

        var discoveredSessions = await _sessionDiscovery.DiscoverSessionsAsync(workingDirectory, cancellationToken);

        var resumableSessions = new List<ResumableSession>();
        foreach (var discovered in discoveredSessions)
        {
            var metadata = await _metadataStore.GetBySessionIdAsync(discovered.SessionId, cancellationToken);
            var messageCount = await _sessionDiscovery.GetMessageCountAsync(
                discovered.SessionId, workingDirectory, cancellationToken);

            resumableSessions.Add(new ResumableSession(
                SessionId: discovered.SessionId,
                LastActivityAt: discovered.LastModified,
                Mode: metadata?.Mode,
                Model: metadata?.Model,
                MessageCount: messageCount
            ));
        }

        _logger.LogDebug("Found {Count} resumable sessions for entity {EntityId}",
            resumableSessions.Count, entityId);

        return resumableSessions;
    }

    public async Task<ClaudeSession> StartSessionWithTerminationAsync(
        string entityId,
        string projectId,
        string workingDirectory,
        SessionMode mode,
        string model,
        bool terminateExisting,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default)
    {
        if (terminateExisting)
        {
            _logger.LogInformation("Terminating existing session for working directory {WorkingDirectory}", workingDirectory);
            await _agentExecutionService.TerminateCloneSessionAsync(workingDirectory, cancellationToken);
        }

        return await StartSessionAsync(entityId, projectId, workingDirectory, mode, model,
            systemPrompt, cancellationToken);
    }

    public async Task<ClaudeSession?> RestartSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var session = _sessionStore.GetById(sessionId);
        if (session == null)
        {
            _logger.LogWarning("RestartSessionAsync: Session {SessionId} not found", sessionId);
            return null;
        }

        _logger.LogInformation(
            "RestartSessionAsync: Restarting container for session {SessionId}, entity {EntityId}",
            sessionId, session.EntityId);

        var restartAgentId = _stateManager.GetAgentSessionId(sessionId);
        if (restartAgentId == null)
        {
            _logger.LogWarning("RestartSessionAsync: No agent session ID found for session {SessionId}", sessionId);
            return null;
        }

        await _hubContext.Clients.All.SendAsync("SessionContainerRestarting", sessionId, cancellationToken);

        var restartResult = await _agentExecutionService.RestartContainerAsync(restartAgentId, cancellationToken);
        if (restartResult == null)
        {
            _logger.LogError("RestartSessionAsync: Failed to restart container for session {SessionId}", sessionId);
            return null;
        }

        // Clear any existing CTS and agent session mapping
        await _stateManager.CancelAndRemoveCtsAsync(sessionId);
        _stateManager.TryRemoveAgentSessionId(sessionId, out _);

        // Clear pending question/answer state
        _stateManager.TryRemoveQuestionAnswerSource(sessionId);
        _stateManager.RemoveSessionToolUses(sessionId);
        _stateManager.RemoveTurnId(sessionId);

        // Update the session with the ConversationId for resumption
        session.ConversationId = restartResult.ConversationId;
        session.Status = ClaudeSessionStatus.WaitingForInput;
        session.ErrorMessage = null;

        _logger.LogInformation(
            "RestartSessionAsync: Container restarted. ConversationId={ConversationId} preserved for session {SessionId}",
            restartResult.ConversationId, sessionId);

        await _hubContext.Clients.All.SendAsync("SessionContainerRestarted", sessionId, session, cancellationToken);
        await _hubContext.BroadcastSessionStatusChanged(sessionId, session.Status);

        return session;
    }

    public async Task StopSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var session = _sessionStore.GetById(sessionId);
        if (session == null)
        {
            _logger.LogWarning("Attempted to stop non-existent session {SessionId}", sessionId);
            return;
        }

        _logger.LogInformation("Stopping session {SessionId}", sessionId);

        // Cancel any ongoing operations
        await _stateManager.CancelAndRemoveCtsAsync(sessionId);

        // Stop the agent execution service session
        if (_stateManager.TryRemoveAgentSessionId(sessionId, out var agentSessionId) && agentSessionId != null)
        {
            try
            {
                await _agentExecutionService.StopSessionAsync(agentSessionId, forceStopContainer: true, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error stopping agent session {AgentSessionId} for session {SessionId}",
                    agentSessionId, sessionId);
            }
        }

        // Remove remaining state for this session
        _stateManager.TryRemoveQuestionAnswerSource(sessionId);
        _stateManager.RemoveSessionToolUses(sessionId);
        _stateManager.RemoveTurnId(sessionId);

        // Update session status and remove from store
        session.Status = ClaudeSessionStatus.Stopped;
        _sessionStore.Remove(sessionId);

        // Notify clients
        await _hubContext.BroadcastSessionStopped(sessionId);
    }

    public async Task<int> StopAllSessionsForEntityAsync(string entityId, CancellationToken cancellationToken = default)
    {
        var sessions = _sessionStore.GetAllByEntityId(entityId);
        var stoppedCount = 0;

        _logger.LogInformation("Stopping all {Count} session(s) for entity {EntityId}", sessions.Count, entityId);

        foreach (var session in sessions)
        {
            try
            {
                await StopSessionAsync(session.Id, cancellationToken);
                stoppedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error stopping session {SessionId} for entity {EntityId}",
                    session.Id, entityId);
            }
        }

        try
        {
            var orphansCleanedUp = await _agentExecutionService.CleanupOrphanedContainersAsync(cancellationToken);
            if (orphansCleanedUp > 0)
            {
                _logger.LogInformation("Cleaned up {OrphanCount} orphaned container(s) after stopping sessions for entity {EntityId}",
                    orphansCleanedUp, entityId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error cleaning up orphaned containers for entity {EntityId}", entityId);
        }

        return stoppedCount;
    }

    public async Task InterruptSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var session = _sessionStore.GetById(sessionId);
        if (session == null)
        {
            _logger.LogWarning("Attempted to interrupt non-existent session {SessionId}", sessionId);
            return;
        }

        _logger.LogInformation("Interrupting session {SessionId}", sessionId);

        // Cancel any ongoing operations and replace with a fresh CTS
        await _stateManager.CancelAndRemoveCtsAsync(sessionId);
        _stateManager.GetOrCreateCts(sessionId);

        // Interrupt the agent execution service session (cancel but preserve session)
        var interruptAgentId = _stateManager.GetAgentSessionId(sessionId);
        if (interruptAgentId != null)
        {
            try
            {
                await _agentExecutionService.InterruptSessionAsync(interruptAgentId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error interrupting agent session {AgentSessionId} for session {SessionId}",
                    interruptAgentId, sessionId);
            }
        }

        // Clear any pending question answer sources (question is now moot)
        _stateManager.TryRemoveQuestionAnswerSource(sessionId);

        // Update session status to WaitingForInput
        session.Status = ClaudeSessionStatus.WaitingForInput;
        session.PendingQuestion = null;

        await _hubContext.BroadcastSessionStatusChanged(sessionId, ClaudeSessionStatus.WaitingForInput);
    }

    public Task ClearContextAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var session = _sessionStore.GetById(sessionId);
        if (session == null)
        {
            _logger.LogWarning("Attempted to clear context for non-existent session {SessionId}", sessionId);
            return Task.CompletedTask;
        }

        _logger.LogInformation("Clearing context for session {SessionId}", sessionId);

        session.ConversationId = null;
        _stateManager.TryRemoveAgentSessionId(sessionId, out _);
        session.ContextClearMarkers.Add(DateTime.UtcNow);

        return Task.CompletedTask;
    }

    public async Task<ClaudeSession> ClearContextAndStartNewAsync(
        string currentSessionId,
        string? initialPrompt = null,
        CancellationToken cancellationToken = default)
    {
        var currentSession = _sessionStore.GetById(currentSessionId);
        if (currentSession == null)
        {
            throw new KeyNotFoundException($"Session with ID {currentSessionId} not found");
        }

        _logger.LogInformation(
            "Clearing context for session {SessionId}, entity {EntityId}, and starting new session",
            currentSessionId, currentSession.EntityId);

        await StopSessionAsync(currentSessionId, cancellationToken);

        var newSession = await StartSessionAsync(
            currentSession.EntityId,
            currentSession.ProjectId,
            currentSession.WorkingDirectory,
            currentSession.Mode,
            currentSession.Model,
            currentSession.SystemPrompt,
            cancellationToken);

        await _hubContext.BroadcastSessionContextCleared(currentSessionId, newSession);

        _logger.LogInformation(
            "Context cleared. Old session: {OldSessionId}, New session: {NewSessionId}",
            currentSessionId, newSession.Id);

        if (!string.IsNullOrWhiteSpace(initialPrompt))
        {
            await _messageProcessing.Value.SendMessageAsync(newSession.Id, initialPrompt, newSession.Mode, null, cancellationToken);
        }

        return newSession;
    }

    public async Task SetSessionModeAsync(string sessionId, SessionMode mode, CancellationToken cancellationToken = default)
    {
        var session = _sessionStore.GetById(sessionId);
        if (session == null)
        {
            throw new KeyNotFoundException($"Session with ID {sessionId} not found");
        }

        if (session.Mode == mode)
        {
            _logger.LogDebug("Session {SessionId} mode unchanged at {Mode}", sessionId, mode);
            return;
        }

        session.Mode = mode;
        _sessionStore.Update(session);

        if (_agentExecutionService != null)
        {
            var success = await _agentExecutionService.SetSessionModeAsync(sessionId, mode, cancellationToken);
            if (!success)
            {
                _logger.LogWarning("Failed to set mode on worker for session {SessionId}", sessionId);
            }
        }

        await _metadataStore.SaveAsync(new SessionMetadata(
            session.Id, session.EntityId, session.ProjectId,
            session.WorkingDirectory, session.Mode, session.Model,
            session.SystemPrompt, session.CreatedAt
        ), cancellationToken);

        await _hubContext.BroadcastSessionModeModelChanged(sessionId, mode, session.Model);
        _logger.LogInformation("Session {SessionId} mode changed to {Mode}", sessionId, mode);
    }

    public async Task SetSessionModelAsync(string sessionId, string model, CancellationToken cancellationToken = default)
    {
        var session = _sessionStore.GetById(sessionId);
        if (session == null)
        {
            throw new KeyNotFoundException($"Session with ID {sessionId} not found");
        }

        if (session.Model == model)
        {
            _logger.LogDebug("Session {SessionId} model unchanged at {Model}", sessionId, model);
            return;
        }

        session.Model = model;
        _sessionStore.Update(session);

        if (_agentExecutionService != null)
        {
            var success = await _agentExecutionService.SetSessionModelAsync(sessionId, model, cancellationToken);
            if (!success)
            {
                _logger.LogWarning("Failed to set model on worker for session {SessionId}", sessionId);
            }
        }

        await _metadataStore.SaveAsync(new SessionMetadata(
            session.Id, session.EntityId, session.ProjectId,
            session.WorkingDirectory, session.Mode, session.Model,
            session.SystemPrompt, session.CreatedAt
        ), cancellationToken);

        await _hubContext.BroadcastSessionModeModelChanged(sessionId, session.Mode, model);
        _logger.LogInformation("Session {SessionId} model changed to {Model}", sessionId, model);
    }

    public async Task<AgentStartCheckResult> CheckCloneStateAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        var containerState = await _agentExecutionService.GetCloneContainerStateAsync(
            workingDirectory, cancellationToken);

        if (containerState == null)
        {
            return new AgentStartCheckResult(AgentStartAction.StartNew, null, null);
        }

        return containerState.SessionStatus switch
        {
            ClaudeSessionStatus.Starting or
            ClaudeSessionStatus.RunningHooks or
            ClaudeSessionStatus.Running =>
                new AgentStartCheckResult(AgentStartAction.NotifyActive, containerState,
                    "An agent is currently working on this clone."),

            ClaudeSessionStatus.WaitingForQuestionAnswer =>
                new AgentStartCheckResult(AgentStartAction.NotifyActive, containerState,
                    "An agent is waiting for you to answer a question."),

            ClaudeSessionStatus.WaitingForPlanExecution =>
                new AgentStartCheckResult(AgentStartAction.NotifyActive, containerState,
                    "An agent has a plan waiting for approval."),

            ClaudeSessionStatus.WaitingForInput =>
                new AgentStartCheckResult(AgentStartAction.ConfirmTerminate, containerState,
                    "An existing session is waiting for input. Would you like to terminate it?"),

            _ => new AgentStartCheckResult(AgentStartAction.ReuseContainer, containerState, null)
        };
    }

    public async Task<string> AcceptIssueChangesAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var session = _sessionStore.GetById(sessionId);
        if (session == null)
        {
            throw new KeyNotFoundException($"Session with ID {sessionId} not found");
        }

        try
        {
            var issueId = session.EntityId;
            await StopSessionAsync(sessionId, cancellationToken);
            return $"/projects/{session.ProjectId}/issues/{issueId}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error accepting issue changes for session {SessionId}", sessionId);
            throw;
        }
    }

    public async Task<string> CancelIssueChangesAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var session = _sessionStore.GetById(sessionId);
        if (session == null)
        {
            throw new KeyNotFoundException($"Session with ID {sessionId} not found");
        }

        try
        {
            var issueId = session.EntityId;
            await StopSessionAsync(sessionId, cancellationToken);
            return $"/projects/{session.ProjectId}/issues/{issueId}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error canceling issue changes for session {SessionId}", sessionId);
            throw;
        }
    }

    public async Task<IReadOnlyList<ClaudeMessage>> GetCachedMessagesAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        return await _messageCache.GetMessagesAsync(sessionId, cancellationToken);
    }

    public async Task<IReadOnlyList<SessionCacheSummary>> GetSessionHistoryAsync(
        string projectId,
        string entityId,
        CancellationToken cancellationToken = default)
    {
        var sessionIds = await _messageCache.GetSessionIdsForEntityAsync(projectId, entityId, cancellationToken);
        var summaries = new List<SessionCacheSummary>();

        foreach (var sessionId in sessionIds)
        {
            var summary = await _messageCache.GetSessionSummaryAsync(sessionId, cancellationToken);
            if (summary != null)
            {
                summaries.Add(summary);
            }
        }

        return summaries.OrderByDescending(s => s.LastMessageAt).ToList();
    }

    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("Disposing SessionLifecycleService");
        await _stateManager.CleanupAllAsync();
    }

    private static string? BuildSystemPrompt(string? basePrompt, IReadOnlyList<string> hookOutputs)
    {
        if (hookOutputs.Count == 0)
            return basePrompt;

        var parts = new List<string>();
        parts.AddRange(hookOutputs);

        if (!string.IsNullOrWhiteSpace(basePrompt))
            parts.Add(basePrompt);

        return string.Join("\n\n", parts);
    }
}
