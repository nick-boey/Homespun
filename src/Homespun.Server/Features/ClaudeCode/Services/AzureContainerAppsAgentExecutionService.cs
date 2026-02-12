using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Azure;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppContainers;
using Azure.ResourceManager.AppContainers.Models;
using Homespun.Features.ClaudeCode.Exceptions;
using Microsoft.Extensions.Options;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Configuration options for Azure Container Apps agent execution.
/// Supports dynamic per-issue Container App creation via ARM API.
/// </summary>
public class AzureContainerAppsAgentExecutionOptions
{
    public const string SectionName = "AgentExecution:AzureContainerApps";

    /// <summary>
    /// Base URL of the worker container app (legacy, used when no EnvironmentId is configured).
    /// </summary>
    public string WorkerEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// ACA managed environment resource ID for dynamic Container App creation.
    /// </summary>
    public string? EnvironmentId { get; set; }

    /// <summary>
    /// Container image for the Hono worker.
    /// </summary>
    public string WorkerImage { get; set; } = "ghcr.io/nick-boey/homespun-worker:latest";

    /// <summary>
    /// Resource group name for dynamic Container Apps.
    /// </summary>
    public string? ResourceGroupName { get; set; }

    /// <summary>
    /// NFS storage mount name in the ACA environment.
    /// </summary>
    public string? StorageMountName { get; set; }

    /// <summary>
    /// Base path for per-issue project workspaces within the NFS share.
    /// </summary>
    public string ProjectsBasePath { get; set; } = "projects";

    /// <summary>
    /// Timeout for HTTP requests to the worker.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Maximum session duration before automatic termination.
    /// </summary>
    public TimeSpan MaxSessionDuration { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Timeout for waiting for Container App provisioning.
    /// </summary>
    public TimeSpan ProvisioningTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Whether dynamic Container App creation is enabled.
    /// True when EnvironmentId and ResourceGroupName are configured.
    /// </summary>
    public bool IsDynamicMode => !string.IsNullOrEmpty(EnvironmentId) && !string.IsNullOrEmpty(ResourceGroupName);
}

/// <summary>
/// Azure Container Apps implementation for agent execution.
/// Supports two modes:
/// - Legacy: Routes all requests to a single shared worker Container App
/// - Dynamic: Creates per-issue Container Apps via ARM API
/// The worker streams raw SDK messages which are passed through to the consumer.
/// </summary>
public class AzureContainerAppsAgentExecutionService : IAgentExecutionService, IAsyncDisposable
{
    private readonly AzureContainerAppsAgentExecutionOptions _options;
    private readonly ILogger<AzureContainerAppsAgentExecutionService> _logger;
    private readonly HttpClient _httpClient;
    private readonly ConcurrentDictionary<string, WorkerSession> _sessions = new();
    private readonly ConcurrentDictionary<string, IssueContainerApp> _issueApps = new();
    private readonly ConcurrentDictionary<string, string> _sessionToIssue = new();
    private readonly ArmClient? _armClient;

    private static readonly JsonSerializerOptions SdkJsonOptions = SdkMessageParser.CreateJsonOptions();

    private static readonly JsonSerializerOptions CamelCaseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private record WorkerSession(
        string SessionId,
        string? WorkerSessionId,
        string WorkerUrl,
        CancellationTokenSource Cts,
        DateTime CreatedAt,
        string? IssueId = null)
    {
        public string? ConversationId { get; set; }
        public DateTime LastActivityAt { get; set; } = CreatedAt;
    }

    private record IssueContainerApp(
        string IssueId,
        string ContainerAppName,
        string WorkerUrl,
        DateTime CreatedAt);

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

        if (_options.IsDynamicMode)
        {
            _armClient = new ArmClient(new DefaultAzureCredential());
        }
    }

