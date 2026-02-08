using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Homespun.ClaudeAgentSdk;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Exceptions;
using Microsoft.Extensions.AI;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Local in-process implementation of agent execution using the ClaudeAgentSdk.
/// This is the default implementation that runs agents directly in the main process.
/// </summary>
public class LocalAgentExecutionService : IAgentExecutionService, IAsyncDisposable
{
    private readonly SessionOptionsFactory _optionsFactory;
    private readonly ILogger<LocalAgentExecutionService> _logger;

    private readonly ConcurrentDictionary<string, LocalSession> _sessions = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _sessionToolUses = new();
    private readonly ConcurrentDictionary<string, ClaudeSdkClient> _activeClients = new();
    private readonly ConcurrentDictionary<string, TaskCompletionSource<AgentAnswerRequest>> _pendingQuestions = new();

    private record LocalSession(
        string Id,
        string WorkingDirectory,
        SessionMode Mode,
        string Model,
        string? SystemPrompt,
        ClaudeAgentOptions Options,
        CancellationTokenSource Cts,
        DateTime CreatedAt)
    {
        public string? ConversationId { get; set; }
        public DateTime LastActivityAt { get; set; } = CreatedAt;
    }

    public LocalAgentExecutionService(
        SessionOptionsFactory optionsFactory,
        ILogger<LocalAgentExecutionService> logger)
    {
        _optionsFactory = optionsFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AgentEvent> StartSessionAsync(
        AgentStartRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid().ToString();
        var channel = System.Threading.Channels.Channel.CreateUnbounded<AgentEvent>();

        // Create per-session AIFunction for ask_user MCP tool
        var askUserFunction = CreateAskUserFunction(sessionId, channel);

        var options = _optionsFactory.Create(request.Mode, request.WorkingDirectory, request.Model,
            request.SystemPrompt, askUserFunction);
        var effectivePermissionMode = request.PermissionMode ?? PermissionMode.BypassPermissions;
        options.PermissionMode = effectivePermissionMode;
        if (effectivePermissionMode != PermissionMode.BypassPermissions)
        {
            options.PermissionPromptToolName = "stdio";
        }
        options.IncludePartialMessages = true;

        if (!string.IsNullOrEmpty(request.ResumeSessionId))
        {
            options.Resume = request.ResumeSessionId;
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var session = new LocalSession(
            sessionId,
            request.WorkingDirectory,
            request.Mode,
            request.Model,
            request.SystemPrompt,
            options,
            cts,
            DateTime.UtcNow)
        {
            ConversationId = request.ResumeSessionId
        };

        _sessions[sessionId] = session;
        _sessionToolUses[sessionId] = new ConcurrentDictionary<string, string>();

        _logger.LogInformation("Starting local session {SessionId} in {Mode} mode", sessionId, request.Mode);

        yield return new AgentSessionStartedEvent(sessionId, session.ConversationId);

        await foreach (var evt in ProcessMessagesAsync(session, request.Prompt, channel, cts.Token))
        {
            yield return evt;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AgentEvent> SendMessageAsync(
        AgentMessageRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(request.SessionId, out var session))
        {
            yield return new AgentErrorEvent(request.SessionId, $"Session {request.SessionId} not found", "SESSION_NOT_FOUND", false);
            yield break;
        }

        session.LastActivityAt = DateTime.UtcNow;

        var channel = System.Threading.Channels.Channel.CreateUnbounded<AgentEvent>();

        // Create per-query AIFunction for ask_user MCP tool
        var askUserFunction = CreateAskUserFunction(request.SessionId, channel);

        // Create options for this query
        var queryOptions = _optionsFactory.Create(session.Mode, session.WorkingDirectory,
            request.Model ?? session.Model, session.SystemPrompt, askUserFunction);
        var effectivePermissionMode = request.PermissionMode ?? PermissionMode.BypassPermissions;
        queryOptions.PermissionMode = effectivePermissionMode;
        if (effectivePermissionMode != PermissionMode.BypassPermissions)
        {
            queryOptions.PermissionPromptToolName = "stdio";
        }
        queryOptions.IncludePartialMessages = true;
        queryOptions.Resume = session.ConversationId;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Cts.Token);

        // Temporarily update session options for this query
        var originalOptions = session.Options;
        session = session with { Options = queryOptions };
        _sessions[request.SessionId] = session;

        await foreach (var evt in ProcessMessagesAsync(session, request.Message, channel, linkedCts.Token))
        {
            yield return evt;
        }

        // Restore original options
        session = session with { Options = originalOptions };
        _sessions[request.SessionId] = session;
    }

    /// <inheritdoc />
    public Task AnswerQuestionAsync(
        AgentAnswerRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_pendingQuestions.TryGetValue(request.SessionId, out var tcs))
        {
            _logger.LogWarning("No pending question for session {SessionId}", request.SessionId);
            return Task.CompletedTask;
        }

        // Complete the TCS with the answer - this unblocks the MCP tool handler
        tcs.TrySetResult(request);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            _logger.LogInformation("Stopping local session {SessionId}", sessionId);

            // Cancel any pending question TCS
            if (_pendingQuestions.TryRemove(sessionId, out var tcs))
            {
                tcs.TrySetCanceled();
            }

            session.Cts.Cancel();
            session.Cts.Dispose();
            _sessionToolUses.TryRemove(sessionId, out _);
            _activeClients.TryRemove(sessionId, out _);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task InterruptSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            _logger.LogInformation("Interrupting local session {SessionId}", sessionId);

            // Cancel any pending question TCS
            if (_pendingQuestions.TryRemove(sessionId, out var tcs))
            {
                tcs.TrySetCanceled();
            }

            // Cancel current execution
            session.Cts.Cancel();
            session.Cts.Dispose();

            // Replace with a fresh CTS so the next message works
            var newCts = new CancellationTokenSource();
            var updatedSession = session with { Cts = newCts };
            _sessions[sessionId] = updatedSession;

            _activeClients.TryRemove(sessionId, out _);

            // Do NOT remove from _sessions or _sessionToolUses - session stays alive
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<AgentSessionStatus?> GetSessionStatusAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            return Task.FromResult<AgentSessionStatus?>(new AgentSessionStatus(
                session.Id,
                session.WorkingDirectory,
                session.Mode,
                session.Model,
                session.ConversationId,
                session.CreatedAt,
                session.LastActivityAt
            ));
        }

        return Task.FromResult<AgentSessionStatus?>(null);
    }

