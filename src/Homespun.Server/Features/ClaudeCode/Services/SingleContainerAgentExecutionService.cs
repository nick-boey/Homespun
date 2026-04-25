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
///
/// <para>
/// Event delivery runs through <see cref="IPerSessionEventStream"/> — the
/// worker's <c>POST /api/sessions</c> and <c>POST /api/sessions/:id/message</c>
/// endpoints now return JSON, and the long-lived <c>GET /api/sessions/:id/events</c>
/// SSE reader owned by <see cref="IPerSessionEventStream"/> fans out parsed
/// <see cref="SdkMessage"/>s to per-turn subscribers and drives the ingestor on
/// every A2A event. This keeps post-result background events flowing to the
/// client after a turn has ended, mirroring the Docker executor.
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
    private readonly IPerSessionEventStream _perSession;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

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

    /// <summary>
    /// Response parsed from the worker's <c>POST /api/sessions</c> and
    /// <c>POST /api/sessions/:id/message</c> endpoints now that those endpoints
    /// return JSON rather than an SSE stream (task 3 / task 4 of the
    /// <c>fix-post-result-events</c> plan).
    /// </summary>
    private sealed record WorkerStartResponse(string SessionId, string? ConversationId);

    public SingleContainerAgentExecutionService(
        IOptions<SingleContainerAgentExecutionOptions> options,
        ILogger<SingleContainerAgentExecutionService> logger,
        IPerSessionEventStream perSession)
    {
        _options = options.Value;
        _logger = logger;
        _perSession = perSession;

        if (string.IsNullOrWhiteSpace(_options.WorkerUrl))
        {
            throw new InvalidOperationException(
                "AgentExecution:SingleContainer:WorkerUrl must be set when AgentExecution:Mode=SingleContainer.");
        }

        _httpClient = new HttpClient
        {
            Timeout = _options.RequestTimeout,
        };
        _ownsHttpClient = true;
    }

    /// <summary>
    /// Test-only constructor that accepts a pre-configured <see cref="HttpClient"/>
    /// so unit tests can exercise the per-session wiring against
    /// <see cref="Homespun.Tests.Helpers.MockHttpMessageHandler"/>-style fakes.
    /// </summary>
    internal SingleContainerAgentExecutionService(
        IOptions<SingleContainerAgentExecutionOptions> options,
        ILogger<SingleContainerAgentExecutionService> logger,
        IPerSessionEventStream perSession,
        HttpClient httpClient)
    {
        _options = options.Value;
        _logger = logger;
        _perSession = perSession;

        if (string.IsNullOrWhiteSpace(_options.WorkerUrl))
        {
            throw new InvalidOperationException(
                "AgentExecution:SingleContainer:WorkerUrl must be set when AgentExecution:Mode=SingleContainer.");
        }

        _httpClient = httpClient;
        _ownsHttpClient = false;
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

        var workerUrl = _options.WorkerUrl.TrimEnd('/');
        var startUrl = $"{workerUrl}/api/sessions";

        // POST to the worker — response is JSON (task 3 of the fix-post-result-events
        // plan: the worker no longer streams SSE from the start endpoint).
        var startJson = JsonSerializer.Serialize(startBody, CamelCaseJsonOptions);
        using var startContent = new StringContent(startJson, Encoding.UTF8, "application/json");
        using var startResponse = await _httpClient.PostAsync(startUrl, startContent, session.Cts.Token);
        if (!startResponse.IsSuccessStatusCode)
        {
            var errorBody = await startResponse.Content.ReadAsStringAsync(session.Cts.Token);
            throw new InvalidOperationException(
                $"SingleContainer worker at {startUrl} returned {startResponse.StatusCode}: {errorBody}");
        }

        var responseBody = await startResponse.Content.ReadAsStringAsync(session.Cts.Token);
        var parsed = JsonSerializer.Deserialize<WorkerStartResponse>(
            responseBody,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (parsed is null || string.IsNullOrEmpty(parsed.SessionId))
        {
            throw new InvalidOperationException(
                $"SingleContainer worker at {startUrl} returned an empty / malformed start response: {responseBody}");
        }

        session.WorkerSessionId = parsed.SessionId;

        // Start the long-lived per-session reader and drain the first turn's
        // messages through it. Matching task 8 of this plan: every executor
        // routes events through IPerSessionEventStream so post-result background
        // events (task_notification / task_updated / task_started) keep flowing
        // to the client after the turn's SdkResultMessage.
        await _perSession.StartAsync(sessionId, workerUrl, parsed.SessionId, request.ProjectId, session.Cts.Token);

        await foreach (var msg in DrainWithBestEffortStopAsync(sessionId, session.Cts.Token))
        {
            if (msg is SdkResultMessage result)
            {
                session.ConversationId = result.SessionId;
            }
            yield return msg;
        }
    }

    /// <summary>
    /// Wraps <see cref="IPerSessionEventStream.SubscribeTurnAsync"/> so a faulted
    /// turn drain best-effort stops the reader. Mirrors the Task 8 I1 follow-up
    /// on the Docker executor: if we skip this, a later session reusing the same
    /// id would see a stale reader subscribed to the wrong worker session.
    /// </summary>
    private async IAsyncEnumerable<SdkMessage> DrainWithBestEffortStopAsync(
        string sessionId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IAsyncEnumerator<SdkMessage>? enumerator = null;
        try
        {
            enumerator = _perSession.SubscribeTurnAsync(sessionId, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await BestEffortStopAsync(sessionId, ex);
            throw;
        }

        try
        {
            while (true)
            {
                bool hasNext;
                try
                {
                    hasNext = await enumerator.MoveNextAsync();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    await BestEffortStopAsync(sessionId, ex);
                    throw;
                }

                if (!hasNext) break;
                yield return enumerator.Current;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    private async Task BestEffortStopAsync(string sessionId, Exception cause)
    {
        try
        {
            await _perSession.StopAsync(sessionId);
        }
        catch (Exception stopEx)
        {
            _logger.LogWarning(stopEx,
                "Best-effort PerSessionEventStream.StopAsync failed for session {SessionId} after {Cause}",
                sessionId, cause.GetType().Name);
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

        // The worker's /message endpoint is JSON-only under the long-lived SSE design —
        // stream events ride the long-lived /events SSE consumed by PerSessionEventStream.
        var messageJson = JsonSerializer.Serialize(body, CamelCaseJsonOptions);
        using var content = new StringContent(messageJson, Encoding.UTF8, "application/json");
        using var response = await _httpClient.PostAsync(url, content, linked.Token);
        response.EnsureSuccessStatusCode();

        await foreach (var msg in _perSession.SubscribeTurnAsync(active.SessionId, linked.Token))
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

        // Halt the long-lived PerSessionEventStream reader before tearing the
        // session down so no post-stop events race an already-removed slot.
        // Even though this service doesn't tear down the container (it's
        // shared), it must still stop the reader so a subsequent session for
        // a different id doesn't have a stale reader subscribed to the wrong
        // session. StopAsync is safe to call for unknown ids.
        try
        {
            await _perSession.StopAsync(sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "PerSessionEventStream.StopAsync failed for session {SessionId}; continuing teardown", sessionId);
        }

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
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
        _slotLock.Dispose();
        _active?.Cts.Dispose();
    }
}
