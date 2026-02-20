using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Homespun.ClaudeAgentSdk;
using Homespun.Features.ClaudeCode.Exceptions;
using SdkPermissionMode = Homespun.ClaudeAgentSdk.PermissionMode;
using SharedPermissionMode = Homespun.Shared.Models.Sessions.PermissionMode;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Local in-process implementation of agent execution using the ClaudeAgentSdk.
/// Converts C# SDK Message types to SdkMessage records for the consumer.
/// </summary>
public class LocalAgentExecutionService : IAgentExecutionService, IAsyncDisposable
{
    private readonly SessionOptionsFactory _optionsFactory;
    private readonly ILogger<LocalAgentExecutionService> _logger;

    private readonly ConcurrentDictionary<string, LocalSession> _sessions = new();

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
    public async IAsyncEnumerable<SdkMessage> StartSessionAsync(
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

        _logger.LogInformation("Starting local session {SessionId} in {Mode} mode", sessionId, request.Mode);

        // Emit a synthetic system message with the session ID
        yield return new SdkSystemMessage(sessionId, null, "session_started", request.Model, null);

        await foreach (var msg in ProcessMessagesAsync(session, request.Prompt, cts.Token))
        {
            yield return msg;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SdkMessage> SendMessageAsync(
        AgentMessageRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(request.SessionId, out var session))
        {
            _logger.LogError("Session {SessionId} not found", request.SessionId);
            yield break;
        }

        session.LastActivityAt = DateTime.UtcNow;

        // Create options for this query, using the permission mode from the request
        var queryOptions = _optionsFactory.Create(session.Mode, session.WorkingDirectory,
            request.Model ?? session.Model, session.SystemPrompt);
        queryOptions.PermissionMode = MapPermissionMode(request.PermissionMode);
        queryOptions.IncludePartialMessages = true;
        queryOptions.Resume = session.ConversationId;

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Cts.Token);

        // Temporarily update session options for this query
        var originalOptions = session.Options;
        session = session with { Options = queryOptions };
        _sessions[request.SessionId] = session;

        await foreach (var msg in ProcessMessagesAsync(session, request.Message, linkedCts.Token))
        {
            yield return msg;
        }

        // Restore original options
        session = session with { Options = originalOptions };
        _sessions[request.SessionId] = session;
    }

