using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Hubs;
using Homespun.Features.Fleece.Services;
using Homespun.Shared.Models.Sessions;
using Microsoft.AspNetCore.SignalR;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Handles sending messages and orchestrates the per-turn lifecycle (status transitions,
/// turn id bookkeeping, session result aggregation, workflow callbacks, error handling).
///
/// <para>
/// Content-block assembly, AG-UI broadcast, and message-cache persistence have moved to
/// <see cref="SessionEventIngestor"/> + <see cref="A2AEventStore"/>. This service no longer
/// constructs <c>ClaudeMessage</c> records; it simply drives the worker through its
/// session-start and send-message entry points and observes the control-plane primitives
/// (<see cref="SdkQuestionPendingMessage"/>, <see cref="SdkPlanPendingMessage"/>, <see cref="SdkResultMessage"/>)
/// that the worker still emits alongside A2A events.
/// </para>
/// </summary>
public class MessageProcessingService : IMessageProcessingService
{
    private readonly IClaudeSessionStore _sessionStore;
    private readonly ILogger<MessageProcessingService> _logger;
    private readonly IHubContext<ClaudeCodeHub> _hubContext;
    private readonly IAgentExecutionService _agentExecutionService;
    private readonly IFleeceIssueTransitionService _fleeceTransitionService;
    private readonly ISessionStateManager _stateManager;
    private readonly IToolInteractionService _toolInteraction;
    private readonly Lazy<Features.Workflows.Services.IWorkflowSessionCallback> _workflowSessionCallback;
    private readonly ISessionMetadataStore _metadataStore;

    public MessageProcessingService(
        IClaudeSessionStore sessionStore,
        ILogger<MessageProcessingService> logger,
        IHubContext<ClaudeCodeHub> hubContext,
        IAgentExecutionService agentExecutionService,
        IFleeceIssueTransitionService fleeceTransitionService,
        ISessionStateManager stateManager,
        IToolInteractionService toolInteraction,
        Lazy<Features.Workflows.Services.IWorkflowSessionCallback> workflowSessionCallback,
        ISessionMetadataStore metadataStore)
    {
        _sessionStore = sessionStore;
        _logger = logger;
        _hubContext = hubContext;
        _agentExecutionService = agentExecutionService;
        _fleeceTransitionService = fleeceTransitionService;
        _stateManager = stateManager;
        _toolInteraction = toolInteraction;
        _workflowSessionCallback = workflowSessionCallback;
        _metadataStore = metadataStore;
    }

    public Task SendMessageAsync(string sessionId, string message, CancellationToken cancellationToken = default)
    {
        return SendMessageAsync(sessionId, message, SessionMode.Build, null, cancellationToken);
    }

    public Task SendMessageAsync(string sessionId, string message, SessionMode mode, CancellationToken cancellationToken = default)
    {
        return SendMessageAsync(sessionId, message, mode, null, cancellationToken);
    }

    public async Task SendMessageAsync(string sessionId, string message, SessionMode mode, string? model, CancellationToken cancellationToken = default)
    {
        var session = _sessionStore.GetById(sessionId);
        if (session == null)
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        if (session.Status == ClaudeSessionStatus.Stopped || session.Status == ClaudeSessionStatus.Error)
        {
            throw new InvalidOperationException($"Session {sessionId} is not active (status: {session.Status})");
        }

        _logger.LogInformation("Sending message to session {SessionId} with mode {Mode}", sessionId, mode);

        var originalMode = session.Mode;
        var originalModel = session.Model;

        if (session.Mode != mode)
        {
            session.Mode = mode;
            _logger.LogInformation("Session {SessionId} mode changed from {OldMode} to {NewMode}",
                sessionId, originalMode, mode);
        }

        var effectiveModel = !string.IsNullOrEmpty(model) ? model : session.Model;
        if (!string.IsNullOrEmpty(model) && session.Model != model)
        {
            session.Model = model;
            _logger.LogInformation("Session {SessionId} model changed from {OldModel} to {NewModel}",
                sessionId, originalModel, model);
        }

        if (session.Mode != originalMode || session.Model != originalModel)
        {
            await _hubContext.BroadcastSessionModeModelChanged(sessionId, session.Mode, session.Model);
        }

        // Turn id MUST be set before the status broadcast — without it, the await on
        // BroadcastSessionStatusChanged can yield and let a superseded invocation's
        // post-loop guard see the old turn id and erroneously transition to WaitingForInput.
        var turnId = Guid.NewGuid();
        _stateManager.SetCurrentTurnId(sessionId, turnId);

        session.Status = ClaudeSessionStatus.Running;
        await _hubContext.BroadcastSessionStatusChanged(sessionId, session.Status);

        try
        {
            var cts = _stateManager.GetCts(sessionId);
            using var linkedCts = cts != null
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token)
                : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            IAsyncEnumerable<SdkMessage> messageStream;

            var agentSessionId = _stateManager.GetAgentSessionId(sessionId);
            if (agentSessionId == null)
            {
                var startRequest = new AgentStartRequest(
                    WorkingDirectory: session.WorkingDirectory,
                    Mode: session.Mode,
                    Model: effectiveModel,
                    Prompt: message,
                    SystemPrompt: session.SystemPrompt,
                    ResumeSessionId: session.ConversationId,
                    IssueId: session.EntityId,
                    ProjectId: session.ProjectId,
                    HomespunSessionId: sessionId
                );

                if (!string.IsNullOrEmpty(session.EntityId) && !string.IsNullOrEmpty(session.ProjectId))
                {
                    var transitionResult = await _fleeceTransitionService.TransitionToInProgressAsync(
                        session.ProjectId, session.EntityId);
                    if (transitionResult.Success)
                    {
                        _logger.LogInformation("Issue {IssueId} transitioned to Progress for session {SessionId}",
                            session.EntityId, sessionId);
                    }
                }

                messageStream = _agentExecutionService.StartSessionAsync(startRequest, linkedCts.Token);
            }
            else
            {
                var messageRequest = new AgentMessageRequest(
                    SessionId: agentSessionId,
                    Message: message,
                    Mode: mode,
                    Model: effectiveModel
                );
                messageStream = _agentExecutionService.SendMessageAsync(messageRequest, linkedCts.Token);
            }

            await foreach (var msg in messageStream.WithCancellation(linkedCts.Token))
            {
                await ProcessSdkMessageAsync(sessionId, session, msg, turnId, linkedCts.Token);

                if (msg is SdkSystemMessage sysMsg && sysMsg.Subtype == "session_started")
                {
                    _stateManager.SetAgentSessionId(sessionId, sysMsg.SessionId);
                    _logger.LogDebug("Stored agent session ID {AgentSessionId} for session {SessionId}",
                        sysMsg.SessionId, sessionId);
                }

                if (msg is SdkResultMessage)
                    break;
            }

            if (session.Status == ClaudeSessionStatus.Running &&
                _stateManager.IsTurnActive(sessionId, turnId))
            {
                session.Status = ClaudeSessionStatus.WaitingForInput;
                await _hubContext.BroadcastSessionStatusChanged(sessionId, session.Status);
            }
            _logger.LogInformation("Message processing completed for session {SessionId}, status: {Status}", sessionId, session.Status);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Message processing cancelled for session {SessionId}", sessionId);
            if (_sessionStore.GetById(sessionId) != null &&
                session.Status != ClaudeSessionStatus.Stopped &&
                _stateManager.IsTurnActive(sessionId, turnId))
            {
                session.Status = ClaudeSessionStatus.WaitingForInput;
                await _hubContext.BroadcastSessionStatusChanged(sessionId, session.Status);
            }
        }
        catch (Homespun.Features.ClaudeCode.Exceptions.SingleContainerBusyException busy)
        {
            _logger.LogError(
                "SingleContainer worker busy: requested session {RequestedSessionId} but {CurrentSessionId} is active",
                busy.RequestedSessionId, busy.CurrentSessionId);
            session.Status = ClaudeSessionStatus.Error;
            session.ErrorMessage = busy.Message;
            await _hubContext.BroadcastSessionError(
                sessionId,
                $"Dev worker is busy with session {busy.CurrentSessionId}. Stop it before starting a new one.",
                errorSubtype: "single_container_busy",
                isRecoverable: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message in session {SessionId}", sessionId);
            session.Status = ClaudeSessionStatus.Error;
            session.ErrorMessage = ex.Message;
            throw;
        }
    }

    private async Task ProcessSdkMessageAsync(
        string sessionId,
        ClaudeSession session,
        SdkMessage msg,
        Guid turnId,
        CancellationToken cancellationToken)
    {
        switch (msg)
        {
            case SdkSystemMessage sysMsg:
                _logger.LogDebug("System message: subtype={Subtype}, sessionId={SdkSessionId}",
                    sysMsg.Subtype, sysMsg.SessionId);
                break;

            case SdkQuestionPendingMessage questionMsg:
                await _toolInteraction.HandleQuestionPendingFromWorkerAsync(sessionId, session, questionMsg, turnId, cancellationToken);
                break;

            case SdkPlanPendingMessage planMsg:
                await _toolInteraction.HandlePlanPendingFromWorkerAsync(sessionId, session, planMsg, turnId, cancellationToken);
                break;

            case SdkResultMessage resultMsg:
                await ProcessResultMessageAsync(sessionId, session, resultMsg, cancellationToken);
                break;
        }
    }

    private async Task ProcessResultMessageAsync(
        string sessionId,
        ClaudeSession session,
        SdkResultMessage resultMsg,
        CancellationToken cancellationToken)
    {
        session.TotalCostUsd = resultMsg.TotalCostUsd;
        session.TotalDurationMs = resultMsg.DurationMs;

        var previousConversationId = session.ConversationId;
        session.ConversationId = resultMsg.SessionId;
        _logger.LogDebug("Stored ConversationId {ConversationId} for session resumption", resultMsg.SessionId);

        if (resultMsg.IsError)
        {
            var errorMessage = BuildErrorMessage(resultMsg);
            var isRecoverable = IsRecoverableError(resultMsg.Subtype);

            session.Status = ClaudeSessionStatus.Error;
            session.ErrorMessage = errorMessage;

            _logger.LogWarning(
                "Session {SessionId} encountered error: subtype={Subtype}, message={Message}, recoverable={Recoverable}",
                sessionId, resultMsg.Subtype, errorMessage, isRecoverable);

            await _hubContext.BroadcastSessionError(
                sessionId, errorMessage, resultMsg.Subtype, isRecoverable);
            await _hubContext.BroadcastSessionStatusChanged(sessionId, session.Status);

            // Run error is broadcast as a SessionEventEnvelope by SessionEventIngestor
            // when it observes the failed A2A StatusUpdate event.

            await _workflowSessionCallback.Value.HandleSessionFailedAsync(
                sessionId, errorMessage, CancellationToken.None);
        }

        if (resultMsg.SessionId != null && resultMsg.SessionId != previousConversationId)
        {
            var metadata = new SessionMetadata(
                SessionId: resultMsg.SessionId,
                EntityId: session.EntityId,
                ProjectId: session.ProjectId,
                WorkingDirectory: session.WorkingDirectory,
                Mode: session.Mode,
                Model: session.Model,
                SystemPrompt: session.SystemPrompt,
                CreatedAt: session.CreatedAt
            );
            await _metadataStore.SaveAsync(metadata, cancellationToken);
        }

        await _hubContext.BroadcastSessionResultReceived(sessionId, session.TotalCostUsd, resultMsg.DurationMs);

        if (!resultMsg.IsError)
        {
            // Run finished is broadcast as a SessionEventEnvelope by SessionEventIngestor
            // when it observes the completed A2A StatusUpdate.
            _stateManager.GetRunId(sessionId);
            await _workflowSessionCallback.Value.HandleSessionCompletedAsync(
                sessionId, CancellationToken.None);
        }
    }

    private static string BuildErrorMessage(SdkResultMessage resultMsg)
    {
        if (resultMsg.Errors is { Count: > 0 })
        {
            return string.Join("\n", resultMsg.Errors);
        }

        if (!string.IsNullOrEmpty(resultMsg.Result))
        {
            return resultMsg.Result;
        }

        return resultMsg.Subtype switch
        {
            "error_max_turns" => "The conversation reached its maximum number of turns. You can continue by sending another message.",
            "error_during_execution" => "An error occurred during execution. The session can be resumed.",
            "error_max_budget_usd" => "The session budget limit has been reached.",
            "error_max_structured_output_retries" => "Failed to generate the expected output format after multiple attempts.",
            _ => "An unexpected error occurred in the session."
        };
    }

    private static bool IsRecoverableError(string? subtype)
    {
        return subtype switch
        {
            "error_max_turns" => true,
            "error_during_execution" => true,
            "error_max_budget_usd" => false,
            "error_max_structured_output_retries" => true,
            _ => true
        };
    }
}