    /// <inheritdoc />
    public async Task<string?> ReadFileFromAgentAsync(string sessionId, string filePath, CancellationToken cancellationToken = default)
    {
        // Local agents run in the same filesystem as the parent application,
        // so we can read files directly from disk.
        if (!File.Exists(filePath))
        {
            _logger.LogDebug("ReadFileFromAgentAsync: File not found at {Path}", filePath);
            return null;
        }

        try
        {
            return await File.ReadAllTextAsync(filePath, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ReadFileFromAgentAsync: Error reading file at {Path}", filePath);
            return null;
        }
    }

    /// <summary>
    /// Creates a per-session AIFunction for the ask_user MCP tool.
    /// The handler emits an AgentQuestionEvent to the channel and blocks until the user answers.
    /// </summary>
    private AIFunction CreateAskUserFunction(
        string sessionId,
        System.Threading.Channels.Channel<AgentEvent> channel)
    {
        return AskUserQuestionFunction.Create(async (questions, cancellationToken) =>
        {
            _logger.LogInformation("ask_user MCP tool invoked for session {SessionId} with {Count} questions",
                sessionId, questions.Count);

            // Convert to AgentQuestion format
            var agentQuestions = questions.Select(q => new AgentQuestion(
                q.Question,
                q.Header,
                q.Options.Select(o => new AgentQuestionOption(o.Label, o.Description)).ToList(),
                q.MultiSelect
            )).ToList();

            var toolUseId = Guid.NewGuid().ToString();
            var questionEvent = new AgentQuestionEvent(
                sessionId,
                Guid.NewGuid().ToString(),
                toolUseId,
                agentQuestions
            );

            // Emit the question event to the UI
            await channel.Writer.WriteAsync(questionEvent, cancellationToken);

            // Wait for the user to answer
            var tcs = new TaskCompletionSource<AgentAnswerRequest>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingQuestions[sessionId] = tcs;

            var answer = await tcs.Task;
            _pendingQuestions.TryRemove(sessionId, out _);

            return answer.Answers;
        });
    }

    private async IAsyncEnumerable<AgentEvent> ProcessMessagesAsync(
        LocalSession session,
        string prompt,
        System.Threading.Channels.Channel<AgentEvent> channel,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var currentContentBlocks = new List<AgentContentBlockEvent>();

        // Start processing in background task
        _ = Task.Run(async () =>
        {
            ClaudeSdkClient? client = null;
            try
            {
                client = new ClaudeSdkClient(session.Options);
                _activeClients[session.Id] = client;

                // Connect in streaming mode (null prompt) then send query
                await client.ConnectAsync(null, cancellationToken);
                await client.QueryAsync(prompt, session.ConversationId, cancellationToken);

                await foreach (var msg in client.ReceiveMessagesAsync().WithCancellation(cancellationToken))
                {
                    // Handle control_request messages (permission prompts delegated via --permission-prompt-tool stdio)
                    if (msg is ControlRequest controlRequest)
                    {
                        await HandleControlRequestAsync(controlRequest, client, cancellationToken);
                        continue;
                    }

                    var events = ProcessSdkMessage(session, msg, currentContentBlocks);
                    foreach (var evt in events)
                    {
                        await channel.Writer.WriteAsync(evt, cancellationToken);

                        if (evt is AgentResultEvent resultEvt)
                        {
                            session.ConversationId = resultEvt.ConversationId;
                            _sessions[session.Id] = session;

                            await channel.Writer.WriteAsync(new AgentSessionEndedEvent(session.Id, "completed"), cancellationToken);
                            channel.Writer.Complete();
                            return;
                        }
                    }
                }
                channel.Writer.Complete();
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Local session {SessionId} cancelled", session.Id);
                await channel.Writer.WriteAsync(new AgentSessionEndedEvent(session.Id, "cancelled"));
                channel.Writer.Complete();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in local session {SessionId}", session.Id);
                await channel.Writer.WriteAsync(new AgentErrorEvent(session.Id, ex.Message, "AGENT_ERROR", false));
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

        // Yield events from channel
        await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return evt;
        }
    }

    private List<AgentEvent> ProcessSdkMessage(
        LocalSession session,
        Message sdkMessage,
        List<AgentContentBlockEvent> currentContentBlocks)
    {
        var events = new List<AgentEvent>();

        switch (sdkMessage)
        {
            case StreamEvent streamEvent:
                var streamEvents = ProcessStreamEvent(session, streamEvent, currentContentBlocks);
                events.AddRange(streamEvents);
                break;

            case AssistantMessage assistantMsg:
                // Content already processed via streaming
                if (currentContentBlocks.Count > 0)
                {
                    events.Add(new AgentMessageEvent(session.Id, ClaudeMessageRole.Assistant,
                        new List<AgentContentBlockEvent>(currentContentBlocks)));
                    currentContentBlocks.Clear();
                }
                break;

            case ResultMessage resultMsg:
                events.Add(new AgentResultEvent(
                    session.Id,
                    (decimal)(resultMsg.TotalCostUsd ?? 0),
                    resultMsg.DurationMs,
                    resultMsg.SessionId
                ));
                break;

            case UserMessage userMsg:
                var toolEvents = ProcessUserMessage(session, userMsg);
                events.AddRange(toolEvents);
                break;
        }

        return events;
    }

    private List<AgentEvent> ProcessStreamEvent(
        LocalSession session,
        StreamEvent streamEvent,
        List<AgentContentBlockEvent> currentContentBlocks)
    {
        var events = new List<AgentEvent>();

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
        LocalSession session,
        Dictionary<string, object> eventData,
        List<AgentContentBlockEvent> currentContentBlocks)
    {
        if (!eventData.TryGetValue("content_block", out var blockObj))
            return;

        var index = GetIntValue(eventData, "index") ?? currentContentBlocks.Count;
        var blockJson = blockObj is JsonElement element ? element.GetRawText() : JsonSerializer.Serialize(blockObj);
        var blockData = JsonSerializer.Deserialize<Dictionary<string, object>>(blockJson);
        if (blockData == null) return;

        var blockType = GetStringValue(blockData, "type");
        AgentContentBlockEvent? content = blockType switch
        {
            "text" => new AgentContentBlockEvent(session.Id, ClaudeContentType.Text, "", null, null, null, null, index),
            "thinking" => new AgentContentBlockEvent(session.Id, ClaudeContentType.Thinking, "", null, null, null, null, index),
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
        List<AgentContentBlockEvent> currentContentBlocks)
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
                currentContentBlocks[index] = block with { Text = (block.Text ?? "") + (GetStringValue(deltaData, "text") ?? "") };
                break;
            case "thinking_delta":
                currentContentBlocks[index] = block with { Text = (block.Text ?? "") + (GetStringValue(deltaData, "thinking") ?? "") };
                break;
            case "input_json_delta":
                currentContentBlocks[index] = block with { ToolInput = (block.ToolInput ?? "") + (GetStringValue(deltaData, "partial_json") ?? "") };
                break;
        }
    }

    private List<AgentEvent> ProcessContentBlockStop(
        LocalSession session,
        Dictionary<string, object> eventData,
        List<AgentContentBlockEvent> currentContentBlocks)
    {
        var events = new List<AgentEvent>();
        var index = GetIntValue(eventData, "index") ?? currentContentBlocks.Count - 1;

        if (index < 0 || index >= currentContentBlocks.Count)
            return events;

        var block = currentContentBlocks[index];
        events.Add(block);

        return events;
    }

    private AgentContentBlockEvent CreateToolUseContent(
        LocalSession session,
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

        return new AgentContentBlockEvent(session.Id, ClaudeContentType.ToolUse, null, toolName, "", toolUseId, null, index);
    }

    private List<AgentEvent> ProcessUserMessage(LocalSession session, UserMessage userMsg)
    {
        var events = new List<AgentEvent>();

        if (userMsg.Content is not List<object> contentBlocks)
            return events;

        var toolResultContents = new List<AgentContentBlockEvent>();

        foreach (var block in contentBlocks)
        {
            if (block is ToolResultBlock toolResultBlock)
            {
                var toolName = GetToolNameForUseId(session.Id, toolResultBlock.ToolUseId);
                var isError = toolResultBlock.IsError ?? false;

                toolResultContents.Add(new AgentContentBlockEvent(
                    session.Id,
                    ClaudeContentType.ToolResult,
                    toolResultBlock.Content?.ToString() ?? "",
                    toolName,
                    null,
                    toolResultBlock.ToolUseId,
                    !isError,
                    toolResultContents.Count
                ));
            }
        }

        if (toolResultContents.Count > 0)
        {
            events.Add(new AgentMessageEvent(session.Id, ClaudeMessageRole.User, toolResultContents));
        }

        return events;
    }

    /// <summary>
    /// Handles a control_request from the CLI (permission prompt delegated via --permission-prompt-tool stdio).
    /// Auto-approves all tools. AskUserQuestion is handled via the custom MCP tool instead.
    /// </summary>
    private async Task HandleControlRequestAsync(
        ControlRequest controlRequest,
        ClaudeSdkClient client,
        CancellationToken cancellationToken)
    {
        var requestId = controlRequest.RequestId;
        if (string.IsNullOrEmpty(requestId))
        {
            _logger.LogWarning("ControlRequest missing request_id, cannot respond");
            return;
        }

        var toolName = GetControlRequestToolName(controlRequest);
        _logger.LogDebug("Auto-approving control request for tool {ToolName}", toolName);
        var originalInput = ExtractOriginalInputAsObjectDictionary(controlRequest);
        await client.SendControlResponseAsync(requestId, "allow", originalInput, cancellationToken: cancellationToken);
    }

    private static string? GetControlRequestToolName(ControlRequest controlRequest)
    {
        if (controlRequest.Data == null)
            return null;

        if (controlRequest.Data.TryGetValue("tool_name", out var toolNameObj))
        {
            return toolNameObj is JsonElement element ? element.GetString() : toolNameObj?.ToString();
        }

        return null;
    }

    private static Dictionary<string, object>? ExtractOriginalInputAsObjectDictionary(ControlRequest controlRequest)
    {
        if (controlRequest.Data == null)
            return null;

        if (controlRequest.Data.TryGetValue("input", out var inputObj) && inputObj is JsonElement inputElement)
        {
            var input = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(inputElement.GetRawText());
            if (input == null) return null;
            var result = new Dictionary<string, object>();
            foreach (var kvp in input)
                result[kvp.Key] = kvp.Value;
            return result;
        }

        return null;
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
            try
            {
                session.Cts.Cancel();
                session.Cts.Dispose();
            }
            catch { }
        }
        _sessions.Clear();
        _sessionToolUses.Clear();
        _activeClients.Clear();
    }
}
