using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Homespun.AgentWorker.Models;
using Homespun.ClaudeAgentSdk;

namespace Homespun.AgentWorker.Services;

/// <summary>
/// Mode of operation for agent sessions.
/// </summary>
public enum SessionMode
{
    Plan,
    Build
}

/// <summary>
/// State of an active worker session.
/// </summary>
public class WorkerSession
{
    public required string Id { get; init; }
    public required string WorkingDirectory { get; init; }
    public required SessionMode Mode { get; init; }
    public required string Model { get; init; }
    public string? ConversationId { get; set; }
    public string? SystemPrompt { get; init; }
    public ClaudeAgentOptions? Options { get; set; }
    public CancellationTokenSource? Cts { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Service that manages Claude agent sessions in the worker container.
/// </summary>
public class WorkerSessionService : IAsyncDisposable
{
    private readonly ILogger<WorkerSessionService> _logger;
    private readonly ConcurrentDictionary<string, WorkerSession> _sessions = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _sessionToolUses = new();

    /// <summary>
    /// Maximum buffer size for JSON message streaming (10MB).
    /// </summary>
    private const int DefaultMaxBufferSize = 10 * 1024 * 1024;

    /// <summary>
    /// Read-only tools available in Plan mode.
    /// </summary>
    private static readonly string[] PlanModeTools =
    [
        "Read", "Glob", "Grep", "WebFetch", "WebSearch",
        "Task", "AskUserQuestion", "ExitPlanMode"
    ];

    public WorkerSessionService(ILogger<WorkerSessionService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Starts a new session and streams events via IAsyncEnumerable.
    /// </summary>
    public async IAsyncEnumerable<(string EventType, object Data)> StartSessionAsync(
        StartSessionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid().ToString();
        var mode = Enum.TryParse<SessionMode>(request.Mode, true, out var parsedMode)
            ? parsedMode : SessionMode.Build;

        var session = new WorkerSession
        {
            Id = sessionId,
            WorkingDirectory = request.WorkingDirectory,
            Mode = mode,
            Model = request.Model,
            SystemPrompt = request.SystemPrompt
        };

        var options = CreateOptions(session);
        options.PermissionMode = PermissionMode.BypassPermissions;
        options.IncludePartialMessages = true;

        // Handle session resumption
        if (!string.IsNullOrEmpty(request.ResumeSessionId))
        {
            options.Resume = request.ResumeSessionId;
            session.ConversationId = request.ResumeSessionId;
        }

        session.Options = options;
        session.Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _sessions[sessionId] = session;
        _sessionToolUses[sessionId] = new ConcurrentDictionary<string, string>();

        _logger.LogInformation("Starting session {SessionId} in {Mode} mode", sessionId, mode);

        // Emit session started event
        yield return (SseEventTypes.SessionStarted, new SessionStartedData
        {
            SessionId = sessionId,
            ConversationId = session.ConversationId
        });

        // Process messages from the SDK
        await foreach (var evt in ProcessMessagesAsync(session, request.Prompt, session.Cts.Token))
        {
            yield return evt;
        }
    }

    /// <summary>
    /// Sends a message to an existing session and streams events.
    /// </summary>
    public async IAsyncEnumerable<(string EventType, object Data)> SendMessageAsync(
        string sessionId,
        SendMessageRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            yield return (SseEventTypes.Error, new ErrorData
            {
                SessionId = sessionId,
                Message = $"Session {sessionId} not found",
                Code = "SESSION_NOT_FOUND",
                IsRecoverable = false
            });
            yield break;
        }

        session.LastActivityAt = DateTime.UtcNow;

        // Create options for this query
        var queryOptions = CreateOptions(session);
        queryOptions.PermissionMode = PermissionMode.BypassPermissions;
        queryOptions.IncludePartialMessages = true;
        queryOptions.Resume = session.ConversationId;

        if (!string.IsNullOrEmpty(request.Model))
        {
            queryOptions.Model = request.Model;
        }

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Cts?.Token ?? CancellationToken.None);

        await foreach (var evt in ProcessMessagesAsync(session, request.Message, linkedCts.Token))
        {
            yield return evt;
        }
    }

    /// <summary>
    /// Answers a pending question and streams events.
    /// </summary>
    public async IAsyncEnumerable<(string EventType, object Data)> AnswerQuestionAsync(
        string sessionId,
        AnswerQuestionRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            yield return (SseEventTypes.Error, new ErrorData
            {
                SessionId = sessionId,
                Message = $"Session {sessionId} not found",
                Code = "SESSION_NOT_FOUND",
                IsRecoverable = false
            });
            yield break;
        }

        // Format answers into a message
        var formattedAnswers = new System.Text.StringBuilder();
        formattedAnswers.AppendLine("I've answered your questions:");
        formattedAnswers.AppendLine();

        foreach (var (question, answer) in request.Answers)
        {
            formattedAnswers.AppendLine($"**{question}**");
            formattedAnswers.AppendLine($"My answer: {answer}");
            formattedAnswers.AppendLine();
        }

        formattedAnswers.AppendLine("Please continue with the task based on my answers above.");

        var messageRequest = new SendMessageRequest
        {
            Message = formattedAnswers.ToString().Trim()
        };

        await foreach (var evt in SendMessageAsync(sessionId, messageRequest, cancellationToken))
        {
            yield return evt;
        }
    }

