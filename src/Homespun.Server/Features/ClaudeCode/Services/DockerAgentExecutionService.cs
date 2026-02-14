using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Homespun.Features.ClaudeCode.Exceptions;
using Homespun.Shared.Models.Sessions;
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
    /// Base path for per-issue project workspaces (relative to DataVolumePath).
    /// Default: "projects" -> {DataVolumePath}/projects/{projectName}/issues/{issueId}/
    /// </summary>
    public string ProjectsBasePath { get; set; } = "projects";

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
/// Spawns worker containers per issue and communicates via SSE over HTTP.
/// Containers are reused across sessions for the same issue.
/// The worker streams raw SDK messages which are passed through to the consumer.
/// </summary>
public class DockerAgentExecutionService : IAgentExecutionService, IAsyncDisposable
{
    private readonly DockerAgentExecutionOptions _options;
    private readonly ILogger<DockerAgentExecutionService> _logger;
    private readonly HttpClient _httpClient;

    // Session tracking: sessionId -> session info
    private readonly ConcurrentDictionary<string, DockerSession> _sessions = new();
    // Issue container tracking: issueId -> container info (for reuse) - legacy
    private readonly ConcurrentDictionary<string, IssueContainer> _issueContainers = new();
    // Clone container tracking: workingDirectory -> container info (for reuse)
    private readonly ConcurrentDictionary<string, CloneContainer> _cloneContainers = new();
    // Session-to-issue mapping for routing
    private readonly ConcurrentDictionary<string, string> _sessionToIssue = new();

    private static readonly JsonSerializerOptions SdkJsonOptions = SdkMessageParser.CreateJsonOptions();

    private static readonly JsonSerializerOptions CamelCaseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private record DockerSession(
        string SessionId,
        string ContainerId,
        string ContainerName,
        string WorkerUrl,
        string WorkingDirectory,
        string? WorkerSessionId,
        CancellationTokenSource Cts,
        DateTime CreatedAt,
        string? IssueId = null)
    {
        public string? ConversationId { get; set; }
        public DateTime LastActivityAt { get; set; } = CreatedAt;
    }

    private record IssueContainer(
        string IssueId,
        string ContainerId,
        string ContainerName,
        string WorkerUrl,
        DateTime CreatedAt);

    private record CloneContainer(
        string WorkingDirectory,
        string ContainerId,
        string ContainerName,
        string WorkerUrl,
        DateTime CreatedAt,
        string? IssueId = null);

    /// <summary>
    /// Response from the worker's /sessions/active endpoint.
    /// </summary>
    private record ActiveSessionResponse(
        bool HasActiveSession,
        string? SessionId,
        string? Status,
        bool? HasPendingQuestion,
        bool? HasPendingPlanApproval,
        string? LastActivityAt);

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

    /// <summary>
    /// Gets the container name for an issue. Deterministic naming enables reuse detection.
    /// </summary>
    internal static string GetIssueContainerName(string issueId)
        => $"homespun-issue-{issueId}";

    internal static string ParseWorkerIpAddress(string dockerInspectOutput)
    {
        if (string.IsNullOrWhiteSpace(dockerInspectOutput))
            throw new AgentStartupException(
                $"Docker inspect returned empty output (raw: '{dockerInspectOutput ?? "(null)"}')");

        var trimmed = dockerInspectOutput.Trim();

        // Strip surrounding quotes (single or double)
        if ((trimmed.StartsWith('"') && trimmed.EndsWith('"')) ||
            (trimmed.StartsWith('\'') && trimmed.EndsWith('\'')))
        {
            trimmed = trimmed[1..^1];
        }

        // Reject Go template nil output
        if (trimmed.StartsWith('<'))
            throw new AgentStartupException(
                $"Docker inspect returned nil/template value (raw: '{dockerInspectOutput.Trim()}')");

        // Try direct parse for the clean case (must be dotted-quad IPv4)
        if (IsValidIPv4(trimmed))
            return trimmed;

        // Fall back: scan substrings from start to extract first valid IPv4
        // Handles concatenated IPs from docker inspect (e.g. "172.17.0.3172.18.0.4")
        // Min IPv4 length = 7 ("0.0.0.0"), max = 15 ("255.255.255.255")
        for (var len = 7; len <= Math.Min(15, trimmed.Length); len++)
        {
            var candidate = trimmed[..len];
            if (IsValidIPv4(candidate))
                return candidate;
        }

        throw new AgentStartupException(
            $"Could not parse IP address from docker inspect output (raw: '{dockerInspectOutput.Trim()}')");
    }

    internal static string BuildWorkerUrl(string ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            throw new AgentStartupException("Worker IP address is null or empty");

        var url = $"http://{ipAddress}:8080";

        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
            throw new AgentStartupException($"Constructed worker URL is invalid: '{url}'");

        return url;
    }

