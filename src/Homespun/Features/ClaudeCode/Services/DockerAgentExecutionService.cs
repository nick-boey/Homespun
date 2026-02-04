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
/// Configuration options for Docker agent execution.
/// </summary>
public class DockerAgentExecutionOptions
{
    public const string SectionName = "AgentExecution:Docker";

    /// <summary>
    /// The Docker image to use for agent workers.
    /// </summary>
    public string WorkerImage { get; set; } = "ghcr.io/nick-boey/homespun-worker:latest";

    /// <summary>
    /// Path on the host to mount as the data volume.
    /// </summary>
    public string DataVolumePath { get; set; } = "/data";

    /// <summary>
    /// Path on the host machine that corresponds to the container's data volume.
    /// Used for path translation when spawning sibling containers.
    /// </summary>
    public string? HostDataPath { get; set; }

    /// <summary>
    /// Memory limit in bytes for worker containers.
    /// </summary>
    public long MemoryLimitBytes { get; set; } = 4L * 1024 * 1024 * 1024; // 4GB

    /// <summary>
    /// CPU limit for worker containers.
    /// </summary>
    public double CpuLimit { get; set; } = 2.0;

    /// <summary>
    /// Timeout for HTTP requests to worker containers.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Docker socket path for DooD.
    /// </summary>
    public string DockerSocketPath { get; set; } = "/var/run/docker.sock";

    /// <summary>
    /// Network name for container communication.
    /// </summary>
    public string NetworkName { get; set; } = "bridge";
}

/// <summary>
/// Docker-based agent execution using Docker-outside-of-Docker (DooD).
/// Spawns worker containers and communicates via SSE over HTTP.
/// </summary>
public class DockerAgentExecutionService : IAgentExecutionService, IAsyncDisposable
{
    private readonly DockerAgentExecutionOptions _options;
    private readonly ILogger<DockerAgentExecutionService> _logger;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, DockerSession> _sessions = new();

