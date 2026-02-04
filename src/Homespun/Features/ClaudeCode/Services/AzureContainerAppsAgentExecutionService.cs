using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Exceptions;
using Microsoft.Extensions.Options;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Configuration options for Azure Container Apps agent execution.
/// </summary>
public class AzureContainerAppsAgentExecutionOptions
{
    public const string SectionName = "AgentExecution:AzureContainerApps";

    /// <summary>
    /// Azure subscription ID.
    /// </summary>
    public string SubscriptionId { get; set; } = string.Empty;

    /// <summary>
    /// Resource group containing the session pool.
    /// </summary>
    public string ResourceGroup { get; set; } = string.Empty;

    /// <summary>
    /// Name of the session pool for dynamic sessions.
    /// </summary>
    public string SessionPoolName { get; set; } = "homespun-agents";

    /// <summary>
    /// The Docker image to use for agent workers.
    /// </summary>
    public string WorkerImage { get; set; } = "ghcr.io/nick-boey/homespun-worker:latest";

    /// <summary>
    /// Timeout for HTTP requests to sessions.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Maximum session duration before automatic termination.
    /// </summary>
    public TimeSpan MaxSessionDuration { get; set; } = TimeSpan.FromMinutes(30);
}

/// <summary>
/// Azure Container Apps Dynamic Sessions implementation for agent execution.
/// Uses Azure Container Apps Dynamic Sessions API to run agents in isolated containers.
/// </summary>
public class AzureContainerAppsAgentExecutionService : IAgentExecutionService, IAsyncDisposable
{
    private readonly AzureContainerAppsAgentExecutionOptions _options;
    private readonly ILogger<AzureContainerAppsAgentExecutionService> _logger;
    private readonly HttpClient _httpClient;
    private readonly TokenCredential _credential;
    private readonly ConcurrentDictionary<string, AzureSession> _sessions = new();

    private const string AzureManagementScope = "https://management.azure.com/.default";

    private record AzureSession(
        string SessionId,
        string PoolSessionId,
        string SessionEndpoint,
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
        _credential = new DefaultAzureCredential();
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AgentEvent> StartSessionAsync(
        AgentStartRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid().ToString();

        _logger.LogInformation("Starting Azure Container Apps session {SessionId}", sessionId);

        var channel = System.Threading.Channels.Channel.CreateUnbounded<AgentEvent>();

        _ = Task.Run(async () =>
        {
            string? poolSessionId = null;
            try
            {
                // Create a dynamic session in the session pool
                string sessionEndpoint;
                (poolSessionId, sessionEndpoint) = await CreateDynamicSessionAsync(sessionId, cancellationToken);

                var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var session = new AzureSession(
                    sessionId, poolSessionId, sessionEndpoint, null, cts, DateTime.UtcNow);

                _sessions[sessionId] = session;

                await channel.Writer.WriteAsync(new AgentSessionStartedEvent(sessionId, null), cancellationToken);

                // Start the agent session in the worker
                var startRequest = new
                {
                    WorkingDirectory = "/data", // Azure Files mount point
                    Mode = request.Mode.ToString(),
                    Model = request.Model,
                    Prompt = request.Prompt,
                    SystemPrompt = request.SystemPrompt,
                    ResumeSessionId = request.ResumeSessionId
                };

                await foreach (var evt in SendSseRequestAsync($"{sessionEndpoint}/api/sessions", startRequest, sessionId, cts.Token))
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
                _logger.LogError(ex, "Error in Azure Container Apps session {SessionId}", sessionId);

                // Cleanup session on error
                if (!string.IsNullOrEmpty(poolSessionId))
                {
                    await DeleteDynamicSessionAsync(poolSessionId);
                }

                await channel.Writer.WriteAsync(new AgentErrorEvent(sessionId, ex.Message, "AZURE_ERROR", ex is AgentStartupException));
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

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Cts.Token);

        await foreach (var evt in SendSseRequestAsync(
            $"{session.SessionEndpoint}/api/sessions/{session.WorkerSessionId}/message",
            messageRequest, request.SessionId, linkedCts.Token))
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

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Cts.Token);

        await foreach (var evt in SendSseRequestAsync(
            $"{session.SessionEndpoint}/api/sessions/{session.WorkerSessionId}/answer",
            answerRequest, request.SessionId, linkedCts.Token))
        {
            yield return MapSessionId(evt, request.SessionId);
        }
    }

    /// <inheritdoc />
    public async Task StopSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryRemove(sessionId, out var session))
        {
            _logger.LogInformation("Stopping Azure Container Apps session {SessionId}", sessionId);

            session.Cts.Cancel();
            session.Cts.Dispose();

            await DeleteDynamicSessionAsync(session.PoolSessionId);
        }
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

    private async Task<(string poolSessionId, string sessionEndpoint)> CreateDynamicSessionAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        // Azure Container Apps Dynamic Sessions API endpoint
        var apiUrl = $"https://management.azure.com/subscriptions/{_options.SubscriptionId}" +
                     $"/resourceGroups/{_options.ResourceGroup}" +
                     $"/providers/Microsoft.App/sessionPools/{_options.SessionPoolName}" +
                     $"/sessions/{sessionId}?api-version=2024-02-02-preview";

        var requestBody = new
        {
            properties = new
            {
                containerImage = _options.WorkerImage,
                maxSessionIdleTimeoutInSeconds = (int)_options.MaxSessionDuration.TotalSeconds,
                customContainerTemplate = new
                {
                    containers = new[]
                    {
                        new
                        {
                            name = "agent-worker",
                            image = _options.WorkerImage,
                            resources = new
                            {
                                cpu = 2.0,
                                memory = "4Gi"
                            },
                            env = new[]
                            {
                                new { name = "ASPNETCORE_URLS", value = "http://+:8080" },
                                new { name = "ASPNETCORE_ENVIRONMENT", value = "Production" }
                            }
                        }
                    }
                }
            }
        };

        var json = JsonSerializer.Serialize(requestBody, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Put, apiUrl)
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new AgentStartupException($"Failed to create dynamic session: {response.StatusCode} - {errorBody}");
        }

        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        using var doc = JsonDocument.Parse(responseBody);