    /// <inheritdoc />
    public Task StopSessionAsync(string sessionId, bool forceStopContainer = false, CancellationToken cancellationToken = default)
    {
        // forceStopContainer is ignored for local execution (no containers)
        if (_sessions.TryRemove(sessionId, out var session))
        {
            _logger.LogInformation("Stopping local session {SessionId}", sessionId);
            session.Cts.Cancel();
            session.Cts.Dispose();
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

    /// <inheritdoc />
    public Task<bool> AnswerQuestionAsync(string sessionId, Dictionary<string, string> answers,
        CancellationToken cancellationToken = default)
    {
        // Local mode answers go through SendMessageAsync, not via worker HTTP
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<bool> ApprovePlanAsync(string sessionId, bool approved, bool keepContext, string? feedback = null,
        CancellationToken cancellationToken = default)
    {
        // Local mode plan approval uses ExecutePlanAsync fallback in ClaudeSessionService
        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public Task<CloneContainerState?> GetCloneContainerStateAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        // Local mode runs in-process, no container tracking needed
        return Task.FromResult<CloneContainerState?>(null);
    }

    /// <inheritdoc />
    public Task TerminateCloneSessionAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        // Local mode runs in-process, terminate by stopping sessions with matching working directory
        var sessionsToStop = _sessions.Values
            .Where(s => s.WorkingDirectory == workingDirectory)
            .Select(s => s.Id)
            .ToList();

        foreach (var sessionId in sessionsToStop)
        {
            if (_sessions.TryRemove(sessionId, out var session))
            {
                session.Cts.Cancel();
                session.Cts.Dispose();
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(CancellationToken cancellationToken = default)
    {
        // Local mode runs in-process, no containers to list
        return Task.FromResult<IReadOnlyList<ContainerInfo>>(Array.Empty<ContainerInfo>());
    }

    /// <inheritdoc />
    public Task<bool> StopContainerByIdAsync(string containerId, CancellationToken cancellationToken = default)
    {
        // Local mode runs in-process, no containers to stop
        return Task.FromResult(false);
    }

    private async IAsyncEnumerable<SdkMessage> ProcessMessagesAsync(
        LocalSession session,
        string prompt,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var channel = System.Threading.Channels.Channel.CreateUnbounded<SdkMessage>();

        // Start processing in background task
        _ = Task.Run(async () =>
        {
            try
            {
                await using var client = new ClaudeSdkClient(session.Options);
                await client.ConnectAsync(prompt, cancellationToken);

                await foreach (var msg in client.ReceiveMessagesAsync().WithCancellation(cancellationToken))
                {
                    var sdkMsg = ConvertToSdkMessage(session, msg);
                    if (sdkMsg != null)
                    {
                        await channel.Writer.WriteAsync(sdkMsg, cancellationToken);

                        if (sdkMsg is SdkResultMessage resultMsg)
                        {
                            session.ConversationId = resultMsg.SessionId;
                            _sessions[session.Id] = session;
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
                channel.Writer.Complete();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in local session {SessionId}", session.Id);
                channel.Writer.Complete(ex);
            }
        }, cancellationToken);

        // Yield events from channel
        await foreach (var msg in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return msg;
        }
    }

    /// <summary>
    /// Converts a C# SDK Message to an SdkMessage record.
    /// This is a simple mapping — all content block assembly and question parsing
    /// happens in the consumer (ClaudeSessionService).
    /// </summary>
    private SdkMessage? ConvertToSdkMessage(LocalSession session, Message sdkMessage)
    {
        switch (sdkMessage)
        {
            case StreamEvent streamEvent:
            {
                // Convert the Event dictionary to a JsonElement
                JsonElement? eventElement = null;
                if (streamEvent.Event != null)
                {
                    var json = JsonSerializer.Serialize(streamEvent.Event);
                    eventElement = JsonDocument.Parse(json).RootElement;
                }

                return new SdkStreamEvent(
                    session.Id,
                    null,
                    eventElement,
                    null
                );
            }

            case AssistantMessage assistantMsg:
            {
                var content = ConvertContentBlocks(assistantMsg.Content);
                var apiMessage = new SdkApiMessage("assistant", content);
                return new SdkAssistantMessage(session.Id, null, apiMessage, null);
            }

            case ResultMessage resultMsg:
            {
                return new SdkResultMessage(
                    SessionId: resultMsg.SessionId ?? session.Id,
                    Uuid: null,
                    Subtype: null,
                    DurationMs: resultMsg.DurationMs,
                    DurationApiMs: 0,
                    IsError: false,
                    NumTurns: 0,
                    TotalCostUsd: (decimal)(resultMsg.TotalCostUsd ?? 0),
                    Result: null
                );
            }

            case UserMessage userMsg:
            {
                var content = ConvertUserContentBlocks(userMsg.Content);
                var apiMessage = new SdkApiMessage("user", content);
                return new SdkUserMessage(session.Id, null, apiMessage, null);
            }

            default:
                return null;
        }
    }

    private static List<SdkContentBlock> ConvertContentBlocks(object? content)
    {
        var blocks = new List<SdkContentBlock>();
        if (content is not List<object> contentBlocks) return blocks;

        foreach (var block in contentBlocks)
        {
            var sdkBlock = ConvertSingleContentBlock(block);
            if (sdkBlock != null) blocks.Add(sdkBlock);
        }

        return blocks;
    }

    private static List<SdkContentBlock> ConvertUserContentBlocks(object? content)
    {
        var blocks = new List<SdkContentBlock>();
        if (content is not List<object> contentBlocks) return blocks;

        foreach (var block in contentBlocks)
        {
            if (block is ToolResultBlock toolResult)
            {
                var contentJson = toolResult.Content != null
                    ? JsonDocument.Parse(JsonSerializer.Serialize(toolResult.Content)).RootElement
                    : default;

                blocks.Add(new SdkToolResultBlock(
                    toolResult.ToolUseId,
                    contentJson,
                    toolResult.IsError
                ));
            }
            else
            {
                var sdkBlock = ConvertSingleContentBlock(block);
                if (sdkBlock != null) blocks.Add(sdkBlock);
            }
        }

        return blocks;
    }

    private static SdkContentBlock? ConvertSingleContentBlock(object block)
    {
        // The C# SDK uses dynamic types; serialize to JSON and extract fields
        var json = JsonSerializer.Serialize(block);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("type", out var typeElement))
            return null;

        var type = typeElement.GetString();

        return type switch
        {
            "text" => new SdkTextBlock(
                root.TryGetProperty("text", out var text) ? text.GetString() : null),
            "thinking" => new SdkThinkingBlock(
                root.TryGetProperty("thinking", out var thinking) ? thinking.GetString() : null),
            "tool_use" => new SdkToolUseBlock(
                root.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                root.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                root.TryGetProperty("input", out var input) ? input.Clone() : default),
            _ => null
        };
    }

    /// <summary>
    /// Maps shared permission mode to SDK permission mode.
    /// </summary>
    private static SdkPermissionMode MapPermissionMode(SharedPermissionMode mode)
    {
        return mode switch
        {
            SharedPermissionMode.Default => SdkPermissionMode.Default,
            SharedPermissionMode.AcceptEdits => SdkPermissionMode.AcceptEdits,
            SharedPermissionMode.Plan => SdkPermissionMode.Plan,
            SharedPermissionMode.BypassPermissions => SdkPermissionMode.BypassPermissions,
            _ => SdkPermissionMode.BypassPermissions
        };
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
    }
}
