using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Homespun.Features.ClaudeCode.Data;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Shared.Models.Sessions;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Minimal mock of <see cref="IAgentExecutionService"/> for offline/demo/mock-mode.
///
/// <para>
/// Since the pipeline was migrated to A2A-native messaging, the server only consumes
/// control-plane <see cref="SdkMessage"/> variants (<c>SdkSystemMessage</c>,
/// <c>SdkResultMessage</c>, <c>SdkQuestionPendingMessage</c>, <c>SdkPlanPendingMessage</c>)
/// from the worker — everything content-bearing flows through
/// <c>SessionEventIngestor</c> as A2A/AG-UI envelopes. This mock emits the control-plane
/// primitives the message loop in <c>MessageProcessingService</c> requires AND ingests a
/// canned A2A stream (text + tool_use/tool_result + completed status-update) so demo/E2E
/// clients render realistic session content without a worker.
/// </para>
/// </summary>
public class MockAgentExecutionService : IAgentExecutionService
{
    private readonly ILogger<MockAgentExecutionService> _logger;
    private readonly IClaudeSessionStore _homespunStore;
    private readonly ISessionEventIngestor _eventIngestor;
    private readonly ConcurrentDictionary<string, MockSession> _sessions = new();

    private record MockSession(
        string SessionId,
        string WorkingDirectory,
        SessionMode Mode,
        string Model,
        DateTime CreatedAt)
    {
        public string? ConversationId { get; set; }
        public DateTime LastActivityAt { get; set; } = CreatedAt;
        public string? HomespunSessionId { get; set; }
        public string? HomespunProjectId { get; set; }
        public string LastUserMessage { get; set; } = string.Empty;
    }

    public MockAgentExecutionService(
        ILogger<MockAgentExecutionService> logger,
        IClaudeSessionStore homespunStore,
        ISessionEventIngestor eventIngestor)
    {
        _logger = logger;
        _homespunStore = homespunStore;
        _eventIngestor = eventIngestor;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SdkMessage> StartSessionAsync(
        AgentStartRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var sessionId = Guid.NewGuid().ToString();
        _logger.LogInformation("[Mock] Starting session {SessionId} in directory {WorkingDirectory}",
            sessionId, request.WorkingDirectory);

        var homespunSessionId = request.HomespunSessionId;
        var homespunProjectId = request.ProjectId;
        if (string.IsNullOrEmpty(homespunProjectId) && !string.IsNullOrEmpty(homespunSessionId))
        {
            homespunProjectId = _homespunStore.GetById(homespunSessionId)?.ProjectId;
        }

        var mockSession = new MockSession(
            sessionId,
            request.WorkingDirectory,
            request.Mode,
            request.Model,
            DateTime.UtcNow)
        {
            HomespunSessionId = homespunSessionId,
            HomespunProjectId = homespunProjectId,
            LastUserMessage = request.Prompt ?? string.Empty,
        };
        _sessions[sessionId] = mockSession;

        yield return new SdkSystemMessage(sessionId, null, "session_started", request.Model, null);

        await EmitMockA2AEventsAsync(mockSession, mockSession.LastUserMessage, cancellationToken);

        yield return new SdkResultMessage(
            SessionId: sessionId,
            Uuid: null,
            Subtype: "success",
            DurationMs: 50,
            DurationApiMs: 0,
            IsError: false,
            NumTurns: 1,
            TotalCostUsd: 0m,
            Result: null);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SdkMessage> SendMessageAsync(
        AgentMessageRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] SendMessage to session {SessionId}", request.SessionId);

        if (_sessions.TryGetValue(request.SessionId, out var session))
        {
            session.LastActivityAt = DateTime.UtcNow;
            session.LastUserMessage = request.Message ?? string.Empty;
            await EmitMockA2AEventsAsync(session, session.LastUserMessage, cancellationToken);
        }

        yield return new SdkResultMessage(
            SessionId: request.SessionId,
            Uuid: null,
            Subtype: "success",
            DurationMs: 50,
            DurationApiMs: 0,
            IsError: false,
            NumTurns: 1,
            TotalCostUsd: 0m,
            Result: null);
    }

    private async Task EmitMockA2AEventsAsync(MockSession session, string userMessage, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(session.HomespunSessionId) || string.IsNullOrEmpty(session.HomespunProjectId))
        {
            // No homespun session context — skip ingestion (e.g., startup-probe calls without an issue).
            return;
        }

        var projectId = session.HomespunProjectId;
        var sessionId = session.HomespunSessionId;
        var taskId = Guid.NewGuid().ToString();

        await IngestAsync(projectId, sessionId, HomespunA2AEventKind.Task, $$"""
        { "kind": "task", "id": "{{taskId}}", "contextId": "{{sessionId}}", "status": { "state": "working", "timestamp": "{{DateTime.UtcNow:O}}" } }
        """, ct);

        // User-role echo is synthesized centrally in MessageProcessingService.SendMessageAsync
        // so every executor (Docker/SingleContainer/Mock) shares the same path.

        await IngestAsync(projectId, sessionId, HomespunA2AEventKind.Message, $$"""
        {
          "kind": "message",
          "messageId": "{{Guid.NewGuid()}}",
          "role": "agent",
          "parts": [ { "kind": "text", "text": "I'll help with that." } ],
          "contextId": "{{sessionId}}",
          "taskId": "{{taskId}}",
          "metadata": { "sdkMessageType": "assistant" }
        }
        """, ct);

        var wantsTool = userMessage.Contains("tool", StringComparison.OrdinalIgnoreCase)
                        || userMessage.Contains("read", StringComparison.OrdinalIgnoreCase);
        if (wantsTool)
        {
            var toolUseId = Guid.NewGuid().ToString();
            await IngestAsync(projectId, sessionId, HomespunA2AEventKind.Message, $$"""
            {
              "kind": "message",
              "messageId": "{{Guid.NewGuid()}}",
              "role": "agent",
              "parts": [ {
                "kind": "data",
                "data": { "toolUseId": "{{toolUseId}}", "toolName": "Read", "input": { "file_path": "mock.txt" } },
                "metadata": { "kind": "tool_use" }
              } ],
              "contextId": "{{sessionId}}",
              "taskId": "{{taskId}}",
              "metadata": { "sdkMessageType": "assistant" }
            }
            """, ct);

            await IngestAsync(projectId, sessionId, HomespunA2AEventKind.Message, $$"""
            {
              "kind": "message",
              "messageId": "{{Guid.NewGuid()}}",
              "role": "user",
              "parts": [ {
                "kind": "data",
                "data": { "toolUseId": "{{toolUseId}}", "content": "mock file contents" },
                "metadata": { "kind": "tool_result" }
              } ],
              "contextId": "{{sessionId}}",
              "metadata": { "sdkMessageType": "user" }
            }
            """, ct);

            await IngestAsync(projectId, sessionId, HomespunA2AEventKind.Message, $$"""
            {
              "kind": "message",
              "messageId": "{{Guid.NewGuid()}}",
              "role": "agent",
              "parts": [ { "kind": "text", "text": "Here is what I found." } ],
              "contextId": "{{sessionId}}",
              "taskId": "{{taskId}}",
              "metadata": { "sdkMessageType": "assistant" }
            }
            """, ct);
        }

        await IngestAsync(projectId, sessionId, HomespunA2AEventKind.StatusUpdate, $$"""
        {
          "kind": "status-update",
          "taskId": "{{taskId}}",
          "contextId": "{{sessionId}}",
          "status": { "state": "completed", "timestamp": "{{DateTime.UtcNow:O}}" },
          "final": true
        }
        """, ct);
    }