    private static bool IsValidIPv4(string value)
        => value.Count(c => c == '.') == 3
           && IPAddress.TryParse(value, out var ip)
           && ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork;

    /// <inheritdoc />
    public async IAsyncEnumerable<SdkMessage> StartSessionAsync(
        AgentStartRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid().ToString();
        var hasIssue = !string.IsNullOrEmpty(request.IssueId);
        var containerName = hasIssue
            ? GetIssueContainerName(request.IssueId!)
            : $"homespun-agent-{sessionId[..8]}";

        _logger.LogInformation(
            "DockerAgentExecutionService: Starting session {SessionId} with worker image {Image}, container {ContainerName}, issue {IssueId}",
            sessionId, _options.WorkerImage, containerName, request.IssueId ?? "(none)");

        var channel = System.Threading.Channels.Channel.CreateUnbounded<SdkMessage>();

        _ = Task.Run(async () =>
        {
            string? containerId = null;
            try
            {
                string workerUrl;

                if (hasIssue)
                {
                    // Per-issue container: reuse if healthy, otherwise start new
                    (containerId, workerUrl) = await GetOrStartIssueContainerAsync(
                        request.IssueId!, request.ProjectName, request.WorkingDirectory, cancellationToken);
                }
                else
                {
                    // Legacy per-session container
                    (containerId, workerUrl) = await StartContainerAsync(
                        containerName, request.WorkingDirectory, useRm: true, cancellationToken: cancellationToken);
                }

                var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var session = new DockerSession(
                    sessionId, containerId, containerName, workerUrl, request.WorkingDirectory,
                    null, cts, DateTime.UtcNow, request.IssueId);

                _sessions[sessionId] = session;

                if (hasIssue)
                {
                    _sessionToIssue[sessionId] = request.IssueId!;
                }

                // Emit a synthetic system message to indicate session started
                await channel.Writer.WriteAsync(
                    new SdkSystemMessage(sessionId, null, "session_started", request.Model, null),
                    cancellationToken);

                var startRequest = new
                {
                    WorkingDirectory = "/workdir",
                    Mode = request.Mode.ToString(),
                    Model = request.Model,
                    Prompt = request.Prompt,
                    SystemPrompt = request.SystemPrompt,
                    ResumeSessionId = request.ResumeSessionId
                };

                await foreach (var msg in SendSseRequestAsync($"{workerUrl}/api/sessions", startRequest, sessionId, cts.Token))
                {
                    // Capture the worker session ID from session_started lifecycle event
                    if (msg is SdkSystemMessage sysMsg && sysMsg.Subtype == "session_started" &&
                        !string.IsNullOrEmpty(sysMsg.SessionId) && sysMsg.SessionId != sessionId)
                    {
                        session = session with { WorkerSessionId = sysMsg.SessionId };
                        _sessions[sessionId] = session;
                        _logger.LogInformation("Stored WorkerSessionId={WorkerSessionId} for Docker session {SessionId}",
                            sysMsg.SessionId, sessionId);
                    }

                    // Remap session IDs to our session ID
                    var remapped = RemapSessionId(msg, sessionId);
                    await channel.Writer.WriteAsync(remapped, cancellationToken);

                    if (msg is SdkResultMessage resultMsg)
                    {
                        session.ConversationId = resultMsg.SessionId;
                        _sessions[sessionId] = session;
                    }
                }
                channel.Writer.Complete();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error in Docker session {SessionId}", sessionId);

                // Only stop container for non-issue (ephemeral) containers
                if (!hasIssue && !string.IsNullOrEmpty(containerId))
                {
                    await StopContainerAsync(containerId);
                }

                channel.Writer.Complete(ex);
            }
            catch (OperationCanceledException)
            {
                channel.Writer.Complete();
            }
        }, cancellationToken);

        await foreach (var msg in channel.Reader.ReadAllAsync(cancellationToken))
        {
            yield return msg;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SdkMessage> SendMessageAsync(
        AgentMessageRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("SendMessageAsync called for session {SessionId}", request.SessionId);

        if (!_sessions.TryGetValue(request.SessionId, out var session))
        {
            _logger.LogError("Session {SessionId} not found in _sessions dictionary", request.SessionId);
            yield break;
        }

        if (string.IsNullOrEmpty(session.WorkerSessionId))
        {
            _logger.LogError("Worker session ID is empty - session was not properly initialized");
            yield break;
        }

        session.LastActivityAt = DateTime.UtcNow;

        var messageRequest = new
        {
            Message = request.Message,
            Model = request.Model,
            PermissionMode = request.PermissionMode.ToString()
        };

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Cts.Token);

        await foreach (var msg in SendSseRequestAsync(
            $"{session.WorkerUrl}/api/sessions/{session.WorkerSessionId}/message",
            messageRequest, request.SessionId, linkedCts.Token))
        {
            var remapped = RemapSessionId(msg, request.SessionId);
            yield return remapped;

            if (msg is SdkResultMessage resultMsg)
            {
                session.ConversationId = resultMsg.SessionId;
                _sessions[request.SessionId] = session;
            }
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

            // For issue containers, only stop the worker session (container stays running)
            if (!string.IsNullOrEmpty(session.IssueId))
            {
                _sessionToIssue.TryRemove(sessionId, out _);
                if (!string.IsNullOrEmpty(session.WorkerSessionId))
                {
                    try
                    {
                        var response = await _httpClient.DeleteAsync(
                            $"{session.WorkerUrl}/api/sessions/{session.WorkerSessionId}", cancellationToken);
                        _logger.LogDebug("Worker session {WorkerSessionId} stop response: {StatusCode}",
                            session.WorkerSessionId, response.StatusCode);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error stopping worker session {WorkerSessionId}", session.WorkerSessionId);
                    }
                }
            }
            else
            {
                // For legacy per-session containers, stop the entire container
                await StopContainerAsync(session.ContainerId);
            }
        }
    }

    /// <summary>
    /// Stops and removes the container for a specific issue.
    /// Called when an issue is completed or explicitly stopped.
    /// </summary>
    public async Task StopIssueContainerAsync(string issueId, CancellationToken cancellationToken = default)
    {
        if (_issueContainers.TryRemove(issueId, out var container))
        {
            _logger.LogInformation("Stopping issue container {ContainerName} for issue {IssueId}",
                container.ContainerName, issueId);

            // Remove all sessions for this issue
            var sessionIds = _sessionToIssue
                .Where(kvp => kvp.Value == issueId)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var sessionId in sessionIds)
            {
                if (_sessions.TryRemove(sessionId, out var session))
                {
                    session.Cts.Cancel();
                    session.Cts.Dispose();
                }
                _sessionToIssue.TryRemove(sessionId, out _);
            }

            await StopContainerAsync(container.ContainerId);
        }
    }

