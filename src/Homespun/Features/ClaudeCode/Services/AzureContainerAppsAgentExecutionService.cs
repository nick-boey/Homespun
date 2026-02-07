using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Exceptions;
using Microsoft.Extensions.Options;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Configuration options for Azure Container Apps agent execution.
/// Routes requests to a worker container app that manages sessions internally.
/// </summary>
public class AzureContainerAppsAgentExecutionOptions
{
    public const string SectionName = "AgentExecution:AzureContainerApps";

    /// <summary>
    /// Base URL of the worker container app (e.g., http://ca-worker-homespun-dev.internal.region.azurecontainerapps.io).
    /// </summary>
    public string WorkerEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// Timeout for HTTP requests to the worker.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Maximum session duration before automatic termination.
    /// </summary>
    public TimeSpan MaxSessionDuration { get; set; } = TimeSpan.FromMinutes(30);
}

/// <summary>
/// Azure Container Apps implementation for agent execution.
/// Routes all requests directly to a worker container app via HTTP.
/// The worker manages sessions internally via WorkerSessionService.
/// </summary>
public class AzureContainerAppsAgentExecutionService : IAgentExecutionService, IAsyncDisposable
{
    private readonly AzureContainerAppsAgentExecutionOptions _options;
    private readonly ILogger<AzureContainerAppsAgentExecutionService> _logger;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, WorkerSession> _sessions = new();

    private record WorkerSession(
        string SessionId,
        string? WorkerSessionId,
        CancellationTokenSource Cts,
        DateTime CreatedAt)
    {
        public string? ConversationId { get; set; }
        public DateTime LastActivityAt { get; set; } = CreatedAt;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AzureContainerAppsAgentExecutionService(
        IOptions<AzureContainerAppsAgentExecutionOptions> options,
        ILogger<AzureContainerAppsAgentExecutionService> logger)
    {
        _options = options.Value;
        _logger = logger;
        _httpClient = new HttpClient
        {
            Timeout = _options.RequestTimeout
        };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AgentEvent> StartSessionAsync(
        AgentStartRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid().ToString();

        _logger.LogInformation("Starting worker session {SessionId} via {WorkerEndpoint}",
            sessionId, _options.WorkerEndpoint);

        var channel = System.Threading.Channels.Channel.CreateUnbounded<AgentEvent>();

        _ = Task.Run(async () =>
        {
            try
            {
                var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var session = new WorkerSession(sessionId, null, cts, DateTime.UtcNow);
                _sessions[sessionId] = session;

                // Start the agent session in the worker
                var startRequest = new
                {
                    WorkingDirectory = request.WorkingDirectory,
                    Mode = request.Mode.ToString(),
                    Model = request.Model,
                    Prompt = request.Prompt,
                    SystemPrompt = request.SystemPrompt,
                    ResumeSessionId = request.ResumeSessionId
                };

                var workerUrl = $"{_options.WorkerEndpoint.TrimEnd('/')}/api/sessions";

                await foreach (var evt in SendSseRequestAsync(workerUrl, startRequest, sessionId, cts.Token))
                {
                    if (evt is AgentSessionStartedEvent startedEvt)
                    {
                        session = session with { WorkerSessionId = startedEvt.SessionId };
                        _sessions[sessionId] = session;
                    }

                    await channel.Writer.WriteAsync(MapSessionId(evt, sessionId), cancellationToken);

                    if (evt is AgentResultEvent resultEvt)
                    {
                        session.ConversationId = resultEvt.ConversationId;
                        _sessions[sessionId] = session;
                    }
                }
                channel.Writer.Complete();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in worker session {SessionId}", sessionId);

                await channel.Writer.WriteAsync(
                    new AgentErrorEvent(sessionId, ex.Message, "WORKER_ERROR", ex is AgentStartupException));
                channel.Writer.Complete(ex);
            }
            catch (OperationCanceledException)
            {
                channel.Writer.Complete();
            }
        }, cancellationToken);

        await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken))
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

        if (string.IsNullOrEmpty(session.WorkerSessionId))
        {
            yield return new AgentErrorEvent(request.SessionId, "Worker session not initialized", "WORKER_NOT_READY", false);
            yield break;
        }

        session.LastActivityAt = DateTime.UtcNow;

        var messageRequest = new
        {
            Message = request.Message,
            Model = request.Model
        };

        var workerUrl = $"{_options.WorkerEndpoint.TrimEnd('/')}/api/sessions/{session.WorkerSessionId}/message";

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Cts.Token);