    private async Task IngestAsync(string projectId, string sessionId, string kind, string json, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(json);
        var payload = doc.RootElement.Clone();
        await _eventIngestor.IngestAsync(projectId, sessionId, kind, payload, ct);
    }

    /// <inheritdoc />
    public Task StopSessionAsync(string sessionId, bool forceStopContainer = false, CancellationToken cancellationToken = default)
    {
        _sessions.TryRemove(sessionId, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task InterruptSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<bool> AnswerQuestionAsync(string sessionId, Dictionary<string, string> answers,
        CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    /// <inheritdoc />
    public Task<bool> ApprovePlanAsync(string sessionId, bool approved, bool keepContext, string? feedback = null,
        CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    /// <inheritdoc />
    public Task<bool> SetSessionModeAsync(string sessionId, SessionMode mode, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    /// <inheritdoc />
    public Task<bool> SetSessionModelAsync(string sessionId, string model, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    /// <inheritdoc />
    public Task<CloneContainerState?> GetCloneContainerStateAsync(
        string workingDirectory,
        CancellationToken cancellationToken = default)
        => Task.FromResult<CloneContainerState?>(null);

    /// <inheritdoc />
    public Task TerminateCloneSessionAsync(string workingDirectory, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <inheritdoc />
    public Task<IReadOnlyList<ContainerInfo>> ListContainersAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ContainerInfo>>(Array.Empty<ContainerInfo>());

    /// <inheritdoc />
    public Task<bool> StopContainerByIdAsync(string containerId, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    /// <inheritdoc />
    public Task<ContainerRestartResult?> RestartContainerAsync(string sessionId, CancellationToken cancellationToken = default)
        => Task.FromResult<ContainerRestartResult?>(null);

    /// <inheritdoc />
    public Task<AgentSessionStatus?> GetSessionStatusAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            return Task.FromResult<AgentSessionStatus?>(new AgentSessionStatus(
                session.SessionId,
                session.WorkingDirectory,
                session.Mode,
                session.Model,
                session.ConversationId,
                session.CreatedAt,
                session.LastActivityAt));
        }

        return Task.FromResult<AgentSessionStatus?>(null);
    }

    /// <inheritdoc />
    public Task<string?> ReadFileFromAgentAsync(string sessionId, string filePath, CancellationToken cancellationToken = default)
        => Task.FromResult<string?>(null);

    /// <inheritdoc />
    public Task<IReadOnlyList<AgentSessionStatus>> ListSessionsAsync(CancellationToken cancellationToken = default)
    {
        var statuses = _sessions.Values.Select(s => new AgentSessionStatus(
            s.SessionId,
            s.WorkingDirectory,
            s.Mode,
            s.Model,
            s.ConversationId,
            s.CreatedAt,
            s.LastActivityAt)).ToList().AsReadOnly();
        return Task.FromResult<IReadOnlyList<AgentSessionStatus>>(statuses);
    }

    /// <inheritdoc />
    public Task<int> CleanupOrphanedContainersAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(0);
}
