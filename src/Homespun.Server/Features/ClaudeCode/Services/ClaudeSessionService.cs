using System.Collections.Concurrent;
using System.Text.Json;
using Homespun.ClaudeAgentSdk;
using Homespun.Features.ClaudeCode.Hubs;
using Microsoft.AspNetCore.SignalR;
using SharedPermissionMode = Homespun.Shared.Models.Sessions.PermissionMode;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Tracks content blocks being assembled from stream events during a single message turn.
/// </summary>
internal class ContentBlockAssembler
{
    private readonly List<ContentBlockState> _blocks = new();
    private readonly Dictionary<string, string> _toolUseNames = new();

    public IReadOnlyList<ContentBlockState> Blocks => _blocks;

    public void StartBlock(int index, JsonElement contentBlock)
    {
        var type = contentBlock.TryGetProperty("type", out var t) ? t.GetString() : null;
        var state = new ContentBlockState { Index = index, Type = type };

        if (type == "tool_use")
        {
            state.ToolUseId = contentBlock.TryGetProperty("id", out var id) ? id.GetString() : null;
            state.ToolName = contentBlock.TryGetProperty("name", out var name) ? name.GetString() : null;
            if (state.ToolUseId != null && state.ToolName != null)
            {
                _toolUseNames[state.ToolUseId] = state.ToolName;
            }
        }

        // Ensure the list is large enough
        while (_blocks.Count <= index) _blocks.Add(new ContentBlockState());
        _blocks[index] = state;
    }

    public void ApplyDelta(int index, JsonElement delta)
    {
        if (index < 0 || index >= _blocks.Count) return;
        var block = _blocks[index];
        var deltaType = delta.TryGetProperty("type", out var t) ? t.GetString() : null;

        switch (deltaType)
        {
            case "text_delta":
                block.Text += delta.TryGetProperty("text", out var text) ? text.GetString() : null;
                break;
            case "thinking_delta":
                block.Thinking += delta.TryGetProperty("thinking", out var thinking) ? thinking.GetString() : null;
                break;
            case "input_json_delta":
                block.PartialJson += delta.TryGetProperty("partial_json", out var pj) ? pj.GetString() : null;
                break;
        }
    }

    public void StopBlock(int index)
    {
        if (index < 0 || index >= _blocks.Count) return;
        _blocks[index].IsComplete = true;
    }

    public string? GetToolName(string toolUseId) =>
        _toolUseNames.TryGetValue(toolUseId, out var name) ? name : null;

    public void Clear()
    {
        _blocks.Clear();
    }
}

internal class ContentBlockState
{
    public int Index { get; set; }
    public string? Type { get; set; }
    public string? Text { get; set; }
    public string? Thinking { get; set; }
    public string? ToolUseId { get; set; }
    public string? ToolName { get; set; }
    public string? PartialJson { get; set; }
    public bool IsComplete { get; set; }
}

/// <summary>
/// Service for managing Claude Code sessions using the ClaudeAgentSdk.
/// </summary>
public class ClaudeSessionService : IClaudeSessionService, IAsyncDisposable
{
    private readonly IClaudeSessionStore _sessionStore;
    private readonly SessionOptionsFactory _optionsFactory;
    private readonly ILogger<ClaudeSessionService> _logger;
    private readonly IHubContext<ClaudeCodeHub> _hubContext;
    private readonly IClaudeSessionDiscovery _sessionDiscovery;
    private readonly ISessionMetadataStore _metadataStore;
    private readonly IToolResultParser _toolResultParser;
    private readonly IHooksService _hooksService;
    private readonly IMessageCacheStore _messageCache;
    private readonly IAgentExecutionService _agentExecutionService;
    private readonly ConcurrentDictionary<string, ClaudeAgentOptions> _sessionOptions = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _sessionCts = new();

    /// <summary>
    /// Maps session ID -> persistent SDK client for streaming mode interactions.
    /// </summary>
    private readonly ConcurrentDictionary<string, ClaudeSdkClient> _sessionClients = new();

    /// <summary>
    /// Maps session ID -> task source for signaling when question answers are received.
    /// </summary>
    private readonly ConcurrentDictionary<string, TaskCompletionSource<Dictionary<string, string>>> _questionAnswerSources = new();

    /// <summary>
    /// Maps session ID -> (tool use ID -> tool name) for linking tool results to their tool uses.
    /// </summary>
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _sessionToolUses = new();

    /// <summary>
    /// Maps our session ID -> agent execution service session ID.
    /// </summary>
    private readonly ConcurrentDictionary<string, string> _agentSessionIds = new();


    public ClaudeSessionService(
        IClaudeSessionStore sessionStore,
        SessionOptionsFactory optionsFactory,
        ILogger<ClaudeSessionService> logger,
        IHubContext<ClaudeCodeHub> hubContext,
        IClaudeSessionDiscovery sessionDiscovery,
        ISessionMetadataStore metadataStore,
        IToolResultParser toolResultParser,
        IHooksService hooksService,
        IMessageCacheStore messageCache,
        IAgentExecutionService agentExecutionService)
    {
        _sessionStore = sessionStore;
        _optionsFactory = optionsFactory;
        _logger = logger;
        _hubContext = hubContext;
        _sessionDiscovery = sessionDiscovery;
        _metadataStore = metadataStore;
        _toolResultParser = toolResultParser;
        _hooksService = hooksService;
        _messageCache = messageCache;
        _agentExecutionService = agentExecutionService;
    }

    /// <inheritdoc />
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
        var cts = new CancellationTokenSource();
        _sessionCts[sessionId] = cts;

        // Execute SessionStart hooks FIRST to collect output for system prompt
        await _hubContext.BroadcastSessionStatusChanged(sessionId, session.Status);

