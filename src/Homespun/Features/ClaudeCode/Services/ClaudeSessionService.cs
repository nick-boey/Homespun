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
        IToolResultParser toolResultParser)
    {
        _sessionStore = sessionStore;
        _optionsFactory = optionsFactory;
        _logger = logger;
        _hubContext = hubContext;
        _sessionDiscovery = sessionDiscovery;
        _metadataStore = metadataStore;
        _toolResultParser = toolResultParser;
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
            Status = ClaudeSessionStatus.Starting,
            CreatedAt = DateTime.UtcNow,
            SystemPrompt = systemPrompt
        };

        _sessionStore.Add(session);
        _logger.LogInformation("Created session {SessionId} for entity {EntityId} in mode {Mode}",
            sessionId, entityId, mode);

        // Create cancellation token source for this session
        var cts = new CancellationTokenSource();
        _sessionCts[sessionId] = cts;

        // Create and store the SDK options (we'll create clients per-query for streaming support)
        var options = _optionsFactory.Create(mode, workingDirectory, model, systemPrompt);
        options.PermissionMode = PermissionMode.BypassPermissions; // Allow all tools without prompting
        options.IncludePartialMessages = true; // Enable streaming with --print mode
        _sessionOptions[sessionId] = options;

        session.Status = ClaudeSessionStatus.WaitingForInput;
        _logger.LogInformation("Session {SessionId} initialized and ready", sessionId);

        // Save metadata for future resumption
        var metadata = new SessionMetadata(
            SessionId: session.ConversationId ?? sessionId, // Will be updated when we get the real ConversationId
            EntityId: entityId,
            ProjectId: projectId,
            WorkingDirectory: workingDirectory,
            Mode: mode,
            Model: model,
            SystemPrompt: systemPrompt,
            CreatedAt: session.CreatedAt
        );
        await _metadataStore.SaveAsync(metadata, cancellationToken);

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
        return SendMessageAsync(sessionId, message, PermissionMode.BypassPermissions, cancellationToken);
    }

    /// <inheritdoc />
    public async Task SendMessageAsync(string sessionId, string message, PermissionMode permissionMode, CancellationToken cancellationToken = default)
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
            var queryOptions = new ClaudeAgentOptions
            {
                AllowedTools = baseOptions.AllowedTools,
                SystemPrompt = baseOptions.SystemPrompt,
                McpServers = baseOptions.McpServers,
                PermissionMode = permissionMode,
                MaxTurns = baseOptions.MaxTurns,
                DisallowedTools = baseOptions.DisallowedTools,
                Model = baseOptions.Model,
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

            var assistantMessage = new ClaudeMessage
            {
                SessionId = sessionId,
                Role = ClaudeMessageRole.Assistant,
                Content = []
            };

            await foreach (var msg in client.ReceiveMessagesAsync().WithCancellation(linkedCts.Token))
            {
                _logger.LogDebug("Received SDK message type: {MessageType}", msg.GetType().Name);
                await ProcessSdkMessageAsync(sessionId, session, assistantMessage, msg, linkedCts.Token);

                // Stop processing after receiving the result message
                if (msg is ResultMessage)
                    break;
            }

            // Add completed assistant message
            if (assistantMessage.Content.Count > 0)
            {
                session.Messages.Add(assistantMessage);
            }

            // Only set to WaitingForInput if we're not waiting for a question answer
            _logger.LogDebug("Checking session {SessionId} status before finalizing: {Status}, PendingQuestion: {HasQuestion}",
                sessionId, session.Status, session.PendingQuestion != null);
            if (session.Status != ClaudeSessionStatus.WaitingForQuestionAnswer)
            {
                session.Status = ClaudeSessionStatus.WaitingForInput;
                _logger.LogDebug("Set session {SessionId} status to WaitingForInput", sessionId);
            }
            else
            {
                _logger.LogDebug("Session {SessionId} is waiting for question answer, NOT changing status", sessionId);
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

    private async Task ProcessSdkMessageAsync(
        string sessionId,
        ClaudeSession session,
        ClaudeMessage assistantMessage,
        Message sdkMessage,
        CancellationToken cancellationToken)
    {
        switch (sdkMessage)
        {
            case StreamEvent streamEvent:
                // Handle partial streaming updates for real-time display
                await ProcessStreamEventAsync(sessionId, session, assistantMessage, streamEvent, cancellationToken);
                break;

            case AssistantMessage assistantMsg:
                // Content already processed by streaming events (content_block_start/delta/stop)
                // Just ensure all blocks are finalized - don't add duplicates or broadcast again
                foreach (var block in assistantMessage.Content)
                {
                    block.IsStreaming = false;
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
                                var existingBlock = assistantMessage.Content.FirstOrDefault(c =>
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
        _logger.LogDebug("Processing stream event type: {EventType} for session {SessionId}", eventType, sessionId);

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
            _logger.LogDebug("Broadcasting content_block_start for index {Index}, type {Type}", index, blockType);
            await _hubContext.BroadcastStreamingContentStarted(sessionId, content, index);
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

        switch (deltaType)
        {
            case "text_delta":
                var textDelta = GetStringValue(deltaData, "text") ?? "";
                streamingBlock.Text = (streamingBlock.Text ?? "") + textDelta;
                await _hubContext.BroadcastStreamingContentDelta(sessionId, streamingBlock, textDelta, index);
                break;

            case "thinking_delta":
                var thinkingDelta = GetStringValue(deltaData, "thinking") ?? "";
                streamingBlock.Text = (streamingBlock.Text ?? "") + thinkingDelta;
                await _hubContext.BroadcastStreamingContentDelta(sessionId, streamingBlock, thinkingDelta, index);
                break;

            case "input_json_delta":
                var inputDelta = GetStringValue(deltaData, "partial_json") ?? "";
                streamingBlock.ToolInput = (streamingBlock.ToolInput ?? "") + inputDelta;
                await _hubContext.BroadcastStreamingContentDelta(sessionId, streamingBlock, inputDelta, index);
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
            _logger.LogDebug("Broadcasting content_block_stop for index {Index}, Type: {Type}, ToolName: {ToolName}",
                index, streamingBlock.Type, streamingBlock.ToolName ?? "(null)");
            await _hubContext.BroadcastStreamingContentStopped(sessionId, streamingBlock, index);

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