    /// <summary>
    /// Stops a session.
    /// </summary>
    public async Task StopSessionAsync(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            _logger.LogInformation("Stopping session {SessionId}", sessionId);

            if (session.Cts != null)
            {
                await session.Cts.CancelAsync();
                session.Cts.Dispose();
            }

            _sessionToolUses.TryRemove(sessionId, out _);
        }
    }

    /// <summary>
    /// Gets information about a session.
    /// </summary>
    public WorkerSession? GetSession(string sessionId)
    {
        return _sessions.TryGetValue(sessionId, out var session) ? session : null;
    }

    private ClaudeAgentOptions CreateOptions(WorkerSession session)
    {
        var options = new ClaudeAgentOptions
        {
            Cwd = session.WorkingDirectory,
            Model = session.Model,
            SystemPrompt = session.SystemPrompt,
            MaxBufferSize = DefaultMaxBufferSize,
            BufferOverflowBehavior = BufferOverflowBehavior.SkipMessage,
            OnBufferOverflow = (messageType, actualSize, maxSize) =>
            {
                _logger.LogWarning(
                    "Buffer overflow: type={MessageType}, size={ActualSize} exceeds max={MaxSize}",
                    messageType ?? "unknown", actualSize, maxSize);
            },
            SettingSources = [SettingSource.User],
            // Configure Playwright MCP for container environment
            McpServers = new Dictionary<string, object>
            {
                ["playwright"] = new Dictionary<string, object>
                {
                    ["type"] = "stdio",
                    ["command"] = "npx",
                    ["args"] = new[] { "@playwright/mcp@latest", "--headless", "--browser", "chromium", "--no-sandbox", "--isolated" },
                    ["env"] = new Dictionary<string, string>
                    {
                        ["PLAYWRIGHT_BROWSERS_PATH"] = "/opt/playwright-browsers"
                    }
                }
            }
        };

        if (session.Mode == SessionMode.Plan)
        {
            options.AllowedTools = PlanModeTools.ToList();
        }

        return options;
    }

    private async IAsyncEnumerable<(string EventType, object Data)> ProcessMessagesAsync(
        WorkerSession session,
        string prompt,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var currentContentBlocks = new List<ContentBlockReceivedData>();
        var channel = System.Threading.Channels.Channel.CreateUnbounded<(string EventType, object Data)>();

        _ = Task.Run(async () =>
        {
            try
            {
                await using var client = new ClaudeSdkClient(session.Options!);
                await client.ConnectAsync(prompt, cancellationToken);

                await foreach (var msg in client.ReceiveMessagesAsync().WithCancellation(cancellationToken))
                {
                    var events = ProcessSdkMessage(session, msg, currentContentBlocks);
                    foreach (var evt in events)
                    {
                        await channel.Writer.WriteAsync(evt, cancellationToken);

                        // Stop after result message
                        if (evt.EventType == SseEventTypes.ResultReceived)
                        {
                            await channel.Writer.WriteAsync((SseEventTypes.SessionEnded, new SessionEndedData
                            {
                                SessionId = session.Id,
                                Reason = "completed"
                            }), cancellationToken);
                            channel.Writer.Complete();
                            return;
                        }
                    }
                }
                channel.Writer.Complete();
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Session {SessionId} cancelled", session.Id);
                await channel.Writer.WriteAsync((SseEventTypes.SessionEnded, new SessionEndedData
                {
                    SessionId = session.Id,
                    Reason = "cancelled"
                }));
                channel.Writer.Complete();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in session {SessionId}", session.Id);
                await channel.Writer.WriteAsync((SseEventTypes.Error, new ErrorData
                {
                    SessionId = session.Id,
                    Message = ex.Message,
                    Code = "AGENT_ERROR",
                    IsRecoverable = false
                }));
                channel.Writer.Complete(ex);
            }
        }, cancellationToken);

        await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return evt;
        }
    }

    private List<(string EventType, object Data)> ProcessSdkMessage(
        WorkerSession session,
        Message sdkMessage,
        List<ContentBlockReceivedData> currentContentBlocks)
    {
        var events = new List<(string EventType, object Data)>();

        switch (sdkMessage)
        {
            case StreamEvent streamEvent:
                var streamEvents = ProcessStreamEvent(session, streamEvent, currentContentBlocks);
                events.AddRange(streamEvents);
                break;

            case AssistantMessage assistantMsg:
                // Content may come from streaming events OR directly in the message
                // (e.g., authentication errors return content directly without streaming)
                List<ContentBlockReceivedData> contentToEmit;

                if (currentContentBlocks.Count > 0)
                {
                    // Use content from streaming events
                    contentToEmit = new List<ContentBlockReceivedData>(currentContentBlocks);
                    currentContentBlocks.Clear();
                }
                else if (assistantMsg.Content.Count > 0)
                {
                    // Convert content blocks from the message directly
                    contentToEmit = ConvertAssistantContent(session.Id, assistantMsg.Content);
                }
                else
                {
                    contentToEmit = new List<ContentBlockReceivedData>();
                }

                if (contentToEmit.Count > 0)
                {
                    events.Add((SseEventTypes.MessageReceived, new MessageReceivedData
                    {
                        SessionId = session.Id,
                        Role = "Assistant",
                        Content = contentToEmit
                    }));
                }
                break;

            case ResultMessage resultMsg:
                session.ConversationId = resultMsg.SessionId;

                // Emit error event if the result indicates an error
                if (resultMsg.IsError && !string.IsNullOrEmpty(resultMsg.Result))
                {
                    events.Add((SseEventTypes.Error, new ErrorData
                    {
                        SessionId = session.Id,
                        Message = resultMsg.Result,
                        Code = "AGENT_ERROR",
                        IsRecoverable = false
                    }));
                }

                events.Add((SseEventTypes.ResultReceived, new ResultReceivedData
                {
                    SessionId = session.Id,
                    TotalCostUsd = (decimal)(resultMsg.TotalCostUsd ?? 0),
                    DurationMs = resultMsg.DurationMs,
                    ConversationId = resultMsg.SessionId
                }));
                break;

            case UserMessage userMsg:
                // Handle tool results from SDK
                var toolEvents = ProcessUserMessage(session, userMsg);
                events.AddRange(toolEvents);
                break;
        }

        return events;
    }

    private List<(string EventType, object Data)> ProcessStreamEvent(
        WorkerSession session,
        StreamEvent streamEvent,
        List<ContentBlockReceivedData> currentContentBlocks)
    {
        var events = new List<(string EventType, object Data)>();

        if (streamEvent.Event == null || !streamEvent.Event.TryGetValue("type", out var typeObj))
            return events;

        var eventType = typeObj is JsonElement typeElement ? typeElement.GetString() : typeObj?.ToString();

        switch (eventType)
        {
            case "content_block_start":
                ProcessContentBlockStart(session, streamEvent.Event, currentContentBlocks);
                break;

            case "content_block_delta":
                ProcessContentBlockDelta(streamEvent.Event, currentContentBlocks);
                break;

            case "content_block_stop":
                var stopEvents = ProcessContentBlockStop(session, streamEvent.Event, currentContentBlocks);
                events.AddRange(stopEvents);
                break;
        }

        return events;
    }

    private void ProcessContentBlockStart(
        WorkerSession session,
        Dictionary<string, object> eventData,
        List<ContentBlockReceivedData> currentContentBlocks)
    {
        if (!eventData.TryGetValue("content_block", out var blockObj))
            return;

        var index = GetIntValue(eventData, "index") ?? currentContentBlocks.Count;
        var blockJson = blockObj is JsonElement element ? element.GetRawText() : JsonSerializer.Serialize(blockObj);
        var blockData = JsonSerializer.Deserialize<Dictionary<string, object>>(blockJson);
        if (blockData == null) return;

        var blockType = GetStringValue(blockData, "type");
        ContentBlockReceivedData? content = blockType switch
        {
            "text" => new ContentBlockReceivedData
            {
                SessionId = session.Id,
                Type = "Text",
                Text = "",
                Index = index
            },
            "thinking" => new ContentBlockReceivedData
            {
                SessionId = session.Id,
                Type = "Thinking",
                Text = "",
                Index = index
            },
            "tool_use" => CreateToolUseContent(session, blockData, index),
            _ => null
        };

        if (content != null)
        {
            currentContentBlocks.Add(content);
        }
    }

    private void ProcessContentBlockDelta(
        Dictionary<string, object> eventData,
        List<ContentBlockReceivedData> currentContentBlocks)
    {
        if (!eventData.TryGetValue("delta", out var deltaObj))
            return;

        var index = GetIntValue(eventData, "index") ?? currentContentBlocks.Count - 1;
        if (index < 0 || index >= currentContentBlocks.Count)
            return;

        var block = currentContentBlocks[index];
        var deltaJson = deltaObj is JsonElement element ? element.GetRawText() : JsonSerializer.Serialize(deltaObj);
        var deltaData = JsonSerializer.Deserialize<Dictionary<string, object>>(deltaJson);
        if (deltaData == null) return;

        var deltaType = GetStringValue(deltaData, "type");

        switch (deltaType)
        {
            case "text_delta":
                block.Text = (block.Text ?? "") + (GetStringValue(deltaData, "text") ?? "");
                break;
            case "thinking_delta":
                block.Text = (block.Text ?? "") + (GetStringValue(deltaData, "thinking") ?? "");
                break;
            case "input_json_delta":
                block.ToolInput = (block.ToolInput ?? "") + (GetStringValue(deltaData, "partial_json") ?? "");
                break;
        }
    }

    private List<(string EventType, object Data)> ProcessContentBlockStop(
        WorkerSession session,
        Dictionary<string, object> eventData,
        List<ContentBlockReceivedData> currentContentBlocks)
    {
        var events = new List<(string EventType, object Data)>();
        var index = GetIntValue(eventData, "index") ?? currentContentBlocks.Count - 1;

        if (index < 0 || index >= currentContentBlocks.Count)
            return events;

        var block = currentContentBlocks[index];

        // Emit content block received
        events.Add((SseEventTypes.ContentBlockReceived, block));

        // Check for AskUserQuestion
        if (block.ToolName == "AskUserQuestion" && !string.IsNullOrEmpty(block.ToolInput))
        {
            var questionEvent = TryParseAskUserQuestion(session, block);
            if (questionEvent != null)
            {
                events.Add((SseEventTypes.QuestionReceived, questionEvent));
            }
        }

        return events;
    }

    private ContentBlockReceivedData CreateToolUseContent(
        WorkerSession session,
        Dictionary<string, object> blockData,
        int index)
    {
        var toolUseId = GetStringValue(blockData, "id") ?? "";
        var toolName = GetStringValue(blockData, "name") ?? "unknown";

        // Track tool use ID -> name mapping
        if (!string.IsNullOrEmpty(toolUseId))
        {
            var sessionToolUses = _sessionToolUses.GetOrAdd(session.Id, _ => new ConcurrentDictionary<string, string>());
            sessionToolUses[toolUseId] = toolName;
        }

        return new ContentBlockReceivedData
        {
            SessionId = session.Id,
            Type = "ToolUse",
            ToolName = toolName,
            ToolUseId = toolUseId,
            ToolInput = "",
            Index = index
        };
    }

    private List<(string EventType, object Data)> ProcessUserMessage(WorkerSession session, UserMessage userMsg)
    {
        var events = new List<(string EventType, object Data)>();

        if (userMsg.Content is not List<object> contentBlocks)
            return events;

        var toolResultContents = new List<ContentBlockReceivedData>();

        foreach (var block in contentBlocks)
        {
            if (block is ToolResultBlock toolResultBlock)
            {
                var toolName = GetToolNameForUseId(session.Id, toolResultBlock.ToolUseId);
                var isError = toolResultBlock.IsError ?? false;

                toolResultContents.Add(new ContentBlockReceivedData
                {
                    SessionId = session.Id,
                    Type = "ToolResult",
                    ToolName = toolName,
                    ToolUseId = toolResultBlock.ToolUseId,
                    ToolSuccess = !isError,
                    Text = toolResultBlock.Content?.ToString() ?? "",
                    Index = toolResultContents.Count
                });
            }
        }

        if (toolResultContents.Count > 0)
        {
            events.Add((SseEventTypes.MessageReceived, new MessageReceivedData
            {
                SessionId = session.Id,
                Role = "User",
                Content = toolResultContents
            }));
        }

        return events;
    }

    /// <summary>
    /// Converts AssistantMessage content blocks to ContentBlockReceivedData.
    /// Used when content comes directly in the message (not via streaming events),
    /// such as authentication errors.
    /// </summary>
    private List<ContentBlockReceivedData> ConvertAssistantContent(string sessionId, List<object> content)
    {
        var result = new List<ContentBlockReceivedData>();

        for (var i = 0; i < content.Count; i++)
        {
            var block = content[i];
            ContentBlockReceivedData? data = null;

            switch (block)
            {
                case TextBlock textBlock:
                    data = new ContentBlockReceivedData
                    {
                        SessionId = sessionId,
                        Type = "Text",
                        Text = textBlock.Text,
                        Index = i
                    };
                    break;

                case ThinkingBlock thinkingBlock:
                    data = new ContentBlockReceivedData
                    {
                        SessionId = sessionId,
                        Type = "Thinking",
                        Text = thinkingBlock.Thinking,
                        Index = i
                    };
                    break;

                case ToolUseBlock toolUseBlock:
                    data = new ContentBlockReceivedData
                    {
                        SessionId = sessionId,
                        Type = "ToolUse",
                        ToolName = toolUseBlock.Name,
                        ToolUseId = toolUseBlock.Id,
                        ToolInput = JsonSerializer.Serialize(toolUseBlock.Input),
                        Index = i
                    };
                    break;
            }

            if (data != null)
            {
                result.Add(data);
            }
        }

        return result;
    }

    private QuestionReceivedData? TryParseAskUserQuestion(WorkerSession session, ContentBlockReceivedData block)
    {
        try
        {
            var toolInput = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(block.ToolInput!);
            if (toolInput == null || !toolInput.TryGetValue("questions", out var questionsElement))
                return null;

            var questions = new List<UserQuestionData>();
            foreach (var questionElement in questionsElement.EnumerateArray())
            {
                var options = new List<QuestionOptionData>();
                if (questionElement.TryGetProperty("options", out var optionsElement))
                {
                    foreach (var optionElement in optionsElement.EnumerateArray())
                    {
                        options.Add(new QuestionOptionData
                        {
                            Label = optionElement.GetProperty("label").GetString() ?? "",
                            Description = optionElement.GetProperty("description").GetString() ?? ""
                        });
                    }
                }

                questions.Add(new UserQuestionData
                {
                    Question = questionElement.GetProperty("question").GetString() ?? "",
                    Header = questionElement.TryGetProperty("header", out var headerElement)
                        ? headerElement.GetString() ?? "" : "",
                    Options = options,
                    MultiSelect = questionElement.TryGetProperty("multiSelect", out var multiSelectElement)
                        && multiSelectElement.GetBoolean()
                });
            }

            if (questions.Count == 0)
                return null;

            return new QuestionReceivedData
            {
                SessionId = session.Id,
                QuestionId = Guid.NewGuid().ToString(),
                ToolUseId = block.ToolUseId ?? "",
                Questions = questions
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string GetToolNameForUseId(string sessionId, string toolUseId)
    {
        if (_sessionToolUses.TryGetValue(sessionId, out var toolUses) &&
            toolUses.TryGetValue(toolUseId, out var toolName))
        {
            return toolName;
        }
        return "unknown";
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

    public async ValueTask DisposeAsync()
    {
        foreach (var session in _sessions.Values)
        {
            if (session.Cts != null)
            {
                try
                {
                    await session.Cts.CancelAsync();
                    session.Cts.Dispose();
                }
                catch { }
            }
        }
        _sessions.Clear();
        _sessionToolUses.Clear();
    }
}
