using System.Collections.Concurrent;
using System.Text.Json;
using Homespun.ClaudeAgentSdk;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Homespun.Features.ClaudeCode.Services;

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


    public ClaudeSessionService(
        IClaudeSessionStore sessionStore,
        SessionOptionsFactory optionsFactory,
        ILogger<ClaudeSessionService> logger,
        IHubContext<ClaudeCodeHub> hubContext,
        IClaudeSessionDiscovery sessionDiscovery,
        ISessionMetadataStore metadataStore,
        IToolResultParser toolResultParser,
        IHooksService hooksService,
        IMessageCacheStore messageCache)
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

            // Create options for this query, using Resume if we have a conversation ID from previous query
            // Use specified model if provided, otherwise fall back to base options
            var effectiveModel = !string.IsNullOrEmpty(model) ? model : baseOptions.Model;
            var queryOptions = new ClaudeAgentOptions
            {
                AllowedTools = baseOptions.AllowedTools,
                SystemPrompt = baseOptions.SystemPrompt,
                McpServers = baseOptions.McpServers,
                PermissionMode = permissionMode,
                MaxTurns = baseOptions.MaxTurns,
                DisallowedTools = baseOptions.DisallowedTools,
                Model = effectiveModel,
                Cwd = baseOptions.Cwd,
                Settings = baseOptions.Settings,
                AddDirs = baseOptions.AddDirs,
                Env = baseOptions.Env,
                ExtraArgs = baseOptions.ExtraArgs,
                IncludePartialMessages = true, // Enable streaming
                SettingSources = baseOptions.SettingSources,
                // Resume from previous conversation if we have a ConversationId
                Resume = session.ConversationId
            };

            // Create a new client for this query with the message as prompt
            // This uses --print mode which supports --include-partial-messages for streaming
            await using var client = new ClaudeSdkClient(queryOptions);
            await client.ConnectAsync(message, linkedCts.Token);

            // Track the current assistant message being built
            // A new message is created for each turn (after tool results)
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

            await foreach (var msg in client.ReceiveMessagesAsync().WithCancellation(linkedCts.Token))
            {
                await ProcessSdkMessageAsync(sessionId, session, messageContext, msg, linkedCts.Token);

                // Stop processing after receiving the result message
                if (msg is ResultMessage)
                    break;
            }

            // Cache any remaining assistant message content
            if (messageContext.CurrentAssistantMessage.Content.Count > 0 &&
                !messageContext.HasCachedCurrentMessage)
            {
                session.Messages.Add(messageContext.CurrentAssistantMessage);
                await _messageCache.AppendMessageAsync(sessionId, messageContext.CurrentAssistantMessage, linkedCts.Token);
            }

            // Only set to WaitingForInput if we're not waiting for a question answer
            if (session.Status != ClaudeSessionStatus.WaitingForQuestionAnswer)
            {
                session.Status = ClaudeSessionStatus.WaitingForInput;
            }
            _logger.LogInformation("Message processing completed for session {SessionId}, status: {Status}", sessionId, session.Status);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Message processing cancelled for session {SessionId}", sessionId);
            session.Status = ClaudeSessionStatus.Stopped;
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
    }

    private async Task ProcessSdkMessageAsync(
        string sessionId,
        ClaudeSession session,
        MessageProcessingContext context,
        Message sdkMessage,
        CancellationToken cancellationToken)
    {
        switch (sdkMessage)
        {
            case StreamEvent streamEvent:
                // Handle partial streaming updates for real-time display
                await ProcessStreamEventAsync(sessionId, session, context.CurrentAssistantMessage, streamEvent, cancellationToken);
                break;

            case AssistantMessage assistantMsg:
                // Content already processed by streaming events (content_block_start/delta/stop)
                // Just ensure all blocks are finalized - don't add duplicates or broadcast again
                foreach (var block in context.CurrentAssistantMessage.Content)
                {
                    block.IsStreaming = false;
                }

                // CRITICAL FIX: If streaming didn't provide content blocks but the AssistantMessage has them,
                // we need to populate the assistant message from the SDK message directly.
                // This handles cases where streaming mode didn't emit content_block events.
                if (context.CurrentAssistantMessage.Content.Count == 0 && assistantMsg.Content != null && assistantMsg.Content.Count > 0)
                {
                    _logger.LogDebug("Populating assistant message from AssistantMessage SDK object ({Count} blocks)",
                        assistantMsg.Content.Count);

                    foreach (var block in assistantMsg.Content)
                    {
                        var content = ConvertContentBlock(sessionId, block);
                        if (content != null)
                        {
                            context.CurrentAssistantMessage.Content.Add(content);
                        }
                    }
                }

                // Also check for AskUserQuestion in the AssistantMessage content itself
                // This handles cases where streaming events don't include all blocks or
                // where the AssistantMessage arrives before all content_block_stop events
                if (assistantMsg.Content != null)
                {
                    foreach (var block in assistantMsg.Content)
                    {
                        if (block is ToolUseBlock toolUse && toolUse.Name == "AskUserQuestion")
                        {
                            // Check if we already processed this (PendingQuestion would be set)
                            if (session.PendingQuestion == null)
                            {
                                _logger.LogDebug("Processing AskUserQuestion from AssistantMessage: Id={Id}", toolUse.Id);

                                // Find or create the corresponding block in our assistant message
                                var existingBlock = context.CurrentAssistantMessage.Content.FirstOrDefault(c =>
                                    c.Type == ClaudeContentType.ToolUse && c.ToolUseId == toolUse.Id);

                                if (existingBlock != null && !string.IsNullOrEmpty(existingBlock.ToolInput))
                                {
                                    await HandleAskUserQuestionTool(sessionId, session, existingBlock, cancellationToken);
                                }
                                else
                                {
                                    // Create a temporary block from SDK data to process
                                    var toolInput = JsonSerializer.Serialize(toolUse.Input);
                                    var tempBlock = new ClaudeMessageContent
                                    {
                                        Type = ClaudeContentType.ToolUse,
                                        ToolName = toolUse.Name,
                                        ToolUseId = toolUse.Id,
                                        ToolInput = toolInput
                                    };
                                    await HandleAskUserQuestionTool(sessionId, session, tempBlock, cancellationToken);
                                }
                            }
                        }
                    }
                }

                // CRITICAL FIX: Cache the assistant message NOW, before tool results arrive
                // This ensures each assistant turn is properly captured in the JSONL
                if (context.CurrentAssistantMessage.Content.Count > 0 && !context.HasCachedCurrentMessage)
                {
                    session.Messages.Add(context.CurrentAssistantMessage);
                    await _messageCache.AppendMessageAsync(sessionId, context.CurrentAssistantMessage, cancellationToken);
                    context.HasCachedCurrentMessage = true;
                    _logger.LogDebug("Cached assistant message with {Count} content blocks", context.CurrentAssistantMessage.Content.Count);

                    // Broadcast the complete assistant message for UI update
                    await _hubContext.BroadcastMessageReceived(sessionId, context.CurrentAssistantMessage);
                }
                break;

            case ResultMessage resultMsg:
                session.TotalCostUsd = (decimal)(resultMsg.TotalCostUsd ?? 0);
                session.TotalDurationMs = resultMsg.DurationMs;
                // Store the Claude CLI session ID for use with --resume in subsequent messages
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

            case SystemMessage systemMsg:
                _logger.LogDebug("System message received: {Subtype}", systemMsg.Subtype);
                break;

            case UserMessage userMsg:
                // Handle user messages containing tool results
                await ProcessUserMessageAsync(sessionId, session, context, userMsg, cancellationToken);
                break;
        }
    }

    /// <summary>
    /// Processes a UserMessage from the SDK, which may contain tool results.
    /// In Claude's API protocol, tool results are sent back as user messages.
    /// </summary>
    private async Task ProcessUserMessageAsync(
        string sessionId,
        ClaudeSession session,
        MessageProcessingContext context,
        UserMessage userMsg,
        CancellationToken cancellationToken)
    {
        // Check if content contains tool results (content is a list of content blocks)
        if (userMsg.Content is not List<object> contentBlocks)
        {
            _logger.LogDebug("UserMessage content is not a list of blocks, skipping tool result processing");
            return;
        }

        var toolResultContents = new List<ClaudeMessageContent>();

        foreach (var block in contentBlocks)
        {
            if (block is ToolResultBlock toolResultBlock)
            {
                var content = ConvertToolResultBlock(sessionId, toolResultBlock);
                toolResultContents.Add(content);
                _logger.LogDebug("Processed tool result for tool use ID: {ToolUseId}, tool: {ToolName}",
                    toolResultBlock.ToolUseId, content.ToolName);
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

            // Cache the tool result message
            await _messageCache.AppendMessageAsync(sessionId, toolResultMessage, cancellationToken);

            // Broadcast to clients for real-time UI updates
            await _hubContext.BroadcastMessageReceived(sessionId, toolResultMessage);
            _logger.LogDebug("Broadcasted {Count} tool result(s) for session {SessionId}",
                toolResultContents.Count, sessionId);

            // CRITICAL FIX: Create a NEW assistant message for the next turn's content
            // This ensures each assistant response after tool results is a separate message
            context.CurrentAssistantMessage = new ClaudeMessage
            {
                SessionId = sessionId,
                Role = ClaudeMessageRole.Assistant,
                Content = []
            };
            context.HasCachedCurrentMessage = false;
            _logger.LogDebug("Created new assistant message for next turn after tool results");
        }
    }

    private async Task ProcessStreamEventAsync(
        string sessionId,
        ClaudeSession session,
        ClaudeMessage assistantMessage,
        StreamEvent streamEvent,
        CancellationToken cancellationToken)
    {
        if (streamEvent.Event == null || !streamEvent.Event.TryGetValue("type", out var typeObj))
            return;

        var eventType = typeObj is JsonElement typeElement ? typeElement.GetString() : typeObj?.ToString();

        switch (eventType)
        {
            case "content_block_start":
                await HandleContentBlockStart(sessionId, assistantMessage, streamEvent.Event, cancellationToken);
                break;

            case "content_block_delta":
                await HandleContentBlockDelta(sessionId, assistantMessage, streamEvent.Event, cancellationToken);
                break;

            case "content_block_stop":
                await HandleContentBlockStop(sessionId, session, assistantMessage, streamEvent.Event, cancellationToken);
                break;

            default:
                _logger.LogDebug("Unhandled stream event type: {EventType}", eventType);
                break;
        }
    }

    private async Task HandleContentBlockStart(
        string sessionId,
        ClaudeMessage assistantMessage,
        Dictionary<string, object> eventData,
        CancellationToken cancellationToken)
    {
        if (!eventData.TryGetValue("content_block", out var blockObj))
            return;

        // Extract the index from the event data
        var index = GetIntValue(eventData, "index") ?? -1;

        var blockJson = blockObj is JsonElement element ? element.GetRawText() : JsonSerializer.Serialize(blockObj);
        var blockData = JsonSerializer.Deserialize<Dictionary<string, object>>(blockJson);
        if (blockData == null) return;

        var blockType = GetStringValue(blockData, "type");
        ClaudeMessageContent? content = blockType switch
        {
            "text" => new ClaudeMessageContent
            {
                Type = ClaudeContentType.Text,
                Text = "",
                IsStreaming = true,
                Index = index
            },
            "thinking" => new ClaudeMessageContent
            {
                Type = ClaudeContentType.Thinking,
                Text = "",
                IsStreaming = true,
                Index = index
            },
            "tool_use" => CreateToolUseContent(sessionId, blockData, index),
            _ => null
        };

        if (content != null)
        {
            assistantMessage.Content.Add(content);
            _logger.LogDebug("Content block started for index {Index}, type {Type} (streaming disabled, waiting for completion)", index, blockType);
            // NOTE: Streaming is disabled - don't broadcast start events
            // Content will be broadcast when the block completes (content_block_stop)
        }
    }

    private async Task HandleContentBlockDelta(
        string sessionId,
        ClaudeMessage assistantMessage,
        Dictionary<string, object> eventData,
        CancellationToken cancellationToken)
    {
        if (!eventData.TryGetValue("delta", out var deltaObj))
            return;

        // Extract the index from the event data
        var index = GetIntValue(eventData, "index") ?? -1;

        var deltaJson = deltaObj is JsonElement element ? element.GetRawText() : JsonSerializer.Serialize(deltaObj);
        var deltaData = JsonSerializer.Deserialize<Dictionary<string, object>>(deltaJson);
        if (deltaData == null) return;

        var deltaType = GetStringValue(deltaData, "type");

        // Find the content block by index. We don't filter on IsStreaming here because
        // the AssistantMessage event may arrive before content_block_delta events,
        // setting IsStreaming = false on all blocks before we can process them.
        var streamingBlock = index >= 0
            ? assistantMessage.Content.FirstOrDefault(c => c.Index == index)
            : assistantMessage.Content.LastOrDefault();

        if (streamingBlock == null) return;

        // NOTE: Streaming is disabled - accumulate content but don't broadcast deltas
        // Content will be broadcast when the block completes (content_block_stop)
        switch (deltaType)
        {
            case "text_delta":
                var textDelta = GetStringValue(deltaData, "text") ?? "";
                streamingBlock.Text = (streamingBlock.Text ?? "") + textDelta;
                // Streaming disabled: no delta broadcast
                break;

            case "thinking_delta":
                var thinkingDelta = GetStringValue(deltaData, "thinking") ?? "";
                streamingBlock.Text = (streamingBlock.Text ?? "") + thinkingDelta;
                // Streaming disabled: no delta broadcast
                break;

            case "input_json_delta":
                var inputDelta = GetStringValue(deltaData, "partial_json") ?? "";
                streamingBlock.ToolInput = (streamingBlock.ToolInput ?? "") + inputDelta;
                // Streaming disabled: no delta broadcast
                break;
        }
    }

    private async Task HandleContentBlockStop(
        string sessionId,
        ClaudeSession session,
        ClaudeMessage assistantMessage,
        Dictionary<string, object> eventData,
        CancellationToken cancellationToken)
    {
        // Extract the index from the event data
        var index = GetIntValue(eventData, "index") ?? -1;

        // Find the content block by index. We don't filter on IsStreaming here because
        // the AssistantMessage event may arrive before content_block_stop events,
        // setting IsStreaming = false on all blocks before we can process them.
        var streamingBlock = index >= 0
            ? assistantMessage.Content.FirstOrDefault(c => c.Index == index)
            : assistantMessage.Content.LastOrDefault();

        if (streamingBlock != null)
        {
            streamingBlock.IsStreaming = false;

            // Check if this is ExitPlanMode completing - if so, read and display the plan
            if (streamingBlock.ToolName?.Equals("ExitPlanMode", StringComparison.OrdinalIgnoreCase) == true)
            {
                await HandleExitPlanModeCompletedAsync(sessionId, session, streamingBlock, cancellationToken);
            }

            // Check if this is a Write tool completing with a plan file - capture the content
            if (streamingBlock.ToolName?.Equals("Write", StringComparison.OrdinalIgnoreCase) == true)
            {
                TryCaptureWrittenPlanContent(session, streamingBlock);
            }

            _logger.LogDebug("Content block completed for index {Index}, Type: {Type}, ToolName: {ToolName}",
                index, streamingBlock.Type, streamingBlock.ToolName ?? "(null)");

            // NOTE: Streaming is disabled - broadcast the complete content block now
            // instead of streaming deltas. This provides immediate full content to clients.
            await _hubContext.BroadcastContentBlockReceived(sessionId, streamingBlock);

            // Check if this is an AskUserQuestion tool - if so, parse it and wait for user input
            if (streamingBlock.Type == ClaudeContentType.ToolUse &&
                streamingBlock.ToolName == "AskUserQuestion" &&
                !string.IsNullOrEmpty(streamingBlock.ToolInput))
            {
                await HandleAskUserQuestionTool(sessionId, session, streamingBlock, cancellationToken);
            }
        }
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

            var questions = new List<Data.UserQuestion>();
            foreach (var questionElement in questionsElement.EnumerateArray())
            {
                var options = new List<Data.QuestionOption>();
                if (questionElement.TryGetProperty("options", out var optionsElement))
                {
                    foreach (var optionElement in optionsElement.EnumerateArray())
                    {
                        options.Add(new Data.QuestionOption
                        {
                            Label = optionElement.GetProperty("label").GetString() ?? "",
                            Description = optionElement.GetProperty("description").GetString() ?? ""
                        });
                    }
                }

                questions.Add(new Data.UserQuestion
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
            var pendingQuestion = new Data.PendingQuestion
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

        // Try to find and read the plan file
        var (foundPath, planContent) = await TryReadPlanFileAsync(session.WorkingDirectory, planFilePath);

        // If no file found but session has stored plan content (from JSONL), use that
        if (string.IsNullOrEmpty(planContent) && !string.IsNullOrEmpty(session.PlanContent))
        {
            planContent = session.PlanContent;
            _logger.LogInformation("ExitPlanMode: Using stored plan content for session {SessionId}", sessionId);
        }

        if (!string.IsNullOrEmpty(planContent))
        {
            session.PlanFilePath = foundPath;

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

            _logger.LogInformation("ExitPlanMode: Displayed plan from {FilePath} for session {SessionId} ({Length} chars)",
                foundPath ?? "stored content", sessionId, planContent.Length);
        }
        else
        {
            _logger.LogWarning("ExitPlanMode: No plan file found for session {SessionId}", sessionId);
        }
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

    private static string? GetStringValue(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value)) return null;
        return value is JsonElement element ? element.GetString() : value?.ToString();
    }

    private static int? GetIntValue(Dictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value)) return null;
        if (value is JsonElement element)
        {
            return element.ValueKind == JsonValueKind.Number ? element.GetInt32() : null;
        }
        return value is int intValue ? intValue : null;
    }

    /// <summary>
    /// Creates a tool use content block and tracks the tool use ID -> name mapping.
    /// </summary>
    private ClaudeMessageContent CreateToolUseContent(string sessionId, Dictionary<string, object> blockData, int index)
    {
        var toolUseId = GetStringValue(blockData, "id") ?? "";
        var toolName = GetStringValue(blockData, "name") ?? "unknown";

        // Track the tool use ID -> name mapping for this session
        if (!string.IsNullOrEmpty(toolUseId))
        {
            var sessionToolUses = _sessionToolUses.GetOrAdd(sessionId, _ => new ConcurrentDictionary<string, string>());
            sessionToolUses[toolUseId] = toolName;
            _logger.LogDebug("Tracked tool use: {ToolUseId} -> {ToolName}", toolUseId, toolName);
        }

        return new ClaudeMessageContent
        {
            Type = ClaudeContentType.ToolUse,
            ToolName = toolName,
            ToolUseId = toolUseId,
            ToolInput = "",
            IsStreaming = true,
            Index = index
        };
    }

    /// <summary>
    /// Looks up the tool name for a given tool use ID.
    /// </summary>
    private string GetToolNameForUseId(string sessionId, string toolUseId)
    {
        if (_sessionToolUses.TryGetValue(sessionId, out var toolUses) &&
            toolUses.TryGetValue(toolUseId, out var toolName))
        {
            return toolName;
        }
        return "unknown";
    }

    private ClaudeMessageContent? ConvertContentBlock(string sessionId, object block)
    {
        return block switch
        {
            TextBlock textBlock => new ClaudeMessageContent
            {
                Type = ClaudeContentType.Text,
                Text = textBlock.Text
            },
            ThinkingBlock thinkingBlock => new ClaudeMessageContent
            {
                Type = ClaudeContentType.Thinking,
                Text = thinkingBlock.Thinking
            },
            ToolUseBlock toolUseBlock => ConvertToolUseBlock(sessionId, toolUseBlock),
            ToolResultBlock toolResultBlock => ConvertToolResultBlock(sessionId, toolResultBlock),
            _ => null
        };
    }

    private ClaudeMessageContent ConvertToolUseBlock(string sessionId, ToolUseBlock toolUseBlock)
    {
        // Track the tool use ID -> name mapping
        if (!string.IsNullOrEmpty(toolUseBlock.Id))
        {
            var sessionToolUses = _sessionToolUses.GetOrAdd(sessionId, _ => new ConcurrentDictionary<string, string>());
            sessionToolUses[toolUseBlock.Id] = toolUseBlock.Name;
        }

        return new ClaudeMessageContent
        {
            Type = ClaudeContentType.ToolUse,
            ToolName = toolUseBlock.Name,
            ToolUseId = toolUseBlock.Id,
            ToolInput = toolUseBlock.Input != null ? JsonSerializer.Serialize(toolUseBlock.Input) : null
        };
    }

    private ClaudeMessageContent ConvertToolResultBlock(string sessionId, ToolResultBlock toolResultBlock)
    {
        // Look up the tool name from the tool use ID
        var toolName = GetToolNameForUseId(sessionId, toolResultBlock.ToolUseId);
        var isError = toolResultBlock.IsError ?? false;
        var contentString = toolResultBlock.Content?.ToString() ?? "";

        // Parse the result for rich display
        var parsedResult = _toolResultParser.Parse(toolName, toolResultBlock.Content, isError);

        return new ClaudeMessageContent
        {
            Type = ClaudeContentType.ToolResult,
            ToolName = toolName,
            ToolUseId = toolResultBlock.ToolUseId,
            ToolSuccess = !isError,
            Text = contentString,
            ParsedToolResult = parsedResult
        };
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

        // Format the answers in a clear, structured way that Claude can understand
        // Since we're resuming with --resume, Claude will see this as a continuation
        var formattedAnswers = new System.Text.StringBuilder();
        formattedAnswers.AppendLine("I've answered your questions:");
        formattedAnswers.AppendLine();

        // Include the original questions and user's selections for context
        foreach (var question in session.PendingQuestion.Questions)
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

        // Store the questions for reference before clearing
        var pendingQuestion = session.PendingQuestion;

        // Clear the pending question
        session.PendingQuestion = null;

        // Update session status before sending
        session.Status = ClaudeSessionStatus.Running;

        // Broadcast that the question was answered
        await _hubContext.BroadcastQuestionAnswered(sessionId);
        await _hubContext.BroadcastSessionStatusChanged(sessionId, ClaudeSessionStatus.Running);

        // Send the answer as a regular message - this will resume the conversation
        // The --resume flag ensures Claude has context from the previous turn
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

        // Add a context clear marker
        session.ContextClearMarkers.Add(DateTime.UtcNow);

        return Task.CompletedTask;
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
    }
}