        var hookOutputs = new List<string>();
        try
        {
            var hookResults = await _hooksService.ExecuteSessionStartHooksAsync(
                sessionId, workingDirectory, cancellationToken);

            foreach (var result in hookResults)
            {
                await _hubContext.BroadcastHookExecuted(sessionId, result);

                // Collect output from successful hooks
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
            // Log but don't fail session start - hooks are auxiliary
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

        // Create and store the SDK options with combined system prompt
        var options = _optionsFactory.Create(mode, workingDirectory, model, combinedSystemPrompt);
        options.PermissionMode = PermissionMode.BypassPermissions; // Allow all tools without prompting
        options.IncludePartialMessages = true; // Enable streaming with --print mode
        _sessionOptions[sessionId] = options;

        session.Status = ClaudeSessionStatus.WaitingForInput;
        _logger.LogInformation("Session {SessionId} initialized and ready", sessionId);

        // Save metadata for future resumption (use combined system prompt)
        var metadata = new SessionMetadata(
            SessionId: session.ConversationId ?? sessionId, // Will be updated when we get the real ConversationId
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
            sessionId,
            entityId,
            projectId,
            mode,
            model,
            cancellationToken);

        // Notify clients about the new session
        await _hubContext.BroadcastSessionStarted(session);

        return session;
    }

    /// <inheritdoc />
    public async Task<ClaudeSession> ResumeSessionAsync(
        string sessionId,
        string entityId,
        string projectId,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Resuming session {ClaudeSessionId} for entity {EntityId}", sessionId, entityId);

        // Try to get our saved metadata for this session
        var metadata = await _metadataStore.GetBySessionIdAsync(sessionId, cancellationToken);

        // Create a new ClaudeSession using the discovered session ID as ConversationId
        var newSessionId = Guid.NewGuid().ToString();
        var session = new ClaudeSession
        {
            Id = newSessionId,
            EntityId = entityId,
            ProjectId = projectId,
            WorkingDirectory = workingDirectory,
            ConversationId = sessionId, // The Claude CLI session ID for --resume
            Model = metadata?.Model ?? "sonnet",
            Mode = metadata?.Mode ?? SessionMode.Build,
            SystemPrompt = metadata?.SystemPrompt,
            Status = ClaudeSessionStatus.Running,
            CreatedAt = DateTime.UtcNow,
            LastActivityAt = DateTime.UtcNow,
            Messages = []
        };

        // Create SDK options with Resume flag
        var options = _optionsFactory.Create(
            session.Mode,
            workingDirectory,
            session.Model,
            session.SystemPrompt);
        options.PermissionMode = PermissionMode.BypassPermissions;
        options.IncludePartialMessages = true;
        options.Resume = sessionId; // THIS IS THE KEY - tells Claude CLI to resume

        _sessionOptions[newSessionId] = options;
        _sessionCts[newSessionId] = new CancellationTokenSource();
        _sessionStore.Add(session);

        _logger.LogInformation("Resumed session {NewSessionId} with ConversationId {ConversationId}",
            newSessionId, sessionId);

        await _hubContext.BroadcastSessionStarted(session);
        return session;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ResumableSession>> GetResumableSessionsAsync(
        string entityId,
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Discovering resumable sessions for entity {EntityId} at {WorkingDirectory}",
            entityId, workingDirectory);

        // Discover sessions from Claude's storage
        var discoveredSessions = await _sessionDiscovery.DiscoverSessionsAsync(workingDirectory, cancellationToken);

        var resumableSessions = new List<ResumableSession>();
        foreach (var discovered in discoveredSessions)
        {
            // Try to get our metadata for this session
            var metadata = await _metadataStore.GetBySessionIdAsync(discovered.SessionId, cancellationToken);

            // Get message count if possible
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

    /// <inheritdoc />
    public Task SendMessageAsync(string sessionId, string message, CancellationToken cancellationToken = default)
    {
        return SendMessageAsync(sessionId, message, PermissionMode.BypassPermissions, null, cancellationToken);
    }

    /// <inheritdoc />
    public Task SendMessageAsync(string sessionId, string message, PermissionMode permissionMode, CancellationToken cancellationToken = default)
    {
        return SendMessageAsync(sessionId, message, permissionMode, null, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SendMessageAsync(string sessionId, string message, PermissionMode permissionMode, string? model, CancellationToken cancellationToken = default)
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

        if (!_sessionOptions.TryGetValue(sessionId, out var baseOptions))
        {
            throw new InvalidOperationException($"No options found for session {sessionId}");
        }

        _logger.LogInformation("Sending message to session {SessionId} with permission mode {PermissionMode}", sessionId, permissionMode);

        // Add user message to session
        var userMessage = new ClaudeMessage
        {
            SessionId = sessionId,
            Role = ClaudeMessageRole.User,
            Content = [new ClaudeMessageContent { Type = ClaudeContentType.Text, Text = message }]
        };
        session.Messages.Add(userMessage);
        session.Status = ClaudeSessionStatus.Running;
        await _hubContext.BroadcastSessionStatusChanged(sessionId, session.Status);

        // Cache the user message
        await _messageCache.AppendMessageAsync(sessionId, userMessage, cancellationToken);

        // Notify clients about the user message
        await _hubContext.BroadcastMessageReceived(sessionId, userMessage);

        try
        {
            // Get the combined cancellation token
            var cts = _sessionCts.GetValueOrDefault(sessionId);
            using var linkedCts = cts != null
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, cts.Token)
                : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            var effectiveModel = !string.IsNullOrEmpty(model) ? model : (baseOptions.Model ?? "sonnet");

            // Track the current assistant message being built
            var currentAssistantMessage = new ClaudeMessage
            {
                SessionId = sessionId,
                Role = ClaudeMessageRole.Assistant,
                Content = []
            };
            var messageContext = new MessageProcessingContext
            {
                CurrentAssistantMessage = currentAssistantMessage
            };

            // Determine if we need to start a new agent session or send a message to existing one
            IAsyncEnumerable<SdkMessage> messageStream;

            if (!_agentSessionIds.TryGetValue(sessionId, out var agentSessionId))
            {
                // First message - start a new agent session
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
                messageStream = _agentExecutionService.StartSessionAsync(startRequest, linkedCts.Token);
            }
            else
            {
                // Subsequent message - send to existing session with permission mode
                var messageRequest = new AgentMessageRequest(
                    SessionId: agentSessionId,
                    Message: message,
                    PermissionMode: MapToSharedPermissionMode(permissionMode),
                    Model: effectiveModel
                );
                messageStream = _agentExecutionService.SendMessageAsync(messageRequest, linkedCts.Token);
            }

            // Process SDK messages from the agent execution service
            await foreach (var msg in messageStream.WithCancellation(linkedCts.Token))
            {
                await ProcessSdkMessageAsync(sessionId, session, messageContext, msg, linkedCts.Token);

                // Store the agent session ID when we receive it from system message
                if (msg is SdkSystemMessage sysMsg && sysMsg.Subtype == "session_started")
                {
                    _agentSessionIds[sessionId] = sysMsg.SessionId;
                    _logger.LogDebug("Stored agent session ID {AgentSessionId} for session {SessionId}",
                        sysMsg.SessionId, sessionId);
                }

                // Stop processing after result received
                if (msg is SdkResultMessage)
                    break;
            }

            // Cache any remaining assistant message content
            if (messageContext.CurrentAssistantMessage.Content.Count > 0 &&
                !messageContext.HasCachedCurrentMessage)
            {
                session.Messages.Add(messageContext.CurrentAssistantMessage);
                await _messageCache.AppendMessageAsync(sessionId, messageContext.CurrentAssistantMessage, linkedCts.Token);
            }

            // Only set to WaitingForInput if we're not in a special waiting state
            if (session.Status != ClaudeSessionStatus.WaitingForQuestionAnswer &&
                session.Status != ClaudeSessionStatus.WaitingForPlanExecution)
            {
                session.Status = ClaudeSessionStatus.WaitingForInput;
                await _hubContext.BroadcastSessionStatusChanged(sessionId, session.Status);
            }
            _logger.LogInformation("Message processing completed for session {SessionId}, status: {Status}", sessionId, session.Status);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Message processing cancelled for session {SessionId}", sessionId);
            // Don't unconditionally set Stopped here.
            // If this was an interrupt, InterruptSessionAsync already set WaitingForInput.
            // If this was a full stop, StopSessionAsync will set Stopped and remove from store.
            // Only set WaitingForInput as a fallback if the session is still active.
            if (_sessionStore.GetById(sessionId) != null && session.Status != ClaudeSessionStatus.Stopped)
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

    /// <summary>
    /// Context for tracking message processing state across multiple SDK messages.
    /// </summary>
    private class MessageProcessingContext
    {
        public ClaudeMessage CurrentAssistantMessage { get; set; } = null!;
        public bool HasCachedCurrentMessage { get; set; }
        public ContentBlockAssembler Assembler { get; } = new();
    }

    /// <summary>
    /// Processes SDK messages from IAgentExecutionService and converts them to the internal format.
    /// Handles content block assembly from stream events and converts SdkMessage types to ClaudeMessage/ClaudeMessageContent.
    /// </summary>
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
                await HandleQuestionPendingFromWorkerAsync(sessionId, session, questionMsg, cancellationToken);
                break;

            case SdkPlanPendingMessage planMsg:
                await HandlePlanPendingFromWorkerAsync(sessionId, session, planMsg, cancellationToken);
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

                // Update metadata with the actual Claude session ID if it changed
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
                break;
        }
    }

    /// <summary>
    /// Processes stream events (content_block_start/delta/stop) for real-time content assembly.
    /// </summary>
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
                }
                break;
            }

            case "content_block_delta":
            {
                var index = evt.TryGetProperty("index", out var idx) ? idx.GetInt32() : 0;
                if (evt.TryGetProperty("delta", out var delta))
                {
                    context.Assembler.ApplyDelta(index, delta);

                    // Broadcast streaming content for real-time UI updates
                    var block = index < context.Assembler.Blocks.Count ? context.Assembler.Blocks[index] : null;
                    if (block != null)
                    {
                        var streamingContent = ConvertBlockStateToContent(sessionId, block);
                        streamingContent.IsStreaming = true;
                        _ = _hubContext.BroadcastContentBlockReceived(sessionId, streamingContent);
                    }
                }
                break;
            }

            case "content_block_stop":
            {
                var index = evt.TryGetProperty("index", out var idx) ? idx.GetInt32() : 0;
                context.Assembler.StopBlock(index);

                // Emit a finalized content block
                if (index < context.Assembler.Blocks.Count)
                {
                    var block = context.Assembler.Blocks[index];
                    var content = ConvertBlockStateToContent(sessionId, block);
                    content.IsStreaming = false;
                    context.CurrentAssistantMessage.Content.Add(content);

                    _ = _hubContext.BroadcastContentBlockReceived(sessionId, content);

                    // Check for AskUserQuestion tool
                    if (content.Type == ClaudeContentType.ToolUse &&
                        content.ToolName == "AskUserQuestion" &&
                        !string.IsNullOrEmpty(content.ToolInput))
                    {
                        await HandleAskUserQuestionTool(sessionId, session, content, CancellationToken.None);
                    }

                    // Check for ExitPlanMode
                    if (content.ToolName?.Equals("ExitPlanMode", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        await HandleExitPlanModeCompletedAsync(sessionId, session, content, CancellationToken.None);
                    }

                    // Capture plan content from Write tool
                    if (content.ToolName?.Equals("Write", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        TryCaptureWrittenPlanContent(session, content);
                    }
                }
                break;
            }
        }
    }

    /// <summary>
    /// Processes an SdkAssistantMessage — flushes assembled content or converts content blocks directly.
    /// </summary>
    private async Task ProcessAssistantMessageAsync(
        string sessionId,
        ClaudeSession session,
        MessageProcessingContext context,
        SdkAssistantMessage assistantMsg,
        CancellationToken cancellationToken)
    {
        // If we already have content from stream events, use that
        // Otherwise populate from the message's content blocks (non-streaming flow)
        if (context.CurrentAssistantMessage.Content.Count == 0 && assistantMsg.Message.Content.Count > 0)
        {
            foreach (var block in assistantMsg.Message.Content)
            {
                var content = ConvertSdkContentBlock(sessionId, block);
                context.CurrentAssistantMessage.Content.Add(content);

                // Check for AskUserQuestion tool
                if (content.Type == ClaudeContentType.ToolUse &&
                    content.ToolName == "AskUserQuestion" &&
                    !string.IsNullOrEmpty(content.ToolInput))
                {
                    await HandleAskUserQuestionTool(sessionId, session, content, cancellationToken);
                }

                // Check for ExitPlanMode
                if (content.ToolName?.Equals("ExitPlanMode", StringComparison.OrdinalIgnoreCase) == true)
                {
                    await HandleExitPlanModeCompletedAsync(sessionId, session, content, cancellationToken);
                }

                // Capture plan content from Write tool
                if (content.ToolName?.Equals("Write", StringComparison.OrdinalIgnoreCase) == true)
                {
                    TryCaptureWrittenPlanContent(session, content);
                }
            }
            _logger.LogDebug("Populated assistant message from SdkAssistantMessage ({Count} blocks)",
                assistantMsg.Message.Content.Count);
        }

        // Cache the assistant message if we have content
        if (context.CurrentAssistantMessage.Content.Count > 0 && !context.HasCachedCurrentMessage)
        {
            session.Messages.Add(context.CurrentAssistantMessage);
            await _messageCache.AppendMessageAsync(sessionId, context.CurrentAssistantMessage, cancellationToken);
            context.HasCachedCurrentMessage = true;

            await _hubContext.BroadcastMessageReceived(sessionId, context.CurrentAssistantMessage);
        }

        // Clear the assembler for the next assistant message
        context.Assembler.Clear();
    }

    /// <summary>
    /// Processes an SdkUserMessage — extracts tool results and handles Write/ExitPlanMode detection.
    /// </summary>
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
            await _hubContext.BroadcastMessageReceived(sessionId, toolResultMessage);

            // Check tool results for Write (plan capture) and ExitPlanMode
            foreach (var toolResult in toolResultContents)
            {
                if (toolResult.ToolName?.Equals("Write", StringComparison.OrdinalIgnoreCase) == true)
                {
                    TryCaptureWrittenPlanContentFromResult(session, toolResult);
                }

                if (toolResult.ToolName?.Equals("ExitPlanMode", StringComparison.OrdinalIgnoreCase) == true)
                {
                    var exitPlanBlock = new ClaudeMessageContent
                    {
                        Type = ClaudeContentType.ToolUse,
                        ToolName = "ExitPlanMode"
                    };
                    await HandleExitPlanModeCompletedAsync(sessionId, session, exitPlanBlock, cancellationToken);
                }
            }

            // Start a new assistant message for the next turn
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

    /// <summary>
    /// Converts a ContentBlockState (from stream event assembly) to ClaudeMessageContent.
    /// </summary>
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

    /// <summary>
    /// Converts an SdkContentBlock to a ClaudeMessageContent.
    /// </summary>
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
                // Track tool use ID -> name mapping
                var sessionToolUses = _sessionToolUses.GetOrAdd(sessionId, _ => new ConcurrentDictionary<string, string>());
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

    /// <summary>
    /// Converts an SdkToolResultBlock to a ClaudeMessageContent, resolving tool names from tracked tool uses.
    /// </summary>
    private ClaudeMessageContent ConvertSdkToolResult(string sessionId, SdkToolResultBlock toolResult)
    {
        // Resolve tool name from tool use ID tracking
        string? toolName = null;
        if (_sessionToolUses.TryGetValue(sessionId, out var toolUses))
        {
            toolUses.TryGetValue(toolResult.ToolUseId, out toolName);
        }
        // Also check the current assembler for this session
        toolName ??= _sessionToolUses
            .GetValueOrDefault(sessionId)?
            .GetValueOrDefault(toolResult.ToolUseId);

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

        // Parse tool results for rich display
        if (!string.IsNullOrEmpty(toolName))
        {
            content.ParsedToolResult = _toolResultParser.Parse(toolName, contentText, toolResult.IsError == true);
        }

        return content;
    }

    /// <summary>
    /// Handles the AskUserQuestion tool by parsing the input and setting up for user response.
    /// </summary>
    private async Task HandleAskUserQuestionTool(
        string sessionId,
        ClaudeSession session,
        ClaudeMessageContent toolUseContent,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("AskUserQuestion tool detected in session {SessionId}, parsing questions", sessionId);

        try
        {
            // Parse the tool input JSON to extract questions
            var toolInput = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(toolUseContent.ToolInput!);
            if (toolInput == null || !toolInput.TryGetValue("questions", out var questionsElement))
            {
                _logger.LogWarning("AskUserQuestion tool input missing 'questions' array");
                return;
            }

            var questions = new List<UserQuestion>();
            foreach (var questionElement in questionsElement.EnumerateArray())
            {
                var options = new List<QuestionOption>();
                if (questionElement.TryGetProperty("options", out var optionsElement))
                {
                    foreach (var optionElement in optionsElement.EnumerateArray())
                    {
                        options.Add(new QuestionOption
                        {
                            Label = optionElement.GetProperty("label").GetString() ?? "",
                            Description = optionElement.GetProperty("description").GetString() ?? ""
                        });
                    }
                }

                questions.Add(new UserQuestion
                {
                    Question = questionElement.GetProperty("question").GetString() ?? "",
                    Header = questionElement.TryGetProperty("header", out var headerElement) ? headerElement.GetString() ?? "" : "",
                    Options = options,
                    MultiSelect = questionElement.TryGetProperty("multiSelect", out var multiSelectElement) && multiSelectElement.GetBoolean()
                });
            }

            if (questions.Count == 0)
            {
                _logger.LogWarning("AskUserQuestion tool had no questions to parse");
                return;
            }

            // Create the pending question
            var pendingQuestion = new PendingQuestion
            {
                Id = Guid.NewGuid().ToString(),
                ToolUseId = toolUseContent.ToolUseId ?? "",
                Questions = questions
            };

            // Store the pending question in the session
            session.PendingQuestion = pendingQuestion;
            session.Status = ClaudeSessionStatus.WaitingForQuestionAnswer;

            // Broadcast the question to clients
            await _hubContext.BroadcastQuestionReceived(sessionId, pendingQuestion);
            await _hubContext.BroadcastSessionStatusChanged(sessionId, ClaudeSessionStatus.WaitingForQuestionAnswer);

            _logger.LogInformation("Session {SessionId} is now waiting for user to answer {QuestionCount} questions",
                sessionId, questions.Count);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse AskUserQuestion tool input in session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// Handles a question_pending control event from a Docker/Azure worker.
    /// Parses the questions JSON and sets up the pending question, same as HandleAskUserQuestionTool
    /// but without needing to parse from tool_use content blocks.
    /// </summary>
    private async Task HandleQuestionPendingFromWorkerAsync(
        string sessionId,
        ClaudeSession session,
        SdkQuestionPendingMessage questionMsg,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("question_pending control event received for session {SessionId}", sessionId);

        try
        {
            using var doc = JsonDocument.Parse(questionMsg.QuestionsJson);
            var questionsElement = doc.RootElement.TryGetProperty("questions", out var qe) ? qe : doc.RootElement;

            var questions = new List<UserQuestion>();

            // Handle both { questions: [...] } and bare array [...]
            var arrayToEnumerate = questionsElement.ValueKind == JsonValueKind.Array
                ? questionsElement
                : throw new JsonException("Expected questions array in question_pending data");

            foreach (var questionElement in arrayToEnumerate.EnumerateArray())
            {
                var options = new List<QuestionOption>();
                if (questionElement.TryGetProperty("options", out var optionsElement))
                {
                    foreach (var optionElement in optionsElement.EnumerateArray())
                    {
                        options.Add(new QuestionOption
                        {
                            Label = optionElement.GetProperty("label").GetString() ?? "",
                            Description = optionElement.GetProperty("description").GetString() ?? ""
                        });
                    }
                }

                questions.Add(new UserQuestion
                {
                    Question = questionElement.GetProperty("question").GetString() ?? "",
                    Header = questionElement.TryGetProperty("header", out var headerElement) ? headerElement.GetString() ?? "" : "",
                    Options = options,
                    MultiSelect = questionElement.TryGetProperty("multiSelect", out var multiSelectElement) && multiSelectElement.GetBoolean()
                });
            }

            if (questions.Count == 0)
            {
                _logger.LogWarning("question_pending event had no questions for session {SessionId}", sessionId);
                return;
            }

            var pendingQuestion = new PendingQuestion
            {
                Id = Guid.NewGuid().ToString(),
                ToolUseId = "",
                Questions = questions
            };

            session.PendingQuestion = pendingQuestion;
            session.Status = ClaudeSessionStatus.WaitingForQuestionAnswer;

            await _hubContext.BroadcastQuestionReceived(sessionId, pendingQuestion);
            await _hubContext.BroadcastSessionStatusChanged(sessionId, ClaudeSessionStatus.WaitingForQuestionAnswer);

            _logger.LogInformation("Session {SessionId} is now waiting for user to answer {QuestionCount} questions (from worker)",
                sessionId, questions.Count);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse question_pending JSON for session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// Handles a plan_pending control event from a Docker/Azure worker.
    /// The worker has paused on ExitPlanMode and emitted the plan content.
    /// Parses the plan, displays it, and sets status to WaitingForPlanExecution.
    /// </summary>
    private async Task HandlePlanPendingFromWorkerAsync(
        string sessionId,
        ClaudeSession session,
        SdkPlanPendingMessage planMsg,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("plan_pending control event received for session {SessionId}", sessionId);

        try
        {
            // Parse plan content from the control event JSON
            string? planContent = null;
            using var doc = JsonDocument.Parse(planMsg.PlanJson);
            if (doc.RootElement.TryGetProperty("plan", out var planElement))
            {
                planContent = planElement.GetString();
            }

            // If plan content is empty from control event, try stored plan content
            if (string.IsNullOrEmpty(planContent) && !string.IsNullOrEmpty(session.PlanContent))
            {
                planContent = session.PlanContent;
                _logger.LogInformation("plan_pending: Using stored plan content for session {SessionId}", sessionId);
            }

            // If still no content, try to fetch from agent container
            if (string.IsNullOrEmpty(planContent) && !string.IsNullOrEmpty(session.PlanFilePath) &&
                _agentSessionIds.TryGetValue(sessionId, out var agentSessionId))
            {
                planContent = await _agentExecutionService.ReadFileFromAgentAsync(
                    agentSessionId, session.PlanFilePath, cancellationToken);
            }

            // Last resort: search common plan locations via agent container
            if (string.IsNullOrEmpty(planContent) &&
                _agentSessionIds.TryGetValue(sessionId, out var agentSid))
            {
                planContent = await TryReadPlanFromAgentAsync(agentSid, session.WorkingDirectory, cancellationToken);
            }

            if (!string.IsNullOrEmpty(planContent))
            {
                session.PlanContent = planContent;

                // Create a plan message to display in chat
                var planMessage = new ClaudeMessage
                {
                    SessionId = sessionId,
                    Role = ClaudeMessageRole.Assistant,
                    Content =
                    [
                        new ClaudeMessageContent
                        {
                            Type = ClaudeContentType.Text,
                            Text = $"## 📋 Implementation Plan\n\n{planContent}"
                        }
                    ]
                };

                session.Messages.Add(planMessage);
                await _hubContext.BroadcastMessageReceived(sessionId, planMessage);
            }
            else
            {
                _logger.LogWarning("plan_pending: No plan content found for session {SessionId}", sessionId);
            }

            // Set status to WaitingForPlanExecution so UI shows the action buttons
            session.Status = ClaudeSessionStatus.WaitingForPlanExecution;
            await _hubContext.BroadcastSessionStatusChanged(sessionId, session.Status);

            _logger.LogInformation("Session {SessionId} is now waiting for plan approval (from worker plan_pending)",
                sessionId);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse plan_pending JSON for session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// Handles ExitPlanMode tool completion - reads the plan file and displays it as a chat message.
    /// </summary>
    private async Task HandleExitPlanModeCompletedAsync(
        string sessionId,
        ClaudeSession session,
        ClaudeMessageContent toolUseBlock,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("ExitPlanMode detected for session {SessionId}", sessionId);

        // Try to extract plan file path from tool input
        string? planFilePath = null;
        if (!string.IsNullOrEmpty(toolUseBlock.ToolInput))
        {
            try
            {
                var inputParams = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(toolUseBlock.ToolInput);
                planFilePath = TryGetPlanFilePath(inputParams, session.WorkingDirectory);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse ExitPlanMode input JSON");
            }
        }

        // Fall back to stored plan file path from Write tool capture
        if (string.IsNullOrEmpty(planFilePath) && !string.IsNullOrEmpty(session.PlanFilePath))
        {
            planFilePath = session.PlanFilePath;
        }

        // Try to find and read the plan file from local filesystem
        var (foundPath, planContent) = await TryReadPlanFileAsync(session.WorkingDirectory, planFilePath);

        // If no file found locally, try the session's stored plan content (captured from Write tool events)
        if (string.IsNullOrEmpty(planContent) && !string.IsNullOrEmpty(session.PlanContent))
        {
            planContent = session.PlanContent;
            foundPath = session.PlanFilePath;
            _logger.LogInformation("ExitPlanMode: Using stored plan content for session {SessionId}", sessionId);
        }

        // If still no plan content, try to fetch the file from the agent container's filesystem.
        // This handles the case where the agent runs in a Docker/Azure container and the plan file
        // exists only inside that container (e.g., ~/.claude/plans/), not on the parent's filesystem.
        if (string.IsNullOrEmpty(planContent) && !string.IsNullOrEmpty(planFilePath) &&
            _agentSessionIds.TryGetValue(sessionId, out var agentSessionId))
        {
            _logger.LogInformation("ExitPlanMode: Attempting to read plan from agent container at {Path} for session {SessionId}",
                planFilePath, sessionId);

            planContent = await _agentExecutionService.ReadFileFromAgentAsync(agentSessionId, planFilePath, cancellationToken);
            if (!string.IsNullOrEmpty(planContent))
            {
                foundPath = planFilePath;
                _logger.LogInformation("ExitPlanMode: Successfully read plan from agent container ({Length} chars)", planContent.Length);
            }
        }

        // Last resort: if we have a known plan file path pattern but no content yet,
        // try common ~/.claude/plans/ locations via the agent container
        if (string.IsNullOrEmpty(planContent) &&
            _agentSessionIds.TryGetValue(sessionId, out var agentSid))
        {
            planContent = await TryReadPlanFromAgentAsync(agentSid, session.WorkingDirectory, cancellationToken);
            if (!string.IsNullOrEmpty(planContent))
            {
                foundPath = "agent:~/.claude/plans/";
                _logger.LogInformation("ExitPlanMode: Found plan via agent container search ({Length} chars)", planContent.Length);
            }
        }

        if (!string.IsNullOrEmpty(planContent))
        {
            session.PlanFilePath = foundPath;
            session.PlanContent = planContent;

            // Create a plan message to display in chat
            var planMessage = new ClaudeMessage
            {
                SessionId = sessionId,
                Role = ClaudeMessageRole.Assistant,
                Content =
                [
                    new ClaudeMessageContent
                    {
                        Type = ClaudeContentType.Text,
                        Text = $"## 📋 Implementation Plan\n\n{planContent}"
                    }
                ]
            };

            session.Messages.Add(planMessage);
            await _hubContext.BroadcastMessageReceived(sessionId, planMessage);

            // Set status to WaitingForPlanExecution so UI shows the action buttons
            session.Status = ClaudeSessionStatus.WaitingForPlanExecution;
            await _hubContext.BroadcastSessionStatusChanged(sessionId, session.Status);

            _logger.LogInformation("ExitPlanMode: Displayed plan from {FilePath} for session {SessionId} ({Length} chars), awaiting execution",
                foundPath ?? "stored content", sessionId, planContent.Length);
        }
        else
        {
            _logger.LogWarning("ExitPlanMode: No plan file found for session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// Attempts to read plan files from the agent container's filesystem.
    /// Searches common plan file locations (e.g., ~/.claude/plans/) that are
    /// only accessible inside the agent container, not on the parent's filesystem.
    /// </summary>
    private async Task<string?> TryReadPlanFromAgentAsync(
        string agentSessionId,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        // Try common plan file locations that exist inside the agent container
        var pathsToTry = new[]
        {
            Path.Combine(workingDirectory, "PLAN.md"),
            Path.Combine(workingDirectory, ".claude", "plan.md"),
            Path.Combine(workingDirectory, ".claude", "PLAN.md")
        };

        foreach (var path in pathsToTry)
        {
            var content = await _agentExecutionService.ReadFileFromAgentAsync(agentSessionId, path, cancellationToken);
            if (!string.IsNullOrEmpty(content))
            {
                _logger.LogDebug("Found plan file via agent at {Path}", path);
                return content;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks if a Write tool result is writing to a plan file and captures the content.
    /// This allows ExitPlanMode to display the plan even when the file is in a non-standard location.
    /// </summary>
    private void TryCaptureWrittenPlanContent(ClaudeSession session, ClaudeMessageContent toolUseBlock)
    {
        if (string.IsNullOrEmpty(toolUseBlock.ToolInput))
            return;

        try
        {
            var inputParams = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(toolUseBlock.ToolInput);
            if (inputParams == null)
                return;

            // Extract file_path from tool input
            if (!inputParams.TryGetValue("file_path", out var filePathElement) ||
                filePathElement.ValueKind != JsonValueKind.String)
                return;

            var filePath = filePathElement.GetString();
            if (string.IsNullOrEmpty(filePath))
                return;

            // Check if this looks like a plan file
            // Claude Code writes plans to ~/.claude/plans/ directory with random names like fluffy-aurora.md
            // We also capture files in .claude/ directory ending with plan.md
            var normalizedPath = filePath.Replace('\\', '/').ToLowerInvariant();
            var isPlanFile = normalizedPath.Contains("/plans/") ||
                             (normalizedPath.Contains("/.claude/") && normalizedPath.EndsWith("plan.md"));

            if (!isPlanFile)
                return;

            // Extract content from tool input
            if (!inputParams.TryGetValue("content", out var contentElement) ||
                contentElement.ValueKind != JsonValueKind.String)
                return;

            var content = contentElement.GetString();
            if (string.IsNullOrEmpty(content))
                return;

            // Store the plan content for ExitPlanMode to use
            session.PlanContent = content;
            session.PlanFilePath = filePath;
            _logger.LogInformation("Captured plan content from Write tool: {FilePath} ({Length} chars)",
                filePath, content.Length);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse Write tool input for plan content capture");
        }
    }

    /// <summary>
    /// Checks if a Write tool result (arriving as a user message) wrote to a plan file and captures the path.
    /// In agent mode, tool_use blocks are not streamed — only tool results arrive as user messages.
    /// The parsed WriteToolData provides the file path; content is read from disk if accessible locally.
    /// </summary>
    private void TryCaptureWrittenPlanContentFromResult(ClaudeSession session, ClaudeMessageContent toolResultBlock)
    {
        if (toolResultBlock.ParsedToolResult?.TypedData is not WriteToolData writeData)
            return;

        var filePath = writeData.FilePath;
        if (string.IsNullOrEmpty(filePath))
            return;

        // Check if this looks like a plan file (same logic as TryCaptureWrittenPlanContent)
        var normalizedPath = filePath.Replace('\\', '/').ToLowerInvariant();
        var isPlanFile = normalizedPath.Contains("/plans/") ||
                         (normalizedPath.Contains("/.claude/") && normalizedPath.EndsWith("plan.md"));

        if (!isPlanFile)
            return;

        session.PlanFilePath = filePath;
        _logger.LogInformation("Captured plan file path from Write tool result: {FilePath}", filePath);

        // Try to read content from local disk (works for local execution mode)
        try
        {
            if (File.Exists(filePath))
            {
                session.PlanContent = File.ReadAllText(filePath);
                _logger.LogInformation("Read plan content from local disk ({Length} chars)", session.PlanContent.Length);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not read plan file from local disk at {FilePath}", filePath);
        }
    }

    /// <summary>
    /// Attempts to extract the plan file path from ExitPlanMode tool input parameters.
    /// </summary>
    private string? TryGetPlanFilePath(Dictionary<string, JsonElement>? inputParams, string workingDirectory)
    {
        if (inputParams == null) return null;

        // Check for common parameter names that might contain the plan file path
        string[] possibleKeys = ["planFile", "planFilePath", "file", "path"];
        foreach (var key in possibleKeys)
        {
            if (inputParams.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var path = value.GetString();
                if (!string.IsNullOrEmpty(path))
                {
                    // Handle relative paths
                    return Path.IsPathRooted(path) ? path : Path.Combine(workingDirectory, path);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Attempts to read the plan file from various possible locations.
    /// Returns the path where the file was found and its content.
    /// </summary>
    private async Task<(string? foundPath, string? content)> TryReadPlanFileAsync(string workingDirectory, string? specifiedPath)
    {
        // Priority order:
        // 1. Specified path from tool input
        // 2. PLAN.md in working directory (Claude CLI fallback location)
        // 3. .claude/plan.md or .claude/PLAN.md in working directory

        var pathsToTry = new List<string>();

        if (!string.IsNullOrEmpty(specifiedPath))
            pathsToTry.Add(specifiedPath);

        pathsToTry.Add(Path.Combine(workingDirectory, "PLAN.md"));
        pathsToTry.Add(Path.Combine(workingDirectory, ".claude", "plan.md"));
        pathsToTry.Add(Path.Combine(workingDirectory, ".claude", "PLAN.md"));

        foreach (var path in pathsToTry)
        {
            if (File.Exists(path))
            {
                try
                {
                    var content = await File.ReadAllTextAsync(path);
                    _logger.LogDebug("Successfully read plan file from {Path}", path);
                    return (path, content);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not read plan file at {Path}", path);
                }
            }
        }

        return (null, null);
    }

    /// <summary>
    /// Builds a combined system prompt from hook outputs and an optional base prompt.
    /// Hook outputs are placed first to provide context, followed by the original prompt.
    /// </summary>
    private static string? BuildSystemPrompt(string? basePrompt, IReadOnlyList<string> hookOutputs)
    {
        if (hookOutputs.Count == 0)
            return basePrompt;

        var parts = new List<string>();

        // Add hook outputs first (context from fleece prime, etc.)
        parts.AddRange(hookOutputs);

        // Add original system prompt if present
        if (!string.IsNullOrWhiteSpace(basePrompt))
            parts.Add(basePrompt);

        return string.Join("\n\n", parts);
    }

    /// <inheritdoc />
    public async Task AnswerQuestionAsync(string sessionId, Dictionary<string, string> answers, CancellationToken cancellationToken = default)
    {
        var session = _sessionStore.GetById(sessionId);
        if (session == null)
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        if (session.Status != ClaudeSessionStatus.WaitingForQuestionAnswer || session.PendingQuestion == null)
        {
            throw new InvalidOperationException($"Session {sessionId} is not waiting for a question answer");
        }

        _logger.LogInformation("Answering question in session {SessionId} with {AnswerCount} answers",
            sessionId, answers.Count);

        // Store the questions for reference before clearing
        var pendingQuestion = session.PendingQuestion;

        // Clear the pending question and update status
        session.PendingQuestion = null;
        session.Status = ClaudeSessionStatus.Running;

        // Broadcast that the question was answered
        await _hubContext.BroadcastQuestionAnswered(sessionId);
        await _hubContext.BroadcastSessionStatusChanged(sessionId, ClaudeSessionStatus.Running);

        // Try to route the answer through the agent execution service (Docker/Azure workers).
        // When the worker has a pending question, this resolves it via HTTP POST to the worker's
        // /answer endpoint, and messages continue flowing through the original SSE stream.
        if (_agentSessionIds.TryGetValue(sessionId, out var agentSessionId))
        {
            var resolved = await _agentExecutionService.AnswerQuestionAsync(
                agentSessionId, answers, cancellationToken);
            if (resolved)
            {
                _logger.LogInformation("AnswerQuestionAsync: Worker resolved question for session {SessionId}", sessionId);
                return;
            }
        }

        // Fallback for local mode: send formatted answer as a new message
        var formattedAnswers = new System.Text.StringBuilder();
        formattedAnswers.AppendLine("I've answered your questions:");
        formattedAnswers.AppendLine();

        foreach (var question in pendingQuestion.Questions)
        {
            formattedAnswers.AppendLine($"**{question.Header}**: {question.Question}");
            if (answers.TryGetValue(question.Question, out var answer))
            {
                formattedAnswers.AppendLine($"My answer: {answer}");
            }
            else
            {
                formattedAnswers.AppendLine("My answer: (no answer provided)");
            }
            formattedAnswers.AppendLine();
        }

        formattedAnswers.AppendLine("Please continue with the task based on my answers above.");

        await SendMessageAsync(sessionId, formattedAnswers.ToString().Trim(), PermissionMode.BypassPermissions, cancellationToken);
    }

    /// <inheritdoc />
    public Task ClearContextAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var session = _sessionStore.GetById(sessionId);
        if (session == null)
        {
            _logger.LogWarning("Attempted to clear context for non-existent session {SessionId}", sessionId);
            return Task.CompletedTask;
        }

        _logger.LogInformation("Clearing context for session {SessionId}", sessionId);

        // Clear the conversation ID so the next message starts fresh
        // The UI tracks context clear markers separately for display purposes
        session.ConversationId = null;

        // Clear the agent session ID so a new agent session is started
        _agentSessionIds.TryRemove(sessionId, out _);

        // Add a context clear marker
        session.ContextClearMarkers.Add(DateTime.UtcNow);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task ExecutePlanAsync(string sessionId, bool clearContext = true, CancellationToken cancellationToken = default)
    {
        var session = _sessionStore.GetById(sessionId);
        if (session == null || string.IsNullOrEmpty(session.PlanContent))
        {
            _logger.LogWarning("Cannot execute plan: session {SessionId} not found or no plan content", sessionId);
            return;
        }

        _logger.LogInformation("Executing plan for session {SessionId}, clearContext={ClearContext}", sessionId, clearContext);

        if (clearContext)
        {
            await ClearContextAsync(sessionId, cancellationToken);
            await _hubContext.BroadcastContextCleared(sessionId);
        }

        // Clear the WaitingForPlanExecution status
        session.Status = ClaudeSessionStatus.Running;
        await _hubContext.BroadcastSessionStatusChanged(sessionId, session.Status);

        // Send the plan as the next message.
        // Do NOT reference the plan file path - the plan content is provided inline below.
        // Referencing the path causes the agent to try to read the file from disk,
        // which fails when running in a new container after context clearing.
        var executionMessage = $"Please proceed with the implementation of the plan below. The full plan is provided here — do NOT attempt to read or find a plan file on disk.\n\n{session.PlanContent}";
        await SendMessageAsync(sessionId, executionMessage, PermissionMode.BypassPermissions, cancellationToken);
    }

    /// <inheritdoc />
    public async Task ApprovePlanAsync(string sessionId, bool approved, bool keepContext, string? feedback = null,
        CancellationToken cancellationToken = default)
    {
        var session = _sessionStore.GetById(sessionId);
        if (session == null)
        {
            throw new InvalidOperationException($"Session {sessionId} not found");
        }

        if (session.Status != ClaudeSessionStatus.WaitingForPlanExecution)
        {
            throw new InvalidOperationException($"Session {sessionId} is not waiting for plan approval (status: {session.Status})");
        }

        _logger.LogInformation(
            "Plan approval for session {SessionId}: approved={Approved}, keepContext={KeepContext}, hasFeedback={HasFeedback}",
            sessionId, approved, keepContext, feedback != null);

        if (approved)
        {
            if (keepContext)
            {
                // Approve with context: tell worker to allow the ExitPlanMode tool, agent continues
                session.Status = ClaudeSessionStatus.Running;
                await _hubContext.BroadcastSessionStatusChanged(sessionId, session.Status);

                if (_agentSessionIds.TryGetValue(sessionId, out var agentSessionId))
                {
                    var resolved = await _agentExecutionService.ApprovePlanAsync(
                        agentSessionId, true, true, null, cancellationToken);
                    if (resolved)
                    {
                        _logger.LogInformation("ApprovePlanAsync: Worker approved plan (keep context) for session {SessionId}", sessionId);
                        return;
                    }
                }

                // Local fallback: execute plan keeping context
                await ExecutePlanAsync(sessionId, clearContext: false, cancellationToken);
            }
            else
            {
                // Approve with clear context: tell worker to deny (interrupts), then start fresh
                if (_agentSessionIds.TryGetValue(sessionId, out var agentSessionId))
                {
                    var resolved = await _agentExecutionService.ApprovePlanAsync(
                        agentSessionId, true, false, null, cancellationToken);
                    if (resolved)
                    {
                        _logger.LogInformation("ApprovePlanAsync: Worker notified (clear context) for session {SessionId}", sessionId);
                    }
                }

                // Execute the plan in a fresh context
                await ExecutePlanAsync(sessionId, clearContext: true, cancellationToken);
            }
        }
        else
        {
            // Reject: tell worker to deny (without interrupt), agent revises plan
            session.Status = ClaudeSessionStatus.Running;
            await _hubContext.BroadcastSessionStatusChanged(sessionId, session.Status);

            if (_agentSessionIds.TryGetValue(sessionId, out var agentSessionId))
            {
                var resolved = await _agentExecutionService.ApprovePlanAsync(
                    agentSessionId, false, false, feedback, cancellationToken);
                if (resolved)
                {
                    _logger.LogInformation("ApprovePlanAsync: Worker rejected plan for session {SessionId}", sessionId);
                    return;
                }
            }

            // Local fallback: send feedback as a new message
            var rejectMessage = !string.IsNullOrEmpty(feedback)
                ? $"I've reviewed your plan and would like changes. Here's my feedback:\n\n{feedback}\n\nPlease revise the plan based on my feedback."
                : "I've reviewed your plan and would like you to revise it. Please create an updated plan.";

            await SendMessageAsync(sessionId, rejectMessage, PermissionMode.BypassPermissions, cancellationToken);
        }
    }

    /// <inheritdoc />
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
        if (_sessionCts.TryRemove(sessionId, out var cts))
        {
            await cts.CancelAsync();
            cts.Dispose();
        }

        // Stop the agent execution service session
        if (_agentSessionIds.TryRemove(sessionId, out var agentSessionId))
        {
            try
            {
                await _agentExecutionService.StopSessionAsync(agentSessionId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error stopping agent session {AgentSessionId} for session {SessionId}",
                    agentSessionId, sessionId);
            }
        }

        // Dispose any active client
        if (_sessionClients.TryRemove(sessionId, out var client))
        {
            try
            {
                await client.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing client for session {SessionId}", sessionId);
            }
        }

        // Remove any pending question answer sources
        _questionAnswerSources.TryRemove(sessionId, out _);

        // Remove stored options and tool use tracking
        _sessionOptions.TryRemove(sessionId, out _);
        _sessionToolUses.TryRemove(sessionId, out _);

        // Update session status and remove from store
        session.Status = ClaudeSessionStatus.Stopped;
        _sessionStore.Remove(sessionId);

        // Notify clients
        await _hubContext.BroadcastSessionStopped(sessionId);
    }

    /// <inheritdoc />
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

        // Clean up any orphaned containers not tracked in memory
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

    /// <inheritdoc />
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
        if (_sessionCts.TryGetValue(sessionId, out var cts))
        {
            await cts.CancelAsync();
            cts.Dispose();
        }
        _sessionCts[sessionId] = new CancellationTokenSource();

        // Interrupt the agent execution service session (cancel but preserve session)
        if (_agentSessionIds.TryGetValue(sessionId, out var agentSessionId))
        {
            try
            {
                await _agentExecutionService.InterruptSessionAsync(agentSessionId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error interrupting agent session {AgentSessionId} for session {SessionId}",
                    agentSessionId, sessionId);
            }
        }

        // NOTE: We intentionally do NOT remove:
        // - _agentSessionIds (needed for next SendMessageAsync to route correctly)
        // - _sessionOptions (needed for next message)
        // - _sessionToolUses (tool tracking continuity)
        // - _sessionClients (if any)
        // - _sessionStore entry (session stays alive)

        // Clear any pending question answer sources (question is now moot)
        _questionAnswerSources.TryRemove(sessionId, out _);

        // Update session status to WaitingForInput
        session.Status = ClaudeSessionStatus.WaitingForInput;
        session.PendingQuestion = null;

        // Broadcast status change (NOT SessionStopped)
        await _hubContext.BroadcastSessionStatusChanged(sessionId, ClaudeSessionStatus.WaitingForInput);
    }

    /// <inheritdoc />
    public ClaudeSession? GetSession(string sessionId)
    {
        return _sessionStore.GetById(sessionId);
    }

    /// <inheritdoc />
    public ClaudeSession? GetSessionByEntityId(string entityId)
    {
        return _sessionStore.GetAll().FirstOrDefault(s => s.EntityId == entityId);
    }

    /// <inheritdoc />
    public IReadOnlyList<ClaudeSession> GetSessionsForProject(string projectId)
    {
        return _sessionStore.GetAll()
            .Where(s => s.ProjectId == projectId)
            .ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<ClaudeSession> GetAllSessions()
    {
        return _sessionStore.GetAll().ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ClaudeMessage>> GetCachedMessagesAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        return await _messageCache.GetMessagesAsync(sessionId, cancellationToken);
    }

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
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

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _logger.LogInformation("Disposing ClaudeSessionService");

        // Cancel all sessions
        foreach (var cts in _sessionCts.Values)
        {
            try
            {
                await cts.CancelAsync();
                cts.Dispose();
            }
            catch { }
        }
        _sessionCts.Clear();

        // Dispose all active clients
        foreach (var client in _sessionClients.Values)
        {
            try
            {
                await client.DisposeAsync();
            }
            catch { }
        }
        _sessionClients.Clear();

        // Clear pending question answer sources
        _questionAnswerSources.Clear();

        // Clear stored options and tool use tracking
        _sessionOptions.Clear();
        _sessionToolUses.Clear();

        // Clear agent session ID mappings
        _agentSessionIds.Clear();
    }

    /// <summary>
    /// Maps SDK PermissionMode to shared PermissionMode.
    /// </summary>
    private static SharedPermissionMode MapToSharedPermissionMode(PermissionMode mode)
    {
        return mode switch
        {
            PermissionMode.Default => SharedPermissionMode.Default,
            PermissionMode.AcceptEdits => SharedPermissionMode.AcceptEdits,
            PermissionMode.Plan => SharedPermissionMode.Plan,
            PermissionMode.BypassPermissions => SharedPermissionMode.BypassPermissions,
            _ => SharedPermissionMode.BypassPermissions
        };
    }
}