        var poolSessionId = doc.RootElement.GetProperty("name").GetString()
            ?? throw new AgentStartupException("Session ID not returned from Azure");

        // Get the session endpoint from the response
        var sessionEndpoint = doc.RootElement
            .GetProperty("properties")
            .GetProperty("sessionEndpoint")
            .GetString()
            ?? throw new AgentStartupException("Session endpoint not returned from Azure");

        _logger.LogDebug("Created dynamic session {PoolSessionId} at {Endpoint}", poolSessionId, sessionEndpoint);

        // Wait for the session to be ready
        await WaitForSessionReadyAsync(poolSessionId, sessionEndpoint, cancellationToken);

        return (poolSessionId, sessionEndpoint);
    }

    private async Task WaitForSessionReadyAsync(
        string poolSessionId,
        string sessionEndpoint,
        CancellationToken cancellationToken)
    {
        var healthUrl = $"{sessionEndpoint}/api/health";
        var maxAttempts = 60;
        var delay = TimeSpan.FromSeconds(2);

        for (var i = 0; i < maxAttempts; i++)
        {
            try
            {
                using var response = await _httpClient.GetAsync(healthUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("Session {PoolSessionId} is ready", poolSessionId);
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Session not ready yet
            }

            await Task.Delay(delay, cancellationToken);
        }

        throw new AgentStartupException($"Session {poolSessionId} did not become ready in time");
    }

    private async Task DeleteDynamicSessionAsync(string poolSessionId)
    {
        try
        {
            var token = await GetAccessTokenAsync(CancellationToken.None);

            var apiUrl = $"https://management.azure.com/subscriptions/{_options.SubscriptionId}" +
                         $"/resourceGroups/{_options.ResourceGroup}" +
                         $"/providers/Microsoft.App/sessionPools/{_options.SessionPoolName}" +
                         $"/sessions/{poolSessionId}?api-version=2024-02-02-preview";

            using var request = new HttpRequestMessage(HttpMethod.Delete, apiUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            await _httpClient.SendAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error deleting dynamic session {PoolSessionId}", poolSessionId);
        }
    }

    private async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        var tokenRequestContext = new TokenRequestContext(new[] { AzureManagementScope });
        var accessToken = await _credential.GetTokenAsync(tokenRequestContext, cancellationToken);
        return accessToken.Token;
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
            return DeleteDynamicSessionAsync(session.PoolSessionId);
        });

        await Task.WhenAll(deleteTasks);
        _sessions.Clear();
        _httpClient.Dispose();
    }
}