    private record DockerSession(
        string SessionId,
        string ContainerId,
        string ContainerName,
        string WorkerUrl,
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

    public DockerAgentExecutionService(
        IOptions<DockerAgentExecutionOptions> options,
        ILogger<DockerAgentExecutionService> logger)
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
        var containerName = $"homespun-agent-{sessionId[..8]}";

        _logger.LogInformation("DockerAgentExecutionService: Starting session {SessionId} with worker image {Image}, container {ContainerName}",
            sessionId, _options.WorkerImage, containerName);

        var channel = System.Threading.Channels.Channel.CreateUnbounded<AgentEvent>();

        _ = Task.Run(async () =>
        {
            string? containerId = null;
            try
            {
                // Start the worker container
                (containerId, var workerUrl) = await StartContainerAsync(containerName, request.WorkingDirectory, cancellationToken);

                var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var session = new DockerSession(
                    sessionId, containerId, containerName, workerUrl, null, cts, DateTime.UtcNow);

                _sessions[sessionId] = session;

                await channel.Writer.WriteAsync(new AgentSessionStartedEvent(sessionId, null), cancellationToken);

                // Start the agent session in the worker
                // The workspace is mounted at /data in the agent container
                var startRequest = new
                {
                    WorkingDirectory = "/data",
                    Mode = request.Mode.ToString(),
                    Model = request.Model,
                    Prompt = request.Prompt,
                    SystemPrompt = request.SystemPrompt,
                    ResumeSessionId = request.ResumeSessionId
                };

                await foreach (var evt in SendSseRequestAsync($"{workerUrl}/api/sessions", startRequest, sessionId, cts.Token))
                {
                    // Update worker session ID if we receive it
                    if (evt is AgentSessionStartedEvent startedEvt && !string.IsNullOrEmpty(startedEvt.ConversationId))
                    {
                        session = session with { WorkerSessionId = startedEvt.SessionId };
                        _sessions[sessionId] = session;
                    }

                    // Map worker session IDs to our session ID
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
                _logger.LogError(ex, "Error in Docker session {SessionId}", sessionId);

                // Cleanup container on error
                if (!string.IsNullOrEmpty(containerId))
                {
                    await StopContainerAsync(containerId);
                }

                await channel.Writer.WriteAsync(new AgentErrorEvent(sessionId, ex.Message, "DOCKER_ERROR", ex is AgentStartupException));
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
            $"{session.WorkerUrl}/api/sessions/{session.WorkerSessionId}/message",
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
            $"{session.WorkerUrl}/api/sessions/{session.WorkerSessionId}/answer",
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
            _logger.LogInformation("Stopping Docker session {SessionId}, container {ContainerName}",
                sessionId, session.ContainerName);

            session.Cts.Cancel();
            session.Cts.Dispose();

            // Stop the worker container
            await StopContainerAsync(session.ContainerId);
        }
    }

    /// <inheritdoc />
    public Task<AgentSessionStatus?> GetSessionStatusAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            return Task.FromResult<AgentSessionStatus?>(new AgentSessionStatus(
                session.SessionId,
                "/data", // Container-relative path
                SessionMode.Build, // TODO: Store actual mode
                "sonnet", // TODO: Store actual model
                session.ConversationId,
                session.CreatedAt,
                session.LastActivityAt
            ));
        }

        return Task.FromResult<AgentSessionStatus?>(null);
    }

    /// <summary>
    /// Translates a container path to a host path for Docker mounts.
    /// When running in a container, paths like /data/test-workspace need to be
    /// translated to the corresponding host path for sibling container mounts.
    /// </summary>
    public string TranslateToHostPath(string containerPath)
    {
        // If no host path configured, assume we're running directly on host
        if (string.IsNullOrEmpty(_options.HostDataPath))
            return containerPath;

        // Translate /data/xxx to {HostDataPath}/xxx
        if (containerPath.StartsWith(_options.DataVolumePath, StringComparison.OrdinalIgnoreCase))
        {
            var relativePath = containerPath[_options.DataVolumePath.Length..].TrimStart('/');
            return string.IsNullOrEmpty(relativePath)
                ? _options.HostDataPath
                : Path.Combine(_options.HostDataPath, relativePath);
        }

        return containerPath;
    }

    private async Task<(string containerId, string workerUrl)> StartContainerAsync(
        string containerName,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        // Use Docker CLI to create and start the container
        // This is simpler than using Docker.DotNet SDK for now
        var dockerArgs = new StringBuilder();
        dockerArgs.Append("run -d --rm ");
        dockerArgs.Append($"--name {containerName} ");
        dockerArgs.Append($"--memory {_options.MemoryLimitBytes} ");
        dockerArgs.Append($"--cpus {_options.CpuLimit} ");
        var hostPath = TranslateToHostPath(workingDirectory);
        dockerArgs.Append($"-v \"{hostPath}:/data\" ");
        dockerArgs.Append($"-e ASPNETCORE_URLS=http://+:8080 ");
        dockerArgs.Append($"--network {_options.NetworkName} ");
        dockerArgs.Append(_options.WorkerImage);

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "docker",
            Arguments = dockerArgs.ToString(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new AgentStartupException($"Failed to start Docker process for container {containerName}");

        var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            throw new AgentStartupException($"Docker run failed: {stderr}");
        }

        var containerId = stdout.Trim();
        _logger.LogDebug("Started container {ContainerId}", containerId);

        // Get the container's IP address
        var inspectStartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "docker",
            Arguments = $"inspect -f \"{{{{range.NetworkSettings.Networks}}}}{{{{.IPAddress}}}}{{{{end}}}}\" {containerId}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var inspectProcess = System.Diagnostics.Process.Start(inspectStartInfo)
            ?? throw new AgentStartupException($"Failed to inspect container {containerId}");

        var ipAddress = (await inspectProcess.StandardOutput.ReadToEndAsync(cancellationToken)).Trim();
        await inspectProcess.WaitForExitAsync(cancellationToken);

        if (string.IsNullOrEmpty(ipAddress))
        {
            throw new AgentStartupException($"Could not get IP address for container {containerId}");
        }

        var workerUrl = $"http://{ipAddress}:8080";

        // Wait for the worker to be ready
        await WaitForWorkerReadyAsync(workerUrl, cancellationToken);

        return (containerId, workerUrl);
    }

    private async Task WaitForWorkerReadyAsync(string workerUrl, CancellationToken cancellationToken)
    {
        var healthUrl = $"{workerUrl}/api/health";
        var maxAttempts = 30;
        var delay = TimeSpan.FromSeconds(1);

        for (var i = 0; i < maxAttempts; i++)
        {
            try
            {
                using var response = await _httpClient.GetAsync(healthUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogDebug("Worker at {Url} is ready", workerUrl);
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Worker not ready yet
            }

            await Task.Delay(delay, cancellationToken);
        }

        throw new AgentStartupException($"Worker at {workerUrl} did not become ready in time");
    }

    private async Task StopContainerAsync(string containerId)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"stop {containerId}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping container {ContainerId}", containerId);
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
                // End of event
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

        return new AgentContentBlockEvent(
            root.TryGetProperty("sessionId", out var sid) ? sid.GetString() ?? sessionId : sessionId,
            type,
            root.TryGetProperty("text", out var text) ? text.GetString() : null,
            root.TryGetProperty("toolName", out var tn) ? tn.GetString() : null,
            root.TryGetProperty("toolInput", out var ti) ? ti.GetString() : null,
            root.TryGetProperty("toolUseId", out var tuid) ? tuid.GetString() : null,
            root.TryGetProperty("toolSuccess", out var ts) ? ts.GetBoolean() : null,
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
        // Map worker session IDs to our session ID
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
        foreach (var session in _sessions.Values)
        {
            try
            {
                session.Cts.Cancel();
                session.Cts.Dispose();
                await StopContainerAsync(session.ContainerId);
            }
            catch { }
        }
        _sessions.Clear();
        _httpClient.Dispose();
    }
}
