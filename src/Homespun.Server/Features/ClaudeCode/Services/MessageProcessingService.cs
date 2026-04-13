using System.Collections.Concurrent;
using System.Text.Json;
using Homespun.Features.ClaudeCode.Hubs;
using Homespun.Features.Fleece.Services;
using Homespun.Shared.Models.Sessions;
using Microsoft.AspNetCore.SignalR;
using AGUIEvents = Homespun.Shared.Models.Sessions;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Handles sending messages, processing SDK message streams,
/// content block assembly, and message format conversions.
/// </summary>
public class MessageProcessingService : IMessageProcessingService
{
    private readonly IClaudeSessionStore _sessionStore;
    private readonly ILogger<MessageProcessingService> _logger;
    private readonly IHubContext<ClaudeCodeHub> _hubContext;
    private readonly IToolResultParser _toolResultParser;
    private readonly IMessageCacheStore _messageCache;
    private readonly IAgentExecutionService _agentExecutionService;
    private readonly IAGUIEventService _agUIEventService;
    private readonly IFleeceIssueTransitionService _fleeceTransitionService;
    private readonly ISessionStateManager _stateManager;
    private readonly IToolInteractionService _toolInteraction;
    private readonly Lazy<Features.Workflows.Services.IWorkflowSessionCallback> _workflowSessionCallback;
    private readonly ISessionMetadataStore _metadataStore;

    public MessageProcessingService(
        IClaudeSessionStore sessionStore,
        ILogger<MessageProcessingService> logger,
        IHubContext<ClaudeCodeHub> hubContext,
        IToolResultParser toolResultParser,
        IMessageCacheStore messageCache,
        IAgentExecutionService agentExecutionService,
        IAGUIEventService agUIEventService,
        IFleeceIssueTransitionService fleeceTransitionService,
        ISessionStateManager stateManager,
        IToolInteractionService toolInteraction,
        Lazy<Features.Workflows.Services.IWorkflowSessionCallback> workflowSessionCallback,
        ISessionMetadataStore metadataStore)
    {
        _sessionStore = sessionStore;
        _logger = logger;
        _hubContext = hubContext;
        _toolResultParser = toolResultParser;
        _messageCache = messageCache;
        _agentExecutionService = agentExecutionService;
        _agUIEventService = agUIEventService;
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

        var userMessage = new ClaudeMessage
        {
            SessionId = sessionId,
            Role = ClaudeMessageRole.User,
            Content = [new ClaudeMessageContent { Type = ClaudeContentType.Text, Text = message }]
        };
        session.Messages.Add(userMessage);
        // This MUST be set before the status broadcast to prevent a race condition:
        // without this, the await on BroadcastSessionStatusChanged can yield the thread,
        // allowing a superseded invocation's post-loop guard to see the old turn ID
        // and erroneously set WaitingForInput.
        var turnId = Guid.NewGuid();
        _stateManager.SetCurrentTurnId(sessionId, turnId);

        session.Status = ClaudeSessionStatus.Running;
        await _hubContext.BroadcastSessionStatusChanged(sessionId, session.Status);

        await _messageCache.AppendMessageAsync(sessionId, userMessage, cancellationToken);

        try
        {
            var cts = _stateManager.GetCts(sessionId);
            using var linkedCts = cts != null
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token)
                : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var currentAssistantMessage = new ClaudeMessage
            {
                SessionId = sessionId,
                Role = ClaudeMessageRole.Assistant,
                Content = []
            };
            var messageContext = new MessageProcessingContext
            {
                CurrentAssistantMessage = currentAssistantMessage,
                TurnId = turnId
            };

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
                    ProjectId: session.ProjectId
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
                await ProcessSdkMessageAsync(sessionId, session, messageContext, msg, linkedCts.Token);

                if (msg is SdkSystemMessage sysMsg && sysMsg.Subtype == "session_started")
                {
                    _stateManager.SetAgentSessionId(sessionId, sysMsg.SessionId);
                    _logger.LogDebug("Stored agent session ID {AgentSessionId} for session {SessionId}",
                        sysMsg.SessionId, sessionId);
                }

                if (msg is SdkResultMessage)
                    break;
            }

