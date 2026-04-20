using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Homespun.Features.ClaudeCode.Exceptions;
using Homespun.Shared.Models.Sessions;
using Microsoft.Extensions.Options;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Development-only <see cref="IAgentExecutionService"/> shim that forwards
/// every session to a single pre-running <c>homespun-worker</c> container
/// reachable at <see cref="SingleContainerAgentExecutionOptions.WorkerUrl"/>.
///
/// <para>
/// Registered only when <c>AgentExecution:Mode == "SingleContainer"</c> AND the
/// environment is Development — registration in Production fails fast at
/// startup. See <see cref="SingleContainerBusyException"/> for the single-active
/// session invariant.
/// </para>
///
/// <para>
/// Container-management operations (list/stop containers, restart, cleanup) are
/// intentional no-ops: the dev runs <c>docker compose up worker</c> separately
/// and the shim holds no container-lifetime responsibility.
/// </para>
/// </summary>
public sealed class SingleContainerAgentExecutionService : IAgentExecutionService, IDisposable
{
    private static readonly JsonSerializerOptions CamelCaseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly SingleContainerAgentExecutionOptions _options;
    private readonly ILogger<SingleContainerAgentExecutionService> _logger;
    private readonly ISessionEventIngestor _eventIngestor;
    private readonly HttpClient _httpClient;

    private readonly SemaphoreSlim _slotLock = new(1, 1);
    private ActiveSession? _active;

    private sealed class ActiveSession
    {
        public required string SessionId { get; init; }
        public required CancellationTokenSource Cts { get; init; }
        public string? WorkerSessionId { get; set; }
        public string? ConversationId { get; set; }
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
        public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
        public SessionMode Mode { get; init; }
        public string Model { get; init; } = string.Empty;
        public string WorkingDirectory { get; init; } = string.Empty;
        public string? ProjectId { get; init; }
    }

    public SingleContainerAgentExecutionService(
        IOptions<SingleContainerAgentExecutionOptions> options,
        ILogger<SingleContainerAgentExecutionService> logger,
        ISessionEventIngestor eventIngestor)
    {
        _options = options.Value;
        _logger = logger;
        _eventIngestor = eventIngestor;

        if (string.IsNullOrWhiteSpace(_options.WorkerUrl))
        {
            throw new InvalidOperationException(
                "AgentExecution:SingleContainer:WorkerUrl must be set when AgentExecution:Mode=SingleContainer.");
        }

        _httpClient = new HttpClient
        {
            Timeout = _options.RequestTimeout,
        };
    }

    public async IAsyncEnumerable<SdkMessage> StartSessionAsync(
        AgentStartRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sessionId = request.HomespunSessionId ?? Guid.NewGuid().ToString();

        await _slotLock.WaitAsync(cancellationToken);
        ActiveSession session;
        try
        {
            if (_active is not null)
            {
                var busy = new SingleContainerBusyException(sessionId, _active.SessionId);
                _logger.LogError(
                    "SingleContainer worker busy: requested session {RequestedSessionId} but {CurrentSessionId} is active",
                    sessionId, _active.SessionId);
                throw busy;
            }

            session = new ActiveSession
            {
                SessionId = sessionId,
                Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken),
                Mode = request.Mode,
                Model = request.Model,
                WorkingDirectory = request.WorkingDirectory,
                ProjectId = request.ProjectId,
            };
            _active = session;
        }
        finally
        {
            _slotLock.Release();
        }

        var containerWorkingDirectory = TranslateWorkingDirectoryForContainer(request.WorkingDirectory);

        var startBody = new
        {
            workingDirectory = containerWorkingDirectory,
            mode = request.Mode.ToString(),
            model = request.Model,
            prompt = request.Prompt,
            systemPrompt = request.SystemPrompt,
            resumeSessionId = request.ResumeSessionId,
            issueId = request.IssueId,
            projectId = request.ProjectId,
            projectName = request.ProjectName,
            homespunSessionId = sessionId,
        };