    /// <summary>
    /// Gets the Container App name for an issue.
    /// Must be lowercase, alphanumeric + hyphens, max 32 chars.
    /// </summary>
    internal static string GetIssueContainerAppName(string issueId)
    {
        var name = $"ca-issue-{issueId}".ToLowerInvariant();
        return name.Length > 32 ? name[..32] : name;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SdkMessage> StartSessionAsync(
        AgentStartRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid().ToString();
        var hasIssue = _options.IsDynamicMode && !string.IsNullOrEmpty(request.IssueId);

        _logger.LogInformation("Starting ACA session {SessionId}, dynamic={Dynamic}, issue={IssueId}",
            sessionId, hasIssue, request.IssueId ?? "(none)");

        var channel = System.Threading.Channels.Channel.CreateUnbounded<SdkMessage>();

        _ = Task.Run(async () =>
        {
            try
            {
                string workerUrl;
                if (hasIssue)
                {
                    workerUrl = await GetOrCreateIssueContainerAppAsync(
                        request.IssueId!, request.ProjectName, request.WorkingDirectory, cancellationToken);
                }
                else
                {
                    workerUrl = _options.WorkerEndpoint.TrimEnd('/');
                }

                var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var session = new WorkerSession(sessionId, null, workerUrl, cts, DateTime.UtcNow, request.IssueId);
                _sessions[sessionId] = session;

                if (hasIssue)
                {
                    _sessionToIssue[sessionId] = request.IssueId!;
                }

                var startRequest = new
                {
                    WorkingDirectory = "/workdir",
                    Mode = request.Mode.ToString(),
                    Model = request.Model,
                    Prompt = request.Prompt,
                    SystemPrompt = request.SystemPrompt,
                    ResumeSessionId = request.ResumeSessionId
                };

                var url = $"{workerUrl}/api/sessions";

                await foreach (var msg in SendSseRequestAsync(url, startRequest, sessionId, cts.Token))
                {
                    if (msg is SdkSystemMessage sysMsg && sysMsg.Subtype == "session_started" &&
                        !string.IsNullOrEmpty(sysMsg.SessionId) && sysMsg.SessionId != sessionId)
                    {
                        session = session with { WorkerSessionId = sysMsg.SessionId };
                        _sessions[sessionId] = session;
                    }

                    await channel.Writer.WriteAsync(RemapSessionId(msg, sessionId), cancellationToken);

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
                _logger.LogError(ex, "Error in ACA session {SessionId}", sessionId);
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
        if (!_sessions.TryGetValue(request.SessionId, out var session))
        {
            _logger.LogError("Session {SessionId} not found", request.SessionId);
            yield break;
        }

        if (string.IsNullOrEmpty(session.WorkerSessionId))
        {
            _logger.LogError("Worker session not initialized for session {SessionId}", request.SessionId);
            yield break;
        }

        session.LastActivityAt = DateTime.UtcNow;

        var messageRequest = new
        {
            Message = request.Message,
            Model = request.Model,
            PermissionMode = request.PermissionMode.ToString()
        };

        var workerUrl = $"{session.WorkerUrl}/api/sessions/{session.WorkerSessionId}/message";

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.Cts.Token);

        await foreach (var msg in SendSseRequestAsync(workerUrl, messageRequest, request.SessionId, linkedCts.Token))
        {
            yield return RemapSessionId(msg, request.SessionId);

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
            _logger.LogInformation("Stopping ACA session {SessionId}", sessionId);

            session.Cts.Cancel();
            session.Cts.Dispose();

            _sessionToIssue.TryRemove(sessionId, out _);

            if (!string.IsNullOrEmpty(session.WorkerSessionId))
            {
                await DeleteWorkerSessionAsync(session.WorkerUrl, session.WorkerSessionId);
            }
        }
    }

    /// <summary>
    /// Deletes the Container App for a specific issue.
    /// Called when an issue is completed.
    /// </summary>
    public async Task DeleteIssueContainerAppAsync(string issueId, CancellationToken cancellationToken = default)
    {
        if (!_options.IsDynamicMode || _armClient == null)
        {
            _logger.LogWarning("Cannot delete issue Container App: dynamic mode not configured");
            return;
        }

        if (_issueApps.TryRemove(issueId, out var app))
        {
            _logger.LogInformation("Deleting Container App {AppName} for issue {IssueId}",
                app.ContainerAppName, issueId);

            // Remove all sessions for this issue
            var sessionIds = _sessionToIssue
                .Where(kvp => kvp.Value == issueId)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var sid in sessionIds)
            {
                if (_sessions.TryRemove(sid, out var session))
                {
                    session.Cts.Cancel();
                    session.Cts.Dispose();
                }
                _sessionToIssue.TryRemove(sid, out _);
            }

            try
            {
                var resourceId = ContainerAppResource.CreateResourceIdentifier(
                    _armClient.GetDefaultSubscription().Data.SubscriptionId,
                    _options.ResourceGroupName!,
                    app.ContainerAppName);
                var containerApp = _armClient.GetContainerAppResource(resourceId);
                await containerApp.DeleteAsync(WaitUntil.Started, cancellationToken);
                _logger.LogInformation("Container App {AppName} deletion initiated", app.ContainerAppName);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error deleting Container App {AppName}", app.ContainerAppName);
            }
        }
    }

    /// <inheritdoc />
    public Task InterruptSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            _logger.LogInformation("Interrupting ACA session {SessionId}", sessionId);

            session.Cts.Cancel();
            session.Cts.Dispose();

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
                "/workdir",
                SessionMode.Build,
                "sonnet",
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

            var url = $"{session.WorkerUrl}/api/files/read";
            var response = await _httpClient.PostAsync(url, content, cancellationToken);

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
            _logger.LogWarning(ex, "ReadFileFromAgentAsync: Error reading {Path} from Azure agent for session {SessionId}",
                filePath, sessionId);
            return null;
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AgentSessionStatus>> ListSessionsAsync(CancellationToken cancellationToken = default)
    {
        var statuses = _sessions.Values.Select(session => new AgentSessionStatus(
            session.SessionId,
            "/data",
            SessionMode.Build,
            "sonnet",
            session.ConversationId,
            session.CreatedAt,
            session.LastActivityAt
        )).ToList().AsReadOnly();

        return Task.FromResult<IReadOnlyList<AgentSessionStatus>>(statuses);
    }

    /// <inheritdoc />
    public Task<int> CleanupOrphanedContainersAsync(CancellationToken cancellationToken = default)
    {
        // Azure Container Apps sessions are managed by the platform;
        // orphaned sessions are handled via session deletion.
        return Task.FromResult(0);
    }

    private async Task<string> GetOrCreateIssueContainerAppAsync(
        string issueId,
        string? projectName,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        // Check for existing app
        if (_issueApps.TryGetValue(issueId, out var existing))
        {
            if (await IsWorkerHealthyAsync(existing.WorkerUrl, cancellationToken))
            {
                _logger.LogDebug("Reusing existing Container App {AppName} for issue {IssueId}",
                    existing.ContainerAppName, issueId);
                return existing.WorkerUrl;
            }

            _logger.LogWarning("Container App {AppName} for issue {IssueId} is unhealthy",
                existing.ContainerAppName, issueId);
            _issueApps.TryRemove(issueId, out _);
        }

        // Create new Container App
        var appName = GetIssueContainerAppName(issueId);
        var workerUrl = await CreateContainerAppAsync(appName, issueId, projectName, workingDirectory, cancellationToken);

        _issueApps[issueId] = new IssueContainerApp(issueId, appName, workerUrl, DateTime.UtcNow);
        return workerUrl;
    }

    private async Task<string> CreateContainerAppAsync(
        string appName,
        string issueId,
        string? projectName,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        if (_armClient == null)
            throw new InvalidOperationException("ARM client not initialized. Configure EnvironmentId and ResourceGroupName.");

        _logger.LogInformation("Creating Container App {AppName} for issue {IssueId}", appName, issueId);

        var subscription = await _armClient.GetDefaultSubscriptionAsync(cancellationToken);
        var resourceGroup = (await subscription.GetResourceGroupAsync(_options.ResourceGroupName!, cancellationToken)).Value;
        var containerApps = resourceGroup.GetContainerApps();

        var containerAppData = new ContainerAppData(resourceGroup.Data.Location)
        {
            EnvironmentId = new Azure.Core.ResourceIdentifier(_options.EnvironmentId!),
            Configuration = new ContainerAppConfiguration
            {
                Ingress = new ContainerAppIngressConfiguration
                {
                    TargetPort = 8080,
                    External = false,
                },
            },
            Template = new ContainerAppTemplate
            {
                Containers =
                {
                    new ContainerAppContainer
                    {
                        Name = "worker",
                        Image = _options.WorkerImage,
                        Resources = new AppContainerResources
                        {
                            Cpu = 2.0,
                            Memory = "4Gi"
                        },
                        Env =
                        {
                            new ContainerAppEnvironmentVariable { Name = "ISSUE_ID", Value = issueId },
                            new ContainerAppEnvironmentVariable { Name = "PROJECT_NAME", Value = projectName ?? "default" },
                            new ContainerAppEnvironmentVariable { Name = "WORKING_DIRECTORY", Value = "/workdir" },
                        },
                    }
                },
                Scale = new ContainerAppScale
                {
                    MinReplicas = 1,
                    MaxReplicas = 1
                },
            }
        };

        // Add NFS volume mounts if storage is configured
        if (!string.IsNullOrEmpty(_options.StorageMountName))
        {
            containerAppData.Template.Volumes.Add(new ContainerAppVolume
            {
                Name = "claude-data",
                StorageType = ContainerAppStorageType.NfsAzureFile,
                StorageName = _options.StorageMountName,
            });

            // Convert working directory to NFS SubPath by stripping the data volume prefix
            var workdirSubPath = workingDirectory.TrimStart('/');

            var container = containerAppData.Template.Containers[0];
            container.VolumeMounts.Add(new ContainerAppVolumeMount
            {
                VolumeName = "claude-data",
                MountPath = "/workdir",
                SubPath = workdirSubPath
            });
        }

        // Pass auth secrets as environment variables
        var authEnvVars = new[] { "CLAUDE_CODE_OAUTH_TOKEN", "ANTHROPIC_API_KEY", "GITHUB_TOKEN",
            "GIT_AUTHOR_NAME", "GIT_AUTHOR_EMAIL", "GIT_COMMITTER_NAME", "GIT_COMMITTER_EMAIL" };
        foreach (var envVar in authEnvVars)
        {
            var value = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrEmpty(value))
            {
                containerAppData.Template.Containers[0].Env.Add(
                    new ContainerAppEnvironmentVariable { Name = envVar, Value = value });
            }
        }

        var operation = await containerApps.CreateOrUpdateAsync(WaitUntil.Completed, appName, containerAppData, cancellationToken);
        var containerApp = operation.Value;

        var fqdn = containerApp.Data.Configuration?.Ingress?.Fqdn;
        if (string.IsNullOrEmpty(fqdn))
        {
            throw new AgentStartupException($"Container App {appName} created but no FQDN available");
        }

        var workerUrl = $"https://{fqdn}";

        // Wait for the worker to be healthy
        await WaitForWorkerHealthyAsync(workerUrl, cancellationToken);

        _logger.LogInformation("Container App {AppName} is ready at {WorkerUrl}", appName, workerUrl);
        return workerUrl;
    }

    private async Task<bool> IsWorkerHealthyAsync(string workerUrl, CancellationToken cancellationToken)
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

    private async Task WaitForWorkerHealthyAsync(string workerUrl, CancellationToken cancellationToken)
    {
        var timeout = _options.ProvisioningTimeout;
        var delay = TimeSpan.FromSeconds(5);
        var elapsed = TimeSpan.Zero;

        while (elapsed < timeout)
        {
            if (await IsWorkerHealthyAsync(workerUrl, cancellationToken))
            {
                return;
            }

            await Task.Delay(delay, cancellationToken);
            elapsed += delay;
        }

        throw new AgentStartupException($"Container App at {workerUrl} did not become healthy within {timeout.TotalMinutes} minutes");
    }

    private async Task DeleteWorkerSessionAsync(string workerUrl, string workerSessionId)
    {
        try
        {
            var url = $"{workerUrl}/api/sessions/{workerSessionId}";
            using var request = new HttpRequestMessage(HttpMethod.Delete, url);
            await _httpClient.SendAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error deleting worker session {WorkerSessionId}", workerSessionId);
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
            if (eventType == "session_started")
            {
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

            if (eventType == "error")
            {
                _logger.LogWarning("Received error event from worker: {Data}", data);
                return null;
            }

            return JsonSerializer.Deserialize<SdkMessage>(data, SdkJsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse SSE event {EventType}: {Data}", eventType, data);
            return null;
        }
    }

    private static SdkMessage RemapSessionId(SdkMessage msg, string sessionId)
    {
        return msg switch
        {
            SdkAssistantMessage m => m with { SessionId = sessionId },
            SdkUserMessage m => m with { SessionId = sessionId },
            SdkResultMessage m => m with { SessionId = sessionId },
            SdkSystemMessage m => m with { SessionId = sessionId },
            SdkStreamEvent m => m with { SessionId = sessionId },
            _ => msg
        };
    }

    public async ValueTask DisposeAsync()
    {
        var deleteTasks = _sessions.Values.Select(session =>
        {
            session.Cts.Cancel();
            session.Cts.Dispose();
            return !string.IsNullOrEmpty(session.WorkerSessionId)
                ? DeleteWorkerSessionAsync(session.WorkerUrl, session.WorkerSessionId)
                : Task.CompletedTask;
        });

        await Task.WhenAll(deleteTasks);
        _sessions.Clear();
        _issueApps.Clear();
        _sessionToIssue.Clear();
        _httpClient.Dispose();
    }
}
