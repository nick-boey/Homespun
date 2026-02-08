using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Homespun.ClaudeAgentSdk;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Exceptions;

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

        var options = _optionsFactory.Create(request.Mode, request.WorkingDirectory, request.Model, request.SystemPrompt);
        options.PermissionMode = PermissionMode.BypassPermissions;
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

        await foreach (var evt in ProcessMessagesAsync(session, request.Prompt, cts.Token))
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

        // Create options for this query
        var queryOptions = _optionsFactory.Create(session.Mode, session.WorkingDirectory,
            request.Model ?? session.Model, session.SystemPrompt);
        queryOptions.PermissionMode = PermissionMode.BypassPermissions;
        queryOptions.IncludePartialMessages = true;
        queryOptions.Resume = session.ConversationId;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Cts.Token);

        // Temporarily update session options for this query
        var originalOptions = session.Options;
        session = session with { Options = queryOptions };
        _sessions[request.SessionId] = session;

        await foreach (var evt in ProcessMessagesAsync(session, request.Message, linkedCts.Token))
        {
            yield return evt;
        }

        // Restore original options
        session = session with { Options = originalOptions };
        _sessions[request.SessionId] = session;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AgentEvent> AnswerQuestionAsync(
        AgentAnswerRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Format the answers as a message
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

        var messageRequest = new AgentMessageRequest(request.SessionId, formattedAnswers.ToString().Trim());

        await foreach (var evt in SendMessageAsync(messageRequest, cancellationToken))
        {
            yield return evt;
        }
    }

    /// <inheritdoc />
    public Task StopSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            _logger.LogInformation("Stopping local session {SessionId}", sessionId);
            session.Cts.Cancel();
            session.Cts.Dispose();
            _sessionToolUses.TryRemove(sessionId, out _);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task InterruptSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            _logger.LogInformation("Interrupting local session {SessionId}", sessionId);

            // Cancel current execution
            session.Cts.Cancel();
            session.Cts.Dispose();

            // Replace with a fresh CTS so the next message works
            var newCts = new CancellationTokenSource();
            var updatedSession = session with { Cts = newCts };
            _sessions[sessionId] = updatedSession;

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

    /// <inheritdoc />
    public Task<IReadOnlyList<AgentSessionStatus>> ListSessionsAsync(CancellationToken cancellationToken = default)
    {
        var statuses = _sessions.Values.Select(session => new AgentSessionStatus(
            session.Id,
            session.WorkingDirectory,
            session.Mode,
            session.Model,
            session.ConversationId,
            session.CreatedAt,
            session.LastActivityAt
        )).ToList().AsReadOnly();

        return Task.FromResult<IReadOnlyList<AgentSessionStatus>>(statuses);
    }

    /// <inheritdoc />
    public Task<int> CleanupOrphanedContainersAsync(CancellationToken cancellationToken = default)
    {
        // Local agents run in-process, no containers to orphan
        return Task.FromResult(0);
    }

    private async IAsyncEnumerable<AgentEvent> ProcessMessagesAsync(
        LocalSession session,
        string prompt,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var currentContentBlocks = new List<AgentContentBlockEvent>();
        var channel = System.Threading.Channels.Channel.CreateUnbounded<AgentEvent>();

        // Start processing in background task
        _ = Task.Run(async () =>
        {
            try
            {
                await using var client = new ClaudeSdkClient(session.Options);
                await client.ConnectAsync(prompt, cancellationToken);

                await foreach (var msg in client.ReceiveMessagesAsync().WithCancellation(cancellationToken))
                {
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

        // Check for AskUserQuestion
        if (block.ToolName == "AskUserQuestion" && !string.IsNullOrEmpty(block.ToolInput))
        {
            var questionEvent = TryParseAskUserQuestion(session, block);
            if (questionEvent != null)
            {
                events.Add(questionEvent);
            }
        }

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

    private AgentQuestionEvent? TryParseAskUserQuestion(LocalSession session, AgentContentBlockEvent block)
    {
        try
        {
            var toolInput = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(block.ToolInput!);
            if (toolInput == null || !toolInput.TryGetValue("questions", out var questionsElement))
                return null;

            var questions = new List<AgentQuestion>();
            foreach (var questionElement in questionsElement.EnumerateArray())
            {
                var options = new List<AgentQuestionOption>();
                if (questionElement.TryGetProperty("options", out var optionsElement))
                {
                    foreach (var optionElement in optionsElement.EnumerateArray())
                    {
                        options.Add(new AgentQuestionOption(
                            optionElement.GetProperty("label").GetString() ?? "",
                            optionElement.GetProperty("description").GetString() ?? ""
                        ));
                    }
                }

                questions.Add(new AgentQuestion(
                    questionElement.GetProperty("question").GetString() ?? "",
                    questionElement.TryGetProperty("header", out var headerElement)
                        ? headerElement.GetString() ?? "" : "",
                    options,
                    questionElement.TryGetProperty("multiSelect", out var multiSelectElement)
                        && multiSelectElement.GetBoolean()
                ));
            }

            if (questions.Count == 0)
                return null;

            return new AgentQuestionEvent(
                session.Id,
                Guid.NewGuid().ToString(),
                block.ToolUseId ?? "",
                questions
            );
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
            try
            {
                session.Cts.Cancel();
                session.Cts.Dispose();
            }
            catch { }
        }
        _sessions.Clear();
        _sessionToolUses.Clear();
    }
}