        await foreach (var evt in SendSseRequestAsync(workerUrl, messageRequest, request.SessionId, linkedCts.Token))
        {
            var mappedEvt = MapSessionId(evt, request.SessionId);
            yield return mappedEvt;

            if (evt is AgentResultEvent resultEvt)
            {
                session.ConversationId = resultEvt.ConversationId;
                _sessions[request.SessionId] = session;
            }
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AgentEvent> AnswerQuestionAsync(
        AgentAnswerRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(request.SessionId, out var session))
        {
            yield return new AgentErrorEvent(request.SessionId, $"Session {request.SessionId} not found", "SESSION_NOT_FOUND", false);
            yield break;
        }

        if (string.IsNullOrEmpty(session.WorkerSessionId))
        {
            yield return new AgentErrorEvent(request.SessionId, "Worker session not initialized", "WORKER_NOT_READY", false);
            yield break;
        }

        var answerRequest = new { Answers = request.Answers };

        var workerUrl = $"{_options.WorkerEndpoint.TrimEnd('/')}/api/sessions/{session.WorkerSessionId}/answer";

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Cts.Token);

        await foreach (var evt in SendSseRequestAsync(workerUrl, answerRequest, request.SessionId, linkedCts.Token))
        {
            yield return MapSessionId(evt, request.SessionId);
        }
    }

    /// <inheritdoc />
    public async Task StopSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            _logger.LogInformation("Stopping worker session {SessionId}", sessionId);

            session.Cts.Cancel();
            session.Cts.Dispose();

            // Tell the worker to stop the session
            if (!string.IsNullOrEmpty(session.WorkerSessionId))
            {
                await DeleteWorkerSessionAsync(session.WorkerSessionId);
            }
        }
    }

    /// <inheritdoc />
    public Task InterruptSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            _logger.LogInformation("Interrupting worker session {SessionId}", sessionId);

            // Cancel current execution
            session.Cts.Cancel();
            session.Cts.Dispose();

            // Replace with a fresh CTS
            var newCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
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
                session.SessionId,
                "/data",
                SessionMode.Build,
                "sonnet",
                session.ConversationId,
                session.CreatedAt,
                session.LastActivityAt
            ));
        }

        return Task.FromResult<AgentSessionStatus?>(null);
    }

    private async Task DeleteWorkerSessionAsync(string workerSessionId)
    {
        try
        {
            var url = $"{_options.WorkerEndpoint.TrimEnd('/')}/api/sessions/{workerSessionId}";
            using var request = new HttpRequestMessage(HttpMethod.Delete, url);
            await _httpClient.SendAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error deleting worker session {WorkerSessionId}", workerSessionId);
        }
    }

    private async IAsyncEnumerable<AgentEvent> SendSseRequestAsync(
        string url,
        object requestBody,
        string sessionId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(requestBody, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = content
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new AgentConnectionLostException($"Worker returned {response.StatusCode}: {errorBody}", sessionId);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        string? currentEventType = null;
        var dataBuffer = new StringBuilder();

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line == null) break;

            if (line.StartsWith("event: "))
            {
                currentEventType = line[7..];
            }
            else if (line.StartsWith("data: "))
            {
                dataBuffer.Append(line[6..]);
            }
            else if (string.IsNullOrEmpty(line))
            {
                if (!string.IsNullOrEmpty(currentEventType) && dataBuffer.Length > 0)
                {
                    var evt = ParseSseEvent(currentEventType, dataBuffer.ToString(), sessionId);
                    if (evt != null)
                    {
                        yield return evt;

                        if (evt is AgentSessionEndedEvent)
                        {
                            yield break;
                        }
                    }
                }

                currentEventType = null;
                dataBuffer.Clear();
            }
        }
    }

    private AgentEvent? ParseSseEvent(string eventType, string data, string sessionId)
    {
        try
        {
            return eventType switch
            {
                "SessionStarted" => ParseJson<AgentSessionStartedEvent>(data),
                "ContentBlockReceived" => ParseContentBlockEvent(data, sessionId),
                "MessageReceived" => ParseMessageEvent(data, sessionId),
                "ResultReceived" => ParseJson<AgentResultEvent>(data),
                "QuestionReceived" => ParseQuestionEvent(data, sessionId),
                "SessionEnded" => ParseJson<AgentSessionEndedEvent>(data),
                "Error" => ParseJson<AgentErrorEvent>(data),
                _ => null
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse SSE event {EventType}: {Data}", eventType, data);
            return null;
        }
    }

    private T? ParseJson<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    private AgentContentBlockEvent? ParseContentBlockEvent(string json, string sessionId)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var typeStr = root.GetProperty("type").GetString() ?? "Text";
        var type = Enum.TryParse<ClaudeContentType>(typeStr, true, out var parsed)
            ? parsed : ClaudeContentType.Text;

        // Helper to safely get nullable boolean (handles JSON null values)
        bool? GetNullableBool(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var prop))
                return null;
            if (prop.ValueKind == JsonValueKind.Null)
                return null;
            return prop.GetBoolean();
        }

        return new AgentContentBlockEvent(
            root.TryGetProperty("sessionId", out var sid) ? sid.GetString() ?? sessionId : sessionId,
            type,
            root.TryGetProperty("text", out var text) ? text.GetString() : null,
            root.TryGetProperty("toolName", out var tn) ? tn.GetString() : null,
            root.TryGetProperty("toolInput", out var ti) ? ti.GetString() : null,
            root.TryGetProperty("toolUseId", out var tuid) ? tuid.GetString() : null,
            GetNullableBool(root, "toolSuccess"),
            root.TryGetProperty("index", out var idx) ? idx.GetInt32() : 0
        );
    }

    private AgentMessageEvent? ParseMessageEvent(string json, string sessionId)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var roleStr = root.GetProperty("role").GetString() ?? "Assistant";
        var role = roleStr.Equals("User", StringComparison.OrdinalIgnoreCase)
            ? ClaudeMessageRole.User : ClaudeMessageRole.Assistant;

        var content = new List<AgentContentBlockEvent>();
        if (root.TryGetProperty("content", out var contentArray))
        {
            foreach (var block in contentArray.EnumerateArray())
            {
                var blockJson = block.GetRawText();
                var blockEvent = ParseContentBlockEvent(blockJson, sessionId);
                if (blockEvent != null)
                {
                    content.Add(blockEvent);
                }
            }
        }

        return new AgentMessageEvent(
            root.TryGetProperty("sessionId", out var sid) ? sid.GetString() ?? sessionId : sessionId,
            role,
            content
        );
    }

    private AgentQuestionEvent? ParseQuestionEvent(string json, string sessionId)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var questions = new List<AgentQuestion>();
        if (root.TryGetProperty("questions", out var questionsArray))
        {
            foreach (var q in questionsArray.EnumerateArray())
            {
                var options = new List<AgentQuestionOption>();
                if (q.TryGetProperty("options", out var optionsArray))
                {
                    foreach (var opt in optionsArray.EnumerateArray())
                    {
                        options.Add(new AgentQuestionOption(
                            opt.GetProperty("label").GetString() ?? "",
                            opt.GetProperty("description").GetString() ?? ""
                        ));
                    }
                }

                questions.Add(new AgentQuestion(
                    q.GetProperty("question").GetString() ?? "",
                    q.TryGetProperty("header", out var h) ? h.GetString() ?? "" : "",
                    options,
                    q.TryGetProperty("multiSelect", out var ms) && ms.GetBoolean()
                ));
            }
        }

        return new AgentQuestionEvent(
            root.TryGetProperty("sessionId", out var sid) ? sid.GetString() ?? sessionId : sessionId,
            root.TryGetProperty("questionId", out var qid) ? qid.GetString() ?? "" : "",
            root.TryGetProperty("toolUseId", out var tuid) ? tuid.GetString() ?? "" : "",
            questions
        );
    }

    private static AgentEvent MapSessionId(AgentEvent evt, string sessionId)
    {
        return evt switch
        {
            AgentSessionStartedEvent e => e with { SessionId = sessionId },
            AgentContentBlockEvent e => e with { SessionId = sessionId },
            AgentMessageEvent e => e with { SessionId = sessionId, Content = e.Content.Select(c => c with { SessionId = sessionId }).ToList() },
            AgentResultEvent e => e with { SessionId = sessionId },
            AgentQuestionEvent e => e with { SessionId = sessionId },
            AgentSessionEndedEvent e => e with { SessionId = sessionId },
            AgentErrorEvent e => e with { SessionId = sessionId },
            _ => evt
        };
    }

    public async ValueTask DisposeAsync()
    {
        var deleteTasks = _sessions.Values.Select(session =>
        {
            session.Cts.Cancel();
            session.Cts.Dispose();
            return !string.IsNullOrEmpty(session.WorkerSessionId)
                ? DeleteWorkerSessionAsync(session.WorkerSessionId)
                : Task.CompletedTask;
        });

        await Task.WhenAll(deleteTasks);
        _sessions.Clear();
        _httpClient.Dispose();
    }
}