    /// <inheritdoc />
    public Task InterruptSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            _logger.LogInformation("Interrupting Docker session {SessionId}, container {ContainerName}",
                sessionId, session.ContainerName);

            session.Cts.Cancel();
            session.Cts.Dispose();

            var newCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var updatedSession = session with { Cts = newCts };
            _sessions[sessionId] = updatedSession;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<bool> AnswerQuestionAsync(string sessionId, Dictionary<string, string> answers,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            _logger.LogDebug("AnswerQuestionAsync: Session {SessionId} not found", sessionId);
            return false;
        }

        if (string.IsNullOrEmpty(session.WorkerSessionId))
        {
            _logger.LogWarning("AnswerQuestionAsync: Worker session ID not set for session {SessionId}", sessionId);
            return false;
        }

        try
        {
            var requestBody = new { Answers = answers };
            var json = JsonSerializer.Serialize(requestBody, CamelCaseJsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var url = $"{session.WorkerUrl}/api/sessions/{session.WorkerSessionId}/answer";
            _logger.LogInformation("AnswerQuestionAsync: POSTing answers to {Url} for session {SessionId}", url, sessionId);

            var response = await _httpClient.PostAsync(url, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("AnswerQuestionAsync: Worker resolved question for session {SessionId}", sessionId);
                return true;
            }

            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("AnswerQuestionAsync: Worker returned {StatusCode}: {Error} for session {SessionId}",
                response.StatusCode, errorBody, sessionId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AnswerQuestionAsync: Error posting answers for session {SessionId}", sessionId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ApprovePlanAsync(string sessionId, bool approved, bool keepContext, string? feedback = null,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            _logger.LogDebug("ApprovePlanAsync: Session {SessionId} not found", sessionId);
            return false;
        }

        if (string.IsNullOrEmpty(session.WorkerSessionId))
        {
            _logger.LogWarning("ApprovePlanAsync: Worker session ID not set for session {SessionId}", sessionId);
            return false;
        }

        try
        {
            var requestBody = new { Approved = approved, KeepContext = keepContext, Feedback = feedback };
            var json = JsonSerializer.Serialize(requestBody, CamelCaseJsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var url = $"{session.WorkerUrl}/api/sessions/{session.WorkerSessionId}/approve-plan";
            _logger.LogInformation("ApprovePlanAsync: POSTing plan decision to {Url} for session {SessionId}, approved={Approved}, keepContext={KeepContext}",
                url, sessionId, approved, keepContext);

            var response = await _httpClient.PostAsync(url, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("ApprovePlanAsync: Worker resolved plan approval for session {SessionId}", sessionId);
                return true;
            }

            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning("ApprovePlanAsync: Worker returned {StatusCode}: {Error} for session {SessionId}",
                response.StatusCode, errorBody, sessionId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ApprovePlanAsync: Error posting plan decision for session {SessionId}", sessionId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<CloneContainerState?> GetCloneContainerStateAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!_cloneContainers.TryGetValue(workingDirectory, out var container))
        {
            _logger.LogDebug("GetCloneContainerStateAsync: No container found for working directory {WorkingDirectory}",
                workingDirectory);
            return null;
        }

        // Check if the container is still healthy
        if (!await IsContainerHealthyAsync(container.WorkerUrl, cancellationToken))
        {
            _logger.LogWarning("GetCloneContainerStateAsync: Container {ContainerName} for {WorkingDirectory} is unhealthy, removing",
                container.ContainerName, workingDirectory);
            _cloneContainers.TryRemove(workingDirectory, out _);
            return null;
        }

        // Query worker for active session
        try
        {
            var response = await _httpClient.GetAsync(
                $"{container.WorkerUrl}/api/sessions/active", cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("GetCloneContainerStateAsync: Worker returned {StatusCode} for active session query",
                    response.StatusCode);
                return new CloneContainerState(
                    workingDirectory, container.ContainerId, null, null,
                    ClaudeSessionStatus.Stopped, null, false, false);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var activeSession = JsonSerializer.Deserialize<ActiveSessionResponse>(json, CamelCaseJsonOptions);

            if (activeSession?.HasActiveSession != true)
            {
                return new CloneContainerState(
                    workingDirectory, container.ContainerId, null, null,
                    ClaudeSessionStatus.Stopped, null, false, false);
            }

            // Map worker session status to ClaudeSessionStatus
            var sessionStatus = MapWorkerSessionStatus(activeSession);
            DateTime? lastActivity = null;
            if (activeSession.LastActivityAt != null && DateTime.TryParse(activeSession.LastActivityAt, out var parsed))
            {
                lastActivity = parsed;
            }

            return new CloneContainerState(
                workingDirectory,
                container.ContainerId,
                activeSession.SessionId,
                activeSession.SessionId,
                sessionStatus,
                lastActivity,
                activeSession.HasPendingQuestion ?? false,
                activeSession.HasPendingPlanApproval ?? false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetCloneContainerStateAsync: Error querying worker for active session");
            return new CloneContainerState(
                workingDirectory, container.ContainerId, null, null,
                ClaudeSessionStatus.Error, null, false, false);
        }
    }

    /// <inheritdoc />
    public async Task TerminateCloneSessionAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
    {
        if (!_cloneContainers.TryGetValue(workingDirectory, out var container))
        {
            _logger.LogDebug("TerminateCloneSessionAsync: No container found for working directory {WorkingDirectory}",
                workingDirectory);
            return;
        }

        try
        {
            // Get active session from worker
            var response = await _httpClient.GetAsync(
                $"{container.WorkerUrl}/api/sessions/active", cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var activeSession = JsonSerializer.Deserialize<ActiveSessionResponse>(json, CamelCaseJsonOptions);

                if (activeSession?.HasActiveSession == true && activeSession.SessionId != null)
                {
                    // Stop the worker session
                    _logger.LogInformation("TerminateCloneSessionAsync: Terminating worker session {WorkerSessionId} for {WorkingDirectory}",
                        activeSession.SessionId, workingDirectory);

                    await _httpClient.DeleteAsync(
                        $"{container.WorkerUrl}/api/sessions/{activeSession.SessionId}", cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TerminateCloneSessionAsync: Error terminating worker session for {WorkingDirectory}",
                workingDirectory);
        }

        // Also remove from our session tracking
        var sessionsToRemove = _sessions.Values
            .Where(s => s.WorkingDirectory == workingDirectory)
            .Select(s => s.SessionId)
            .ToList();

        foreach (var sessionId in sessionsToRemove)
        {
            if (_sessions.TryRemove(sessionId, out var session))
            {
                session.Cts.Cancel();
                session.Cts.Dispose();
            }
            _sessionToIssue.TryRemove(sessionId, out _);
        }

        _logger.LogDebug("TerminateCloneSessionAsync: Removed {Count} local sessions for {WorkingDirectory}",
            sessionsToRemove.Count, workingDirectory);
    }

    /// <summary>
    /// Maps worker session status to ClaudeSessionStatus enum.
    /// </summary>
    private static ClaudeSessionStatus MapWorkerSessionStatus(ActiveSessionResponse activeSession)
    {
        if (activeSession.HasPendingQuestion == true)
            return ClaudeSessionStatus.WaitingForQuestionAnswer;

        if (activeSession.HasPendingPlanApproval == true)
            return ClaudeSessionStatus.WaitingForPlanExecution;

        return activeSession.Status switch
        {
            "streaming" => ClaudeSessionStatus.Running,
            "idle" => ClaudeSessionStatus.WaitingForInput,
            "closed" => ClaudeSessionStatus.Stopped,
            _ => ClaudeSessionStatus.Running
        };
    }

    /// <inheritdoc />
    public Task<AgentSessionStatus?> GetSessionStatusAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            return Task.FromResult<AgentSessionStatus?>(new AgentSessionStatus(
                session.SessionId,
                session.WorkingDirectory,
                SessionMode.Build, // TODO: Store actual mode
                "sonnet", // TODO: Store actual model
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
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            _logger.LogDebug("ReadFileFromAgentAsync: Session {SessionId} not found", sessionId);
            return null;
        }

        try
        {
            var requestBody = new { FilePath = filePath };
            var json = JsonSerializer.Serialize(requestBody, CamelCaseJsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                $"{session.WorkerUrl}/api/files/read", content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("ReadFileFromAgentAsync: Worker returned {StatusCode} for {Path} in session {SessionId}",
                    response.StatusCode, filePath, sessionId);
                return null;
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(responseJson);

            if (doc.RootElement.TryGetProperty("content", out var contentElement))
            {
                var fileContent = contentElement.GetString();
                _logger.LogInformation("ReadFileFromAgentAsync: Successfully read {Path} from agent ({Length} chars)",
                    filePath, fileContent?.Length ?? 0);
                return fileContent;
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ReadFileFromAgentAsync: Error reading {Path} from agent container for session {SessionId}",
                filePath, sessionId);
            return null;
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AgentSessionStatus>> ListSessionsAsync(CancellationToken cancellationToken = default)
    {
        var statuses = _sessions.Values.Select(session => new AgentSessionStatus(
            session.SessionId,
            session.WorkingDirectory,
            SessionMode.Build, // TODO: Store actual mode
            "sonnet", // TODO: Store actual model
            session.ConversationId,
            session.CreatedAt,
            session.LastActivityAt
        )).ToList().AsReadOnly();

        return Task.FromResult<IReadOnlyList<AgentSessionStatus>>(statuses);
    }

    /// <inheritdoc />
    public async Task<int> CleanupOrphanedContainersAsync(CancellationToken cancellationToken = default)
    {
        var cleanedUp = 0;

        try
        {
            // List all running containers matching homespun-agent- prefix
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "ps --filter \"name=homespun-agent-\" --format \"{{.ID}}\t{{.Names}}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null)
            {
                _logger.LogWarning("Failed to start docker ps process for orphan detection");
                return 0;
            }

            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("docker ps exited with code {ExitCode} during orphan detection", process.ExitCode);
                return 0;
            }

            // Get set of tracked container IDs
            var trackedContainerIds = _sessions.Values.Select(s => s.ContainerId).ToHashSet();

            // Parse output and find orphaned containers
            foreach (var line in stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('\t', 2);
                if (parts.Length < 2) continue;

                var containerId = parts[0].Trim();
                var containerName = parts[1].Trim();

                if (!trackedContainerIds.Contains(containerId))
                {
                    _logger.LogInformation("Found orphaned container {ContainerName} ({ContainerId}), stopping",
                        containerName, containerId);
                    await StopContainerAsync(containerId);
                    cleanedUp++;
                }
            }

            if (cleanedUp > 0)
            {
                _logger.LogInformation("Cleaned up {Count} orphaned agent container(s)", cleanedUp);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during orphaned container cleanup");
        }

        return cleanedUp;
    }

    /// <summary>
    /// Translates a container path to a host path for Docker mounts.
    /// </summary>
    public string TranslateToHostPath(string containerPath)
    {
        if (string.IsNullOrEmpty(_options.HostDataPath))
            return containerPath;

        if (containerPath.StartsWith(_options.DataVolumePath, StringComparison.OrdinalIgnoreCase))
        {
            var relativePath = containerPath[_options.DataVolumePath.Length..].TrimStart('/');
            return string.IsNullOrEmpty(relativePath)
                ? _options.HostDataPath
                : $"{_options.HostDataPath}/{relativePath}";
        }

        return containerPath;
    }

    /// <summary>
    /// Gets an existing healthy issue container, or starts a new one.
    /// Also tracks the container by working directory for state checking.
    /// </summary>
    private async Task<(string containerId, string workerUrl)> GetOrStartIssueContainerAsync(
        string issueId,
        string? projectName,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        // Check for existing healthy container by issueId
        if (_issueContainers.TryGetValue(issueId, out var existing))
        {
            if (await IsContainerHealthyAsync(existing.WorkerUrl, cancellationToken))
            {
                _logger.LogDebug("Reusing existing container {ContainerName} for issue {IssueId}",
                    existing.ContainerName, issueId);
                return (existing.ContainerId, existing.WorkerUrl);
            }

            _logger.LogWarning("Container {ContainerName} for issue {IssueId} is unhealthy, removing",
                existing.ContainerName, issueId);
            _issueContainers.TryRemove(issueId, out _);
            await StopContainerAsync(existing.ContainerId);
        }

        // Start new container - compute issue workspace working directory if no explicit one provided
        var containerName = GetIssueContainerName(issueId);
        var effectiveWorkingDirectory = workingDirectory;
        if (string.IsNullOrEmpty(effectiveWorkingDirectory))
        {
            var issueBasePath = $"{_options.DataVolumePath}/{_options.ProjectsBasePath}/{projectName ?? "default"}/issues/{issueId}";
            effectiveWorkingDirectory = $"{issueBasePath}/src";
        }
        var (containerId, workerUrl) = await StartContainerAsync(
            containerName, effectiveWorkingDirectory, useRm: false, issueId, projectName, cancellationToken);

        // Track both by issueId (legacy) and by workingDirectory (new)
        _issueContainers[issueId] = new IssueContainer(issueId, containerId, containerName, workerUrl, DateTime.UtcNow);
        _cloneContainers[effectiveWorkingDirectory] = new CloneContainer(
            effectiveWorkingDirectory, containerId, containerName, workerUrl, DateTime.UtcNow, issueId);

        return (containerId, workerUrl);
    }

    /// <summary>
    /// Checks if a container's worker is healthy.
    /// </summary>
    private async Task<bool> IsContainerHealthyAsync(string workerUrl, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"{workerUrl}/api/health", cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Builds Docker run arguments for a worker container.
    /// Mounts the provided working directory to /workdir in the container.
    /// </summary>
    internal string BuildContainerDockerArgs(
        string containerName,
        string workingDirectory,
        bool useRm,
        string? issueId = null,
        string? projectName = null)
    {
        var dockerArgs = new StringBuilder();
        dockerArgs.Append(useRm ? "run -d --rm " : "run -d ");
        dockerArgs.Append($"--name {containerName} ");
        dockerArgs.Append($"--memory {_options.MemoryLimitBytes} ");
        dockerArgs.Append($"--cpus {_options.CpuLimit} ");

        var userFlag = ProcessUserInfo.GetDockerUserFlag();
        if (!string.IsNullOrEmpty(userFlag))
        {
            dockerArgs.Append($"--user {userFlag} ");
        }

        // Mount working directory to /workdir
        var hostWorkingDirectory = TranslateToHostPath(workingDirectory);
        dockerArgs.Append($"-v \"{hostWorkingDirectory}:/workdir\" ");

        // Environment variables
        dockerArgs.Append("-e WORKING_DIRECTORY=/workdir ");

        if (!string.IsNullOrEmpty(issueId))
            dockerArgs.Append($"-e ISSUE_ID={issueId} ");
        if (!string.IsNullOrEmpty(projectName))
            dockerArgs.Append($"-e PROJECT_NAME={projectName} ");

        AppendAuthEnvironmentVars(dockerArgs);
        AppendCredentialsMount(dockerArgs);

        dockerArgs.Append($"--network {_options.NetworkName} ");
        dockerArgs.Append(_options.WorkerImage);

        return dockerArgs.ToString();
    }

    /// <summary>
    /// Starts a worker container with the working directory mounted to /workdir.
    /// </summary>
    private async Task<(string containerId, string workerUrl)> StartContainerAsync(
        string containerName,
        string workingDirectory,
        bool useRm,
        string? issueId = null,
        string? projectName = null,
        CancellationToken cancellationToken = default)
    {
        var dockerArgs = BuildContainerDockerArgs(containerName, workingDirectory, useRm, issueId, projectName);
        return await RunDockerAndGetUrl(containerName, dockerArgs, cancellationToken);
    }

    private void AppendAuthEnvironmentVars(StringBuilder dockerArgs)
    {
        var envVarsToPassthrough = new[]
        {
            "CLAUDE_CODE_OAUTH_TOKEN", "ANTHROPIC_API_KEY", "GITHUB_TOKEN",
            "GIT_AUTHOR_NAME", "GIT_AUTHOR_EMAIL", "GIT_COMMITTER_NAME", "GIT_COMMITTER_EMAIL"
        };
        foreach (var envVar in envVarsToPassthrough)
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrEmpty(value))
            {
                dockerArgs.Append($"-e {envVar}=\"{value}\" ");
                _logger.LogDebug("Passing environment variable {EnvVar} to agent container", envVar);
            }
        }
    }

    private void AppendCredentialsMount(StringBuilder dockerArgs)
    {
        var homeDir = Environment.GetEnvironmentVariable("HOME");
        if (!string.IsNullOrEmpty(homeDir))
        {
            var credentialsFile = Path.Combine(homeDir, ".claude", ".credentials.json");
            if (File.Exists(credentialsFile))
            {
                dockerArgs.Append($"-v \"{credentialsFile}:/home/homespun/.claude/.credentials.json:ro\" ");
                _logger.LogDebug("Mounting Claude credentials file for agent container");
            }
        }
    }

    private async Task<(string containerId, string workerUrl)> RunDockerAndGetUrl(
        string containerName,
        string dockerArgs,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Docker run for container {ContainerName}: docker {DockerArgs}",
            containerName, dockerArgs);

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "docker",
            Arguments = dockerArgs,
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

        _logger.LogInformation(
            "Docker run result for {ContainerName}: exit={ExitCode}, stdout='{Stdout}', stderr='{Stderr}'",
            containerName, process.ExitCode, stdout.Trim(), stderr.Trim());

        if (process.ExitCode != 0)
        {
            throw new AgentStartupException($"Docker run failed: {stderr}");
        }

        var containerId = stdout.Trim();

        // Verify container is actually running before inspecting network
        var containerState = await GetContainerStateAsync(containerId, cancellationToken);
        _logger.LogInformation(
            "Container {ContainerId} state after run: {ContainerState}",
            containerId, containerState);

        if (containerState != "running")
        {
            var containerLogs = await GetContainerLogsAsync(containerId, cancellationToken);
            _logger.LogError(
                "Container {ContainerId} is not running (state: {ContainerState}). Container logs:\n{ContainerLogs}",
                containerId, containerState, containerLogs);

            throw new AgentStartupException(
                $"Container {containerName} exited immediately (state: {containerState}). Container logs:\n{containerLogs}");
        }

        var inspectArgs = $"inspect -f \"{{{{range.NetworkSettings.Networks}}}}{{{{.IPAddress}}}}{{{{end}}}}\" {containerId}";
        _logger.LogInformation("Docker inspect command: docker {InspectArgs}", inspectArgs);

        var inspectStartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "docker",
            Arguments = inspectArgs,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var inspectProcess = System.Diagnostics.Process.Start(inspectStartInfo)
            ?? throw new AgentStartupException($"Failed to inspect container {containerId}");

        var inspectStdout = await inspectProcess.StandardOutput.ReadToEndAsync(cancellationToken);
        var inspectStderr = await inspectProcess.StandardError.ReadToEndAsync(cancellationToken);
        await inspectProcess.WaitForExitAsync(cancellationToken);

        _logger.LogInformation(
            "Docker inspect IP result for {ContainerId}: exit={ExitCode}, stdout='{Stdout}' (len={StdoutLen}), stderr='{Stderr}'",
            containerId, inspectProcess.ExitCode, inspectStdout.Trim(), inspectStdout.Length, inspectStderr.Trim());

        if (inspectProcess.ExitCode != 0)
        {
            // Capture full container details on failure for diagnostics
            var fullInspect = await GetFullContainerInspectAsync(containerId, cancellationToken);
            _logger.LogError(
                "Docker inspect failed for container {ContainerId} (exit code {ExitCode}). Full inspect: {FullInspect}",
                containerId, inspectProcess.ExitCode, fullInspect);

            throw new AgentStartupException(
                $"Docker inspect failed for container {containerId} (exit code {inspectProcess.ExitCode}): {inspectStderr.Trim()}");
        }

        string ipAddress;
        try
        {
            ipAddress = ParseWorkerIpAddress(inspectStdout);
        }
        catch (AgentStartupException ex)
        {
            // IP parse failed — capture full container network details for diagnostics
            var networkInspect = await GetContainerNetworkInspectAsync(containerId, cancellationToken);
            _logger.LogError(
                "Failed to parse IP for container {ContainerId}. " +
                "Raw inspect stdout: '{RawStdout}' (bytes: [{RawBytes}]). " +
                "Network details: {NetworkInspect}",
                containerId,
                inspectStdout.Trim(),
                string.Join(", ", System.Text.Encoding.UTF8.GetBytes(inspectStdout.Trim()).Select(b => $"0x{b:X2}")),
                networkInspect);
            throw;
        }

        var workerUrl = BuildWorkerUrl(ipAddress);
        _logger.LogInformation(
            "Container {ContainerId} ready at {WorkerUrl} (IP: {IpAddress})",
            containerId, workerUrl, ipAddress);

        await WaitForWorkerReadyAsync(workerUrl, cancellationToken);

        return (containerId, workerUrl);
    }

    /// <summary>
    /// Gets the running state of a container (e.g. "running", "exited", "created").
    /// </summary>
    private async Task<string> GetContainerStateAsync(string containerId, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"inspect -f \"{{{{.State.Status}}}}\" {containerId}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null) return "(failed to start docker inspect)";

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return output.Trim();
        }
        catch (Exception ex)
        {
            return $"(error: {ex.Message})";
        }
    }

    /// <summary>
    /// Gets the stdout/stderr logs from a container (for crash diagnostics).
    /// </summary>
    private async Task<string> GetContainerLogsAsync(string containerId, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"logs {containerId}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null) return "(failed to start docker logs)";

            var stdout = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = await process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            var combined = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(stdout))
                combined.Append(stdout.Trim());
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                if (combined.Length > 0) combined.Append('\n');
                combined.Append(stderr.Trim());
            }

            return combined.Length > 0 ? combined.ToString() : "(no logs)";
        }
        catch (Exception ex)
        {
            return $"(error retrieving logs: {ex.Message})";
        }
    }

    /// <summary>
    /// Gets full JSON inspect output for a container (for diagnostics).
    /// </summary>
    private async Task<string> GetFullContainerInspectAsync(string containerId, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"inspect {containerId}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null) return "(failed to start docker inspect)";

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return output.Trim();
        }
        catch (Exception ex)
        {
            return $"(error: {ex.Message})";
        }
    }

    /// <summary>
    /// Gets network-specific inspect output for a container (for IP diagnostics).
    /// </summary>
    private async Task<string> GetContainerNetworkInspectAsync(string containerId, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = $"inspect -f \"{{{{json .NetworkSettings}}}}\" {containerId}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null) return "(failed to start docker inspect)";

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return output.Trim();
        }
        catch (Exception ex)
        {
            return $"(error: {ex.Message})";
        }
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
            catch (UriFormatException ex)
            {
                // Bad URL won't fix itself on retry
                throw new AgentStartupException($"Worker URL is malformed: {workerUrl} ({ex.Message})");
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

    private async IAsyncEnumerable<SdkMessage> SendSseRequestAsync(
        string url,
        object requestBody,
        string sessionId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(requestBody, CamelCaseJsonOptions);
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
                    var msg = ParseSseEvent(currentEventType, dataBuffer.ToString(), sessionId);
                    if (msg != null)
                    {
                        yield return msg;

                        if (msg is SdkResultMessage)
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

    private SdkMessage? ParseSseEvent(string eventType, string data, string sessionId)
    {
        try
        {
            // "session_started" is a custom lifecycle event, not a raw SDK message
            if (eventType == "session_started")
            {
                // Parse to extract the worker session ID
                using var doc = JsonDocument.Parse(data);
                var workerSessionId = doc.RootElement.TryGetProperty("sessionId", out var sid)
                    ? sid.GetString() : null;

                return new SdkSystemMessage(
                    workerSessionId ?? sessionId,
                    null,
                    "session_started",
                    null,
                    null
                );
            }

            // "question_pending" is a control event from canUseTool
            if (eventType == "question_pending")
            {
                return new SdkQuestionPendingMessage(sessionId, data);
            }

            // "plan_pending" is a control event from canUseTool (ExitPlanMode paused)
            if (eventType == "plan_pending")
            {
                return new SdkPlanPendingMessage(sessionId, data);
            }

            // "error" is a custom lifecycle event
            if (eventType == "error")
            {
                _logger.LogWarning("Received error event from worker: {Data}", data);
                return null;
            }

            // All other events are raw SDK messages — deserialize using SdkMessage converters
            return JsonSerializer.Deserialize<SdkMessage>(data, SdkJsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse SSE event {EventType}: {Data}", eventType, data);
            return null;
        }
    }

    /// <summary>
    /// Creates a new SdkMessage with the session ID remapped to our session ID.
    /// </summary>
    private static SdkMessage RemapSessionId(SdkMessage msg, string sessionId)
    {
        return msg switch
        {
            SdkAssistantMessage m => m with { SessionId = sessionId },
            SdkUserMessage m => m with { SessionId = sessionId },
            SdkResultMessage m => m with { SessionId = sessionId },
            SdkSystemMessage m => m with { SessionId = sessionId },
            SdkStreamEvent m => m with { SessionId = sessionId },
            SdkQuestionPendingMessage m => m with { SessionId = sessionId },
            SdkPlanPendingMessage m => m with { SessionId = sessionId },
            _ => msg
        };
    }

    public async ValueTask DisposeAsync()
    {
        // Stop all sessions
        foreach (var session in _sessions.Values)
        {
            try
            {
                session.Cts.Cancel();
                session.Cts.Dispose();
            }
            catch { }
        }

        // Stop all issue containers
        foreach (var container in _issueContainers.Values)
        {
            try
            {
                await StopContainerAsync(container.ContainerId);
            }
            catch { }
        }

        // Stop any legacy session containers (those without IssueId)
        foreach (var session in _sessions.Values.Where(s => string.IsNullOrEmpty(s.IssueId)))
        {
            try
            {
                await StopContainerAsync(session.ContainerId);
            }
            catch { }
        }

        _sessions.Clear();
        _issueContainers.Clear();
        _cloneContainers.Clear();
        _sessionToIssue.Clear();
        _httpClient.Dispose();
    }
}