        var url = $"{_options.WorkerUrl.TrimEnd('/')}/api/sessions";
        await foreach (var msg in StreamAgentEventsAsync(url, startBody, sessionId, request.ProjectId, session.Cts.Token))
        {
            if (msg is SdkSystemMessage sys && sys.Subtype == "session_started" &&
                !string.IsNullOrEmpty(sys.SessionId))
            {
                session.WorkerSessionId = sys.SessionId;
            }
            if (msg is SdkResultMessage result)
            {
                session.ConversationId = result.SessionId;
            }
            yield return msg;
        }
    }

    public async IAsyncEnumerable<SdkMessage> SendMessageAsync(
        AgentMessageRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var active = _active;
        // MessageProcessingService passes the agentSessionId captured from the
        // session_started event, which for SingleContainer is the worker's own
        // session id — so match on either identifier held by the active slot.
        var matches = active is not null
            && (active.SessionId == request.SessionId
                || (!string.IsNullOrEmpty(active.WorkerSessionId) && active.WorkerSessionId == request.SessionId));
        if (!matches)
        {
            _logger.LogError(
                "SendMessageAsync: no active SingleContainer session for {SessionId} (active={ActiveSessionId}, worker={WorkerSessionId})",
                request.SessionId,
                active?.SessionId,
                active?.WorkerSessionId);
            yield break;
        }

        if (string.IsNullOrEmpty(active!.WorkerSessionId))
        {
            _logger.LogError("SendMessageAsync: active session has no WorkerSessionId yet");
            yield break;
        }

        active.LastActivityAt = DateTime.UtcNow;

        var body = new
        {
            message = request.Message,
            model = request.Model,
            mode = request.Mode.ToString(),
        };

        var url = $"{_options.WorkerUrl.TrimEnd('/')}/api/sessions/{active.WorkerSessionId}/message";
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, active.Cts.Token);
        await foreach (var msg in StreamAgentEventsAsync(url, body, active.SessionId, null, linked.Token))
        {
            yield return msg;
        }
    }

    public async Task StopSessionAsync(string sessionId, bool forceStopContainer = false, CancellationToken cancellationToken = default)
    {
        await _slotLock.WaitAsync(cancellationToken);
        ActiveSession? toStop;
        try
        {
            toStop = _active is not null && _active.SessionId == sessionId ? _active : null;
            if (toStop is not null)
            {
                _active = null;
            }
        }
        finally
        {
            _slotLock.Release();
        }

        if (toStop is null) return;

        toStop.Cts.Cancel();

        if (!string.IsNullOrEmpty(toStop.WorkerSessionId))
        {
            try
            {
                using var response = await _httpClient.DeleteAsync(
                    $"{_options.WorkerUrl.TrimEnd('/')}/api/sessions/{toStop.WorkerSessionId}",
                    cancellationToken);
                _logger.LogDebug("SingleContainer stop worker session {WorkerSessionId}: {Status}",
                    toStop.WorkerSessionId, response.StatusCode);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error stopping SingleContainer worker session {WorkerSessionId}", toStop.WorkerSessionId);
            }
        }

        toStop.Cts.Dispose();
    }

    public Task InterruptSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var active = _active;
        if (active is null || active.SessionId != sessionId || string.IsNullOrEmpty(active.WorkerSessionId))
        {
            return Task.CompletedTask;
        }
        return _httpClient.PostAsync(
            $"{_options.WorkerUrl.TrimEnd('/')}/api/sessions/{active.WorkerSessionId}/interrupt",
            content: null,
            cancellationToken);
    }

    public Task<AgentSessionStatus?> GetSessionStatusAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var active = _active;
        if (active is null || active.SessionId != sessionId)
        {
            return Task.FromResult<AgentSessionStatus?>(null);
        }
        return Task.FromResult<AgentSessionStatus?>(new AgentSessionStatus(
            SessionId: active.SessionId,
            WorkingDirectory: active.WorkingDirectory,
            Mode: active.Mode,
            Model: active.Model,
            ConversationId: active.ConversationId,
            CreatedAt: active.CreatedAt,
            LastActivityAt: active.LastActivityAt));
    }

    public Task<string?> ReadFileFromAgentAsync(string sessionId, string filePath, CancellationToken cancellationToken = default)
    {
        // Not needed for the dev-only debugging use case; fall through to null so callers
        // treat the file as absent and read from the host filesystem instead.
        return Task.FromResult<string?>(null);
    }

    public Task<IReadOnlyList<AgentSessionStatus>> ListSessionsAsync(CancellationToken cancellationToken = default)
    {
        var active = _active;
        if (active is null)
        {
            return Task.FromResult<IReadOnlyList<AgentSessionStatus>>([]);
        }
        return Task.FromResult<IReadOnlyList<AgentSessionStatus>>(new[]
        {
            new AgentSessionStatus(
                SessionId: active.SessionId,
                WorkingDirectory: active.WorkingDirectory,
                Mode: active.Mode,
                Model: active.Model,
                ConversationId: active.ConversationId,
                CreatedAt: active.CreatedAt,
                LastActivityAt: active.LastActivityAt),
        });
    }

    public Task<int> CleanupOrphanedContainersAsync(CancellationToken cancellationToken = default)
    {
        // SingleContainer mode does not own containers; the dev manages the compose worker.
        return Task.FromResult(0);
    }

    public async Task<bool> AnswerQuestionAsync(string sessionId, Dictionary<string, string> answers, CancellationToken cancellationToken = default)
    {
        var active = _active;
        if (active is null || active.SessionId != sessionId || string.IsNullOrEmpty(active.WorkerSessionId))
        {
            return false;
        }
        var url = $"{_options.WorkerUrl.TrimEnd('/')}/api/sessions/{active.WorkerSessionId}/answer";
        using var response = await _httpClient.PostAsync(
            url,
            new StringContent(JsonSerializer.Serialize(new { answers }, CamelCaseJsonOptions), Encoding.UTF8, "application/json"),
            cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> ApprovePlanAsync(string sessionId, bool approved, bool keepContext, string? feedback = null, CancellationToken cancellationToken = default)
    {
        var active = _active;
        if (active is null || active.SessionId != sessionId || string.IsNullOrEmpty(active.WorkerSessionId))
        {
            return false;
        }
        var url = $"{_options.WorkerUrl.TrimEnd('/')}/api/sessions/{active.WorkerSessionId}/approve-plan";
        using var response = await _httpClient.PostAsync(
            url,
            new StringContent(JsonSerializer.Serialize(new { approved, keepContext, feedback }, CamelCaseJsonOptions), Encoding.UTF8, "application/json"),
            cancellationToken);
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Rewrites a host working-directory path to the path the worker container
    /// sees after bind-mounting <see cref="SingleContainerAgentExecutionOptions.HostWorkspaceRoot"/>
    /// at <see cref="SingleContainerAgentExecutionOptions.ContainerWorkspaceRoot"/>.
    ///
    /// <para>
    /// Windows-only. On Linux/macOS the host path already resolves inside the
    /// container (Docker Desktop shared filesystem on macOS; matching paths on
    /// Linux) so the raw value is forwarded unchanged.
    /// </para>
    ///
    /// <para>
    /// Throws <see cref="InvalidOperationException"/> when the shim is running
    /// on Windows and the caller's <paramref name="hostWorkingDirectory"/> is
    /// not under <c>HostWorkspaceRoot</c> — forwarding a raw Windows path to a
    /// Linux worker breaks the SDK's <c>cwd</c> resolution and produces the
    /// misleading "Claude Code executable not found" error.
    /// </para>
    /// </summary>
    internal string TranslateWorkingDirectoryForContainer(string hostWorkingDirectory)
    {
        if (!string.IsNullOrEmpty(_options.ForceContainerWorkingDirectory))
        {
            return _options.ForceContainerWorkingDirectory;
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return hostWorkingDirectory;
        }

        if (string.IsNullOrEmpty(_options.HostWorkspaceRoot))
        {
            throw new InvalidOperationException(
                "AgentExecution:SingleContainer:HostWorkspaceRoot must be configured on Windows so the shim can rewrite host paths to the worker container's mount point.");
        }

        var hostRoot = Path.GetFullPath(_options.HostWorkspaceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var requested = Path.GetFullPath(hostWorkingDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!requested.StartsWith(hostRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"WorkingDirectory '{hostWorkingDirectory}' is not under HostWorkspaceRoot '{_options.HostWorkspaceRoot}'. SingleContainer mode can only run sessions whose working directory sits inside the mounted workspace.");
        }

        var relative = requested.Length == hostRoot.Length
            ? string.Empty
            : requested[(hostRoot.Length + 1)..];
        var containerRoot = _options.ContainerWorkspaceRoot.TrimEnd('/');
        var containerRelative = relative.Replace('\\', '/');
        return string.IsNullOrEmpty(containerRelative)
            ? containerRoot
            : $"{containerRoot}/{containerRelative}";
    }

    public Task<CloneContainerState?> GetCloneContainerStateAsync(string workingDirectory, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<CloneContainerState?>(null);
    }

    public Task TerminateCloneSessionAsync(string workingDirectory, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ContainerInfo>>([]);
    }

    public Task<bool> StopContainerByIdAsync(string containerId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    public Task<ContainerRestartResult?> RestartContainerAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<ContainerRestartResult?>(null);
    }

    public async Task<bool> SetSessionModeAsync(string sessionId, SessionMode mode, CancellationToken cancellationToken = default)
    {
        var active = _active;
        if (active is null || active.SessionId != sessionId || string.IsNullOrEmpty(active.WorkerSessionId))
        {
            return false;
        }
        var url = $"{_options.WorkerUrl.TrimEnd('/')}/api/sessions/{active.WorkerSessionId}/mode";
        using var response = await _httpClient.PostAsync(
            url,
            new StringContent(JsonSerializer.Serialize(new { mode = mode.ToString() }, CamelCaseJsonOptions), Encoding.UTF8, "application/json"),
            cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> SetSessionModelAsync(string sessionId, string model, CancellationToken cancellationToken = default)
    {
        var active = _active;
        if (active is null || active.SessionId != sessionId || string.IsNullOrEmpty(active.WorkerSessionId))
        {
            return false;
        }
        var url = $"{_options.WorkerUrl.TrimEnd('/')}/api/sessions/{active.WorkerSessionId}/model";
        using var response = await _httpClient.PostAsync(
            url,
            new StringContent(JsonSerializer.Serialize(new { model }, CamelCaseJsonOptions), Encoding.UTF8, "application/json"),
            cancellationToken);
        return response.IsSuccessStatusCode;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _slotLock.Dispose();
        _active?.Cts.Dispose();
    }

    private async IAsyncEnumerable<SdkMessage> StreamAgentEventsAsync(
        string url,
        object requestBody,
        string sessionId,
        string? projectId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(requestBody, CamelCaseJsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"SingleContainer worker at {url} returned {response.StatusCode}: {errorBody}");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        string? eventType = null;
        var data = new StringBuilder();

        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);
            if (line is null) break;

            if (line.StartsWith("event: "))
            {
                eventType = line[7..];
            }
            else if (line.StartsWith("data: "))
            {
                data.Append(line[6..]);
            }
            else if (line.Length == 0)
            {
                if (!string.IsNullOrEmpty(eventType) && data.Length > 0)
                {
                    var raw = data.ToString();
                    await TryIngestA2AAsync(eventType, raw, sessionId, projectId, cancellationToken);
                    var msg = TryParseSdkMessage(eventType, raw, sessionId);
                    if (msg is not null)
                    {
                        yield return msg;
                        if (msg is SdkResultMessage) yield break;
                    }
                }
                eventType = null;
                data.Clear();
            }
        }
    }

    private async Task TryIngestA2AAsync(string eventKind, string rawData, string sessionId, string? projectId, CancellationToken cancellationToken)
    {
        if (!A2AMessageParser.IsA2AEventKind(eventKind)) return;
        try
        {
            using var doc = JsonDocument.Parse(rawData);
            var payload = doc.RootElement.Clone();

            var effectiveProjectId = string.IsNullOrEmpty(projectId) ? "unknown" : projectId;
            await _eventIngestor.IngestAsync(effectiveProjectId, sessionId, eventKind, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SingleContainer ingestor tap failed for session {SessionId} kind {Kind}",
                sessionId, eventKind);
        }
    }

    private SdkMessage? TryParseSdkMessage(string eventKind, string rawData, string sessionId)
    {
        try
        {
            if (A2AMessageParser.IsA2AEventKind(eventKind))
            {
                var a2aEvent = A2AMessageParser.ParseSseEvent(eventKind, rawData);
                if (a2aEvent is null) return null;
                return A2AMessageParser.ConvertToSdkMessage(a2aEvent, sessionId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse worker SSE event {EventKind}", eventKind);
        }
        return null;
    }
}
