using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Homespun.AgentWorker.Models;
using Homespun.ClaudeAgentSdk;
using Microsoft.Extensions.AI;

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
    private readonly ConcurrentDictionary<string, ClaudeSdkClient> _activeClients = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<AnswerQuestionRequest>> _pendingQuestions = new();

    /// <summary>
    /// Maximum buffer size for JSON message streaming (10MB).
    /// </summary>
    private const int DefaultMaxBufferSize = 10 * 1024 * 1024;

    /// <summary>
    /// Read-only tools available in Plan mode.
    /// Uses the custom MCP tool name instead of built-in AskUserQuestion.
    /// </summary>
    private static readonly string[] PlanModeTools =
    [
        "Read", "Glob", "Grep", "WebFetch", "WebSearch",
        "Task", "mcp__homespun__ask_user", "ExitPlanMode"
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

        var channel = System.Threading.Channels.Channel.CreateUnbounded<(string EventType, object Data)>();

        var options = CreateOptions(session, channel);
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
        await foreach (var evt in ProcessMessagesAsync(session, request.Prompt, channel, session.Cts.Token))
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

        var channel = System.Threading.Channels.Channel.CreateUnbounded<(string EventType, object Data)>();

        // Create options for this query
        var queryOptions = CreateOptions(session, channel);
        queryOptions.PermissionMode = PermissionMode.BypassPermissions;
        queryOptions.IncludePartialMessages = true;
        queryOptions.Resume = session.ConversationId;

        if (!string.IsNullOrEmpty(request.Model))
        {
            queryOptions.Model = request.Model;
        }

        session.Options = queryOptions;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Cts?.Token ?? CancellationToken.None);

        await foreach (var evt in ProcessMessagesAsync(session, request.Message, channel, linkedCts.Token))
        {
            yield return evt;
        }
    }

    /// <summary>
    /// Answers a pending question by signaling the paused MCP tool handler.
    /// Events continue flowing through the original StartSessionAsync/SendMessageAsync stream.
    /// </summary>
    public Task AnswerQuestionAsync(
        string sessionId,
        AnswerQuestionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_pendingQuestions.TryGetValue(sessionId, out var tcs))
        {
            _logger.LogWarning("No pending question for session {SessionId}", sessionId);
            return Task.CompletedTask;
        }

        // Complete the TCS with the answer - this unblocks the MCP tool handler
        tcs.TrySetResult(request);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Stops a session.
    /// </summary>
    public async Task StopSessionAsync(string sessionId)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            _logger.LogInformation("Stopping session {SessionId}", sessionId);

            // Cancel any pending question TCS
            if (_pendingQuestions.TryRemove(sessionId, out var tcs))
            {
                tcs.TrySetCanceled();
            }

            if (session.Cts != null)
            {
                await session.Cts.CancelAsync();
                session.Cts.Dispose();
            }

            _sessionToolUses.TryRemove(sessionId, out _);
            _activeClients.TryRemove(sessionId, out _);
        }
    }

    /// <summary>
    /// Gets information about a session.
    /// </summary>
    public WorkerSession? GetSession(string sessionId)
    {
        return _sessions.TryGetValue(sessionId, out var session) ? session : null;
    }

    /// <summary>
    /// Creates a per-session AIFunction for the ask_user MCP tool.
    /// The handler emits a QuestionReceived SSE event and blocks until the user answers.
    /// </summary>
    private AIFunction CreateAskUserFunction(
        string sessionId,
        System.Threading.Channels.Channel<(string EventType, object Data)> channel)
    {
        return AskUserQuestionFunction.Create(async (questions, cancellationToken) =>
        {
            _logger.LogInformation("ask_user MCP tool invoked for session {SessionId} with {Count} questions",
                sessionId, questions.Count);

            // Convert to QuestionReceivedData format
            var questionsList = questions.Select(q => new UserQuestionData
            {
                Question = q.Question,
                Header = q.Header,
                Options = q.Options.Select(o => new QuestionOptionData
                {
                    Label = o.Label,
                    Description = o.Description
                }).ToList(),
                MultiSelect = q.MultiSelect
            }).ToList();

            var toolUseId = Guid.NewGuid().ToString();
            var questionEvent = new QuestionReceivedData
            {
                SessionId = sessionId,
                QuestionId = Guid.NewGuid().ToString(),
                ToolUseId = toolUseId,
                Questions = questionsList
            };

            // Emit the question event via SSE
            await channel.Writer.WriteAsync((SseEventTypes.QuestionReceived, (object)questionEvent), cancellationToken);

            // Wait for the user to answer
            var tcs = new TaskCompletionSource<AnswerQuestionRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingQuestions[sessionId] = tcs;

            var answer = await tcs.Task;
            _pendingQuestions.TryRemove(sessionId, out _);

            return answer.Answers;
        });
    }

    private ClaudeAgentOptions CreateOptions(
        WorkerSession session,
        System.Threading.Channels.Channel<(string EventType, object Data)> channel)
    {
        var askUserFunction = CreateAskUserFunction(session.Id, channel);

        var mcpServers = new Dictionary<string, object>
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
        };

        // Add homespun MCP server with ask_user tool
        var converter = new AIFunctionMcpConverter([askUserFunction], "homespun");
        mcpServers["homespun"] = converter.CreateMcpServerConfig();

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
            McpServers = mcpServers,
            DisallowedTools = ["AskUserQuestion"]
        };

        // Pass git identity and GitHub token to the Claude agent subprocess
        options.Env = BuildAgentEnvironment();

        if (session.Mode == SessionMode.Plan)
        {
            options.AllowedTools = PlanModeTools.ToList();
        }

        return options;
    }

    /// <summary>
    /// Builds the environment variables to pass to the Claude agent subprocess.
    /// Includes git identity for commits and GitHub token for authentication.
    /// </summary>
    private static Dictionary<string, string> BuildAgentEnvironment()
    {
        var env = new Dictionary<string, string>();

        // Git identity for commits
        var gitAuthorName = Environment.GetEnvironmentVariable("GIT_AUTHOR_NAME") ?? "Homespun Bot";
        var gitAuthorEmail = Environment.GetEnvironmentVariable("GIT_AUTHOR_EMAIL") ?? "homespun@localhost";
        env["GIT_AUTHOR_NAME"] = gitAuthorName;
        env["GIT_AUTHOR_EMAIL"] = gitAuthorEmail;
        env["GIT_COMMITTER_NAME"] = gitAuthorName;
        env["GIT_COMMITTER_EMAIL"] = gitAuthorEmail;

        // GitHub token for gh CLI and git authentication
        var githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN")
            ?? Environment.GetEnvironmentVariable("GitHub__Token");

        if (!string.IsNullOrEmpty(githubToken))
        {
            env["GITHUB_TOKEN"] = githubToken;
            env["GH_TOKEN"] = githubToken;
        }

        return env;
    }

    private async IAsyncEnumerable<(string EventType, object Data)> ProcessMessagesAsync(
        WorkerSession session,
        string prompt,
        System.Threading.Channels.Channel<(string EventType, object Data)> channel,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var currentContentBlocks = new List<ContentBlockReceivedData>();

        _ = Task.Run(async () =>
        {
            ClaudeSdkClient? client = null;
            try
            {
                client = new ClaudeSdkClient(session.Options!);
                _activeClients[session.Id] = client;

                // Connect in streaming mode (null prompt) then send query
                await client.ConnectAsync(null, cancellationToken);
                await client.QueryAsync(prompt, session.ConversationId ?? "default", cancellationToken);

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
            finally
            {
                _activeClients.TryRemove(session.Id, out _);
                _pendingQuestions.TryRemove(session.Id, out _);
                if (client != null)
                {
                    await client.DisposeAsync();
                }
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
                List<ContentBlockReceivedData> contentToEmit;

                if (currentContentBlocks.Count > 0)
                {
                    contentToEmit = new List<ContentBlockReceivedData>(currentContentBlocks);
                    currentContentBlocks.Clear();
                }
                else if (assistantMsg.Content.Count > 0)
                {
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

        return events;
    }

    private ContentBlockReceivedData CreateToolUseContent(
        WorkerSession session,
        Dictionary<string, object> blockData,
        int index)
    {
        var toolUseId = GetStringValue(blockData, "id") ?? "";
        var toolName = GetStringValue(blockData, "name") ?? "unknown";

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
        // Cancel all pending questions
        foreach (var tcs in _pendingQuestions.Values)
        {
            tcs.TrySetCanceled();
        }
        _pendingQuestions.Clear();

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
        _activeClients.Clear();
    }
}