            if (messageContext.CurrentAssistantMessage.Content.Count > 0 &&
                !messageContext.HasCachedCurrentMessage)
            {
                session.Messages.Add(messageContext.CurrentAssistantMessage);
                await _messageCache.AppendMessageAsync(sessionId, messageContext.CurrentAssistantMessage, linkedCts.Token);
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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message in session {SessionId}", sessionId);
            session.Status = ClaudeSessionStatus.Error;
            session.ErrorMessage = ex.Message;
            throw;
        }
    }

    private class MessageProcessingContext
    {
        public ClaudeMessage CurrentAssistantMessage { get; set; } = null!;
        public bool HasCachedCurrentMessage { get; set; }
        public ContentBlockAssembler Assembler { get; } = new();
        public Guid TurnId { get; init; }
    }

    private async Task ProcessSdkMessageAsync(
        string sessionId,
        ClaudeSession session,
        MessageProcessingContext context,
        SdkMessage msg,
        CancellationToken cancellationToken)
    {
        switch (msg)
        {
            case SdkSystemMessage sysMsg:
                _logger.LogDebug("System message: subtype={Subtype}, sessionId={SdkSessionId}",
                    sysMsg.Subtype, sysMsg.SessionId);
                break;

            case SdkQuestionPendingMessage questionMsg:
                await _toolInteraction.HandleQuestionPendingFromWorkerAsync(sessionId, session, questionMsg, context.TurnId, cancellationToken);
                break;

            case SdkPlanPendingMessage planMsg:
                await _toolInteraction.HandlePlanPendingFromWorkerAsync(sessionId, session, planMsg, context.TurnId, cancellationToken);
                break;

            case SdkStreamEvent streamEvt:
                await ProcessStreamEventAsync(sessionId, session, context, streamEvt);
                break;

            case SdkAssistantMessage assistantMsg:
                await ProcessAssistantMessageAsync(sessionId, session, context, assistantMsg, cancellationToken);
                break;

            case SdkUserMessage userMsg:
                await ProcessUserMessageAsync(sessionId, session, context, userMsg, cancellationToken);
                break;

            case SdkResultMessage resultMsg:
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

                    var runErrorEvent = _agUIEventService.CreateRunError(errorMessage, resultMsg.Subtype);
                    await _hubContext.BroadcastAGUIRunError(sessionId, runErrorEvent);

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
                    var runId = _stateManager.GetRunId(sessionId) ?? Guid.NewGuid().ToString();
                    var runFinishedEvent = _agUIEventService.CreateRunFinished(sessionId, runId, resultMsg.Result);
                    await _hubContext.BroadcastAGUIRunFinished(sessionId, runFinishedEvent);

                    await _workflowSessionCallback.Value.HandleSessionCompletedAsync(
                        sessionId, CancellationToken.None);
                }
                break;
        }
    }

    private async Task ProcessStreamEventAsync(
        string sessionId,
        ClaudeSession session,
        MessageProcessingContext context,
        SdkStreamEvent streamEvt)
    {
        if (streamEvt.Event == null || !streamEvt.Event.HasValue) return;
        var evt = streamEvt.Event.Value;

        var eventType = evt.TryGetProperty("type", out var t) ? t.GetString() : null;

        switch (eventType)
        {
            case "content_block_start":
            {
                var index = evt.TryGetProperty("index", out var idx) ? idx.GetInt32() : 0;
                if (evt.TryGetProperty("content_block", out var contentBlock))
                {
                    context.Assembler.StartBlock(index, contentBlock);

                    var blockType = contentBlock.TryGetProperty("type", out var blockTypeVal) ? blockTypeVal.GetString() : null;
                    if (blockType == "tool_use")
                    {
                        var toolCallId = contentBlock.TryGetProperty("id", out var idVal) ? idVal.GetString() : Guid.NewGuid().ToString();
                        var toolName = contentBlock.TryGetProperty("name", out var nameVal) ? nameVal.GetString() : "unknown";
                        var toolStartEvent = AGUIEvents.AGUIEventFactory.CreateToolCallStart(
                            toolCallId!, toolName!, context.CurrentAssistantMessage?.Id);
                        _ = _hubContext.BroadcastAGUIToolCallStart(sessionId, toolStartEvent);
                    }
                    else if (blockType == "text" || blockType == "thinking")
                    {
                        var msgId = context.CurrentAssistantMessage?.Id ?? Guid.NewGuid().ToString();
                        var textStartEvent = AGUIEvents.AGUIEventFactory.CreateTextMessageStart(msgId, "assistant");
                        _ = _hubContext.BroadcastAGUITextMessageStart(sessionId, textStartEvent);
                    }
                }
                break;
            }

            case "content_block_delta":
            {
                var index = evt.TryGetProperty("index", out var idx) ? idx.GetInt32() : 0;
                if (evt.TryGetProperty("delta", out var delta))
                {
                    context.Assembler.ApplyDelta(index, delta);

                    var block = index < context.Assembler.Blocks.Count ? context.Assembler.Blocks[index] : null;
                    if (block != null)
                    {
                        var deltaType = delta.TryGetProperty("type", out var dt) ? dt.GetString() : null;
                        switch (deltaType)
                        {
                            case "text_delta":
                            case "thinking_delta":
                                var textDelta = delta.TryGetProperty("text", out var txt) ? txt.GetString() :
                                               (delta.TryGetProperty("thinking", out var th) ? th.GetString() : null);
                                if (!string.IsNullOrEmpty(textDelta))
                                {
                                    var messageId = context.CurrentAssistantMessage?.Id ?? Guid.NewGuid().ToString();
                                    var textContentEvent = AGUIEvents.AGUIEventFactory.CreateTextMessageContent(messageId, textDelta);
                                    _ = _hubContext.BroadcastAGUITextMessageContent(sessionId, textContentEvent);
                                }
                                break;
                            case "input_json_delta":
                                var jsonDelta = delta.TryGetProperty("partial_json", out var pj) ? pj.GetString() : null;
                                if (!string.IsNullOrEmpty(jsonDelta) && block.ToolUseId != null)
                                {
                                    var toolArgsEvent = AGUIEvents.AGUIEventFactory.CreateToolCallArgs(block.ToolUseId, jsonDelta);
                                    _ = _hubContext.BroadcastAGUIToolCallArgs(sessionId, toolArgsEvent);
                                }
                                break;
                        }
                    }
                }
                break;
            }

            case "content_block_stop":
            {
                var index = evt.TryGetProperty("index", out var idx) ? idx.GetInt32() : 0;
                context.Assembler.StopBlock(index);

                if (index < context.Assembler.Blocks.Count)
                {
                    var block = context.Assembler.Blocks[index];
                    var content = ConvertBlockStateToContent(sessionId, block);
                    content.IsStreaming = false;
                    context.CurrentAssistantMessage.Content.Add(content);

                    if (block.Type == "tool_use" && block.ToolUseId != null)
                    {
                        var toolEndEvent = AGUIEvents.AGUIEventFactory.CreateToolCallEnd(block.ToolUseId);
                        _ = _hubContext.BroadcastAGUIToolCallEnd(sessionId, toolEndEvent);
                    }
                    else if (block.Type == "text" || block.Type == "thinking")
                    {
                        var messageId = context.CurrentAssistantMessage?.Id ?? Guid.NewGuid().ToString();
                        var textEndEvent = AGUIEvents.AGUIEventFactory.CreateTextMessageEnd(messageId);
                        _ = _hubContext.BroadcastAGUITextMessageEnd(sessionId, textEndEvent);
                    }

                    if (content.Type == ClaudeContentType.ToolUse &&
                        content.ToolName == "AskUserQuestion" &&
                        !string.IsNullOrEmpty(content.ToolInput))
                    {
                        await _toolInteraction.HandleAskUserQuestionTool(sessionId, session, content, CancellationToken.None);
                    }

                    if (content.ToolName?.Equals("ExitPlanMode", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        await _toolInteraction.HandleExitPlanModeCompletedAsync(sessionId, session, content, CancellationToken.None);
                    }

                    if (content.Type == ClaudeContentType.ToolUse &&
                        content.ToolName == "workflow_signal" &&
                        !string.IsNullOrEmpty(content.ToolInput))
                    {
                        await _toolInteraction.HandleWorkflowSignalToolAsync(sessionId, content, CancellationToken.None);
                    }

                    if (content.ToolName?.Equals("Write", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        _toolInteraction.TryCaptureWrittenPlanContent(session, content);
                    }
                }
                break;
            }
        }
    }

    private async Task ProcessAssistantMessageAsync(
        string sessionId,
        ClaudeSession session,
        MessageProcessingContext context,
        SdkAssistantMessage assistantMsg,
        CancellationToken cancellationToken)
    {
        if (context.CurrentAssistantMessage.Content.Count == 0 && assistantMsg.Message.Content.Count > 0)
        {
            foreach (var block in assistantMsg.Message.Content)
            {
                var content = ConvertSdkContentBlock(sessionId, block);
                context.CurrentAssistantMessage.Content.Add(content);

                // Broadcast AG-UI events for complete assistant message blocks
                // (streaming path handles its own broadcasting via ProcessStreamEventAsync)
                if (content.Type == ClaudeContentType.Text || content.Type == ClaudeContentType.Thinking)
                {
                    var msgId = context.CurrentAssistantMessage.Id;
                    _ = _hubContext.BroadcastAGUITextMessageStart(sessionId,
                        AGUIEvents.AGUIEventFactory.CreateTextMessageStart(msgId, "assistant"));
                    if (!string.IsNullOrEmpty(content.Text))
                    {
                        _ = _hubContext.BroadcastAGUITextMessageContent(sessionId,
                            AGUIEvents.AGUIEventFactory.CreateTextMessageContent(msgId, content.Text));
                    }
                    _ = _hubContext.BroadcastAGUITextMessageEnd(sessionId,
                        AGUIEvents.AGUIEventFactory.CreateTextMessageEnd(msgId));
                }
                else if (content.Type == ClaudeContentType.ToolUse && content.ToolUseId != null)
                {
                    _ = _hubContext.BroadcastAGUIToolCallStart(sessionId,
                        AGUIEvents.AGUIEventFactory.CreateToolCallStart(content.ToolUseId, content.ToolName ?? "unknown", context.CurrentAssistantMessage.Id));
                    if (!string.IsNullOrEmpty(content.ToolInput))
                    {
                        _ = _hubContext.BroadcastAGUIToolCallArgs(sessionId,
                            AGUIEvents.AGUIEventFactory.CreateToolCallArgs(content.ToolUseId, content.ToolInput));
                    }
                    _ = _hubContext.BroadcastAGUIToolCallEnd(sessionId,
                        AGUIEvents.AGUIEventFactory.CreateToolCallEnd(content.ToolUseId));
                }

                if (content.Type == ClaudeContentType.ToolUse &&
                    content.ToolName == "AskUserQuestion" &&
                    !string.IsNullOrEmpty(content.ToolInput))
                {
                    await _toolInteraction.HandleAskUserQuestionTool(sessionId, session, content, cancellationToken);
                }

                if (content.ToolName?.Equals("ExitPlanMode", StringComparison.OrdinalIgnoreCase) == true)
                {
                    await _toolInteraction.HandleExitPlanModeCompletedAsync(sessionId, session, content, cancellationToken);
                }

                if (content.Type == ClaudeContentType.ToolUse &&
                    content.ToolName == "workflow_signal" &&
                    !string.IsNullOrEmpty(content.ToolInput))
                {
                    await _toolInteraction.HandleWorkflowSignalToolAsync(sessionId, content, cancellationToken);
                }

                if (content.ToolName?.Equals("Write", StringComparison.OrdinalIgnoreCase) == true)
                {
                    _toolInteraction.TryCaptureWrittenPlanContent(session, content);
                }
            }
            _logger.LogDebug("Populated assistant message from SdkAssistantMessage ({Count} blocks)",
                assistantMsg.Message.Content.Count);
        }

        if (context.CurrentAssistantMessage.Content.Count > 0 && !context.HasCachedCurrentMessage)
        {
            session.Messages.Add(context.CurrentAssistantMessage);
            await _messageCache.AppendMessageAsync(sessionId, context.CurrentAssistantMessage, cancellationToken);
            context.HasCachedCurrentMessage = true;
        }

        context.Assembler.Clear();
    }

    private async Task ProcessUserMessageAsync(
        string sessionId,
        ClaudeSession session,
        MessageProcessingContext context,
        SdkUserMessage userMsg,
        CancellationToken cancellationToken)
    {
        var toolResultContents = new List<ClaudeMessageContent>();

        foreach (var block in userMsg.Message.Content)
        {
            if (block is SdkToolResultBlock toolResult)
            {
                var content = ConvertSdkToolResult(sessionId, toolResult);
                toolResultContents.Add(content);
            }
        }

        if (toolResultContents.Count > 0)
        {
            var toolResultMessage = new ClaudeMessage
            {
                SessionId = sessionId,
                Role = ClaudeMessageRole.User,
                Content = toolResultContents
            };
            session.Messages.Add(toolResultMessage);
            await _messageCache.AppendMessageAsync(sessionId, toolResultMessage, cancellationToken);

            foreach (var toolResult in toolResultContents)
            {
                if (!string.IsNullOrEmpty(toolResult.ToolUseId))
                {
                    var toolResultEvent = AGUIEvents.AGUIEventFactory.CreateToolCallResult(
                        toolResult.ToolUseId,
                        toolResult.ToolResult ?? "",
                        toolResultMessage.Id);
                    _ = _hubContext.BroadcastAGUIToolCallResult(sessionId, toolResultEvent);
                }
            }

            foreach (var toolResult in toolResultContents)
            {
                if (toolResult.ToolName?.Equals("Write", StringComparison.OrdinalIgnoreCase) == true)
                {
                    _toolInteraction.TryCaptureWrittenPlanContentFromResult(session, toolResult);
                }

                if (toolResult.ToolName?.Equals("ExitPlanMode", StringComparison.OrdinalIgnoreCase) == true)
                {
                    var exitPlanBlock = new ClaudeMessageContent
                    {
                        Type = ClaudeContentType.ToolUse,
                        ToolName = "ExitPlanMode"
                    };
                    await _toolInteraction.HandleExitPlanModeCompletedAsync(sessionId, session, exitPlanBlock, cancellationToken);
                }
            }

            context.CurrentAssistantMessage = new ClaudeMessage
            {
                SessionId = sessionId,
                Role = ClaudeMessageRole.Assistant,
                Content = []
            };
            context.HasCachedCurrentMessage = false;
            context.Assembler.Clear();
        }
    }

    private ClaudeMessageContent ConvertBlockStateToContent(string sessionId, ContentBlockState block)
    {
        return block.Type switch
        {
            "text" => new ClaudeMessageContent
            {
                Type = ClaudeContentType.Text,
                Text = block.Text,
                Index = block.Index
            },
            "thinking" => new ClaudeMessageContent
            {
                Type = ClaudeContentType.Thinking,
                Text = block.Thinking,
                Index = block.Index
            },
            "tool_use" => new ClaudeMessageContent
            {
                Type = ClaudeContentType.ToolUse,
                ToolUseId = block.ToolUseId,
                ToolName = block.ToolName,
                ToolInput = block.PartialJson,
                Index = block.Index
            },
            _ => new ClaudeMessageContent
            {
                Type = ClaudeContentType.Text,
                Text = block.Text,
                Index = block.Index
            }
        };
    }

    private ClaudeMessageContent ConvertSdkContentBlock(string sessionId, SdkContentBlock block)
    {
        switch (block)
        {
            case SdkTextBlock textBlock:
                return new ClaudeMessageContent
                {
                    Type = ClaudeContentType.Text,
                    Text = textBlock.Text
                };

            case SdkThinkingBlock thinkingBlock:
                return new ClaudeMessageContent
                {
                    Type = ClaudeContentType.Thinking,
                    Text = thinkingBlock.Thinking
                };

            case SdkToolUseBlock toolUseBlock:
            {
                var sessionToolUses = _stateManager.GetOrCreateSessionToolUses(sessionId);
                sessionToolUses[toolUseBlock.Id] = toolUseBlock.Name;

                return new ClaudeMessageContent
                {
                    Type = ClaudeContentType.ToolUse,
                    ToolUseId = toolUseBlock.Id,
                    ToolName = toolUseBlock.Name,
                    ToolInput = toolUseBlock.Input.ValueKind != JsonValueKind.Undefined
                        ? toolUseBlock.Input.GetRawText()
                        : null
                };
            }

            case SdkToolResultBlock toolResultBlock:
                return ConvertSdkToolResult(sessionId, toolResultBlock);

            default:
                return new ClaudeMessageContent { Type = ClaudeContentType.Text };
        }
    }

    private ClaudeMessageContent ConvertSdkToolResult(string sessionId, SdkToolResultBlock toolResult)
    {
        string? toolName = null;
        var toolUses = _stateManager.GetOrCreateSessionToolUses(sessionId);
        toolUses.TryGetValue(toolResult.ToolUseId, out toolName);

        var contentText = toolResult.Content.ValueKind == JsonValueKind.String
            ? toolResult.Content.GetString()
            : toolResult.Content.ValueKind != JsonValueKind.Undefined
                ? toolResult.Content.GetRawText()
                : null;

        var content = new ClaudeMessageContent
        {
            Type = ClaudeContentType.ToolResult,
            ToolUseId = toolResult.ToolUseId,
            ToolName = toolName,
            Text = contentText,
            ToolSuccess = toolResult.IsError == true ? false : true
        };

        if (!string.IsNullOrEmpty(toolName))
        {
            content.ParsedToolResult = _toolResultParser.Parse(toolName, contentText, toolResult.IsError == true);
        }

        return content;
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
