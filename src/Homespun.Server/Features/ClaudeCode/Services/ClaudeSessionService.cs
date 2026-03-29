using System.Text.Json;
using Homespun.Shared.Models.Sessions;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Tracks content blocks being assembled from stream events during a single message turn.
/// </summary>
internal class ContentBlockAssembler
{
    private readonly List<ContentBlockState> _blocks = new();
    private readonly Dictionary<string, string> _toolUseNames = new();

    public IReadOnlyList<ContentBlockState> Blocks => _blocks;

    public void StartBlock(int index, JsonElement contentBlock)
    {
        var type = contentBlock.TryGetProperty("type", out var t) ? t.GetString() : null;
        var state = new ContentBlockState { Index = index, Type = type };

        if (type == "tool_use")
        {
            state.ToolUseId = contentBlock.TryGetProperty("id", out var id) ? id.GetString() : null;
            state.ToolName = contentBlock.TryGetProperty("name", out var name) ? name.GetString() : null;
            if (state.ToolUseId != null && state.ToolName != null)
            {
                _toolUseNames[state.ToolUseId] = state.ToolName;
            }
        }

        // Ensure the list is large enough
        while (_blocks.Count <= index) _blocks.Add(new ContentBlockState());
        _blocks[index] = state;
    }

    public void ApplyDelta(int index, JsonElement delta)
    {
        if (index < 0 || index >= _blocks.Count) return;
        var block = _blocks[index];
        var deltaType = delta.TryGetProperty("type", out var t) ? t.GetString() : null;

        switch (deltaType)
        {
            case "text_delta":
                block.Text += delta.TryGetProperty("text", out var text) ? text.GetString() : null;
                break;
            case "thinking_delta":
                block.Thinking += delta.TryGetProperty("thinking", out var thinking) ? thinking.GetString() : null;
                break;
            case "input_json_delta":
                block.PartialJson += delta.TryGetProperty("partial_json", out var pj) ? pj.GetString() : null;
                break;
        }
    }

    public void StopBlock(int index)
    {
        if (index < 0 || index >= _blocks.Count) return;
        _blocks[index].IsComplete = true;
    }

    public string? GetToolName(string toolUseId) =>
        _toolUseNames.TryGetValue(toolUseId, out var name) ? name : null;

    public void Clear()
    {
        _blocks.Clear();
    }
}

internal class ContentBlockState
{
    public int Index { get; set; }
    public string? Type { get; set; }
    public string? Text { get; set; }
    public string? Thinking { get; set; }
    public string? ToolUseId { get; set; }
    public string? ToolName { get; set; }
    public string? PartialJson { get; set; }
    public bool IsComplete { get; set; }
}

/// <summary>
/// Thin facade over the decomposed session services.
/// Implements IClaudeSessionService by delegating to SessionLifecycleService,
/// MessageProcessingService, ToolInteractionService, and IClaudeSessionStore.
/// </summary>
public class ClaudeSessionService : IClaudeSessionService, IAsyncDisposable
{
    private readonly ISessionLifecycleService _lifecycle;
    private readonly IMessageProcessingService _messaging;
    private readonly IToolInteractionService _toolInteraction;
    private readonly IClaudeSessionStore _sessionStore;

    public ClaudeSessionService(
        ISessionLifecycleService lifecycle,
        IMessageProcessingService messaging,
        IToolInteractionService toolInteraction,
        IClaudeSessionStore sessionStore)
    {
        _lifecycle = lifecycle;
        _messaging = messaging;
        _toolInteraction = toolInteraction;
        _sessionStore = sessionStore;
    }

    // --- Lifecycle ---

    public Task<ClaudeSession> StartSessionAsync(
        string entityId, string projectId, string workingDirectory,
        SessionMode mode, string model, string? systemPrompt = null,
        CancellationToken cancellationToken = default)
        => _lifecycle.StartSessionAsync(entityId, projectId, workingDirectory, mode, model, systemPrompt, cancellationToken);

    public Task<ClaudeSession> ResumeSessionAsync(
        string sessionId, string entityId, string projectId, string workingDirectory,
        CancellationToken cancellationToken = default)
        => _lifecycle.ResumeSessionAsync(sessionId, entityId, projectId, workingDirectory, cancellationToken);

    public Task<IReadOnlyList<ResumableSession>> GetResumableSessionsAsync(
        string entityId, string workingDirectory, CancellationToken cancellationToken = default)
        => _lifecycle.GetResumableSessionsAsync(entityId, workingDirectory, cancellationToken);

    public Task<ClaudeSession> StartSessionWithTerminationAsync(
        string entityId, string projectId, string workingDirectory,
        SessionMode mode, string model, bool terminateExisting,
        string? systemPrompt = null, CancellationToken cancellationToken = default)
        => _lifecycle.StartSessionWithTerminationAsync(entityId, projectId, workingDirectory, mode, model, terminateExisting, systemPrompt, cancellationToken);

    public Task<ClaudeSession?> RestartSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        => _lifecycle.RestartSessionAsync(sessionId, cancellationToken);

    public Task StopSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        => _lifecycle.StopSessionAsync(sessionId, cancellationToken);

    public Task<int> StopAllSessionsForEntityAsync(string entityId, CancellationToken cancellationToken = default)
        => _lifecycle.StopAllSessionsForEntityAsync(entityId, cancellationToken);

    public Task InterruptSessionAsync(string sessionId, CancellationToken cancellationToken = default)
        => _lifecycle.InterruptSessionAsync(sessionId, cancellationToken);


    public Task ClearContextAsync(string sessionId, CancellationToken cancellationToken = default)
        => _lifecycle.ClearContextAsync(sessionId, cancellationToken);

    public Task<ClaudeSession> ClearContextAndStartNewAsync(
        string currentSessionId, string? initialPrompt = null, CancellationToken cancellationToken = default)
        => _lifecycle.ClearContextAndStartNewAsync(currentSessionId, initialPrompt, cancellationToken);

    public Task SetSessionModeAsync(string sessionId, SessionMode mode, CancellationToken cancellationToken = default)
        => _lifecycle.SetSessionModeAsync(sessionId, mode, cancellationToken);

    public Task SetSessionModelAsync(string sessionId, string model, CancellationToken cancellationToken = default)
        => _lifecycle.SetSessionModelAsync(sessionId, model, cancellationToken);

    public Task<AgentStartCheckResult> CheckCloneStateAsync(
        string workingDirectory, CancellationToken cancellationToken = default)
        => _lifecycle.CheckCloneStateAsync(workingDirectory, cancellationToken);

    public Task<string> AcceptIssueChangesAsync(string sessionId, CancellationToken cancellationToken = default)
        => _lifecycle.AcceptIssueChangesAsync(sessionId, cancellationToken);

    public Task<string> CancelIssueChangesAsync(string sessionId, CancellationToken cancellationToken = default)
        => _lifecycle.CancelIssueChangesAsync(sessionId, cancellationToken);

    public Task<IReadOnlyList<ClaudeMessage>> GetCachedMessagesAsync(
        string sessionId, CancellationToken cancellationToken = default)
        => _lifecycle.GetCachedMessagesAsync(sessionId, cancellationToken);

    public Task<IReadOnlyList<SessionCacheSummary>> GetSessionHistoryAsync(
        string projectId, string entityId, CancellationToken cancellationToken = default)
        => _lifecycle.GetSessionHistoryAsync(projectId, entityId, cancellationToken);

    // --- Messaging ---

    public Task SendMessageAsync(string sessionId, string message, CancellationToken cancellationToken = default)
        => _messaging.SendMessageAsync(sessionId, message, cancellationToken);

    public Task SendMessageAsync(string sessionId, string message, SessionMode mode, CancellationToken cancellationToken = default)
        => _messaging.SendMessageAsync(sessionId, message, mode, cancellationToken);

    public Task SendMessageAsync(string sessionId, string message, SessionMode mode, string? model, CancellationToken cancellationToken = default)
        => _messaging.SendMessageAsync(sessionId, message, mode, model, cancellationToken);

    // --- Tool Interaction ---

    public Task AnswerQuestionAsync(string sessionId, Dictionary<string, string> answers, CancellationToken cancellationToken = default)
        => _toolInteraction.AnswerQuestionAsync(sessionId, answers, cancellationToken);

    public Task ExecutePlanAsync(string sessionId, bool clearContext = true, CancellationToken cancellationToken = default)
        => _toolInteraction.ExecutePlanAsync(sessionId, clearContext, cancellationToken);

    public Task ApprovePlanAsync(string sessionId, bool approved, bool keepContext, string? feedback = null,
        CancellationToken cancellationToken = default)
        => _toolInteraction.ApprovePlanAsync(sessionId, approved, keepContext, feedback, cancellationToken);

    // --- Session Store queries (direct delegation) ---

    public ClaudeSession? GetSession(string sessionId) => _sessionStore.GetById(sessionId);

    public ClaudeSession? GetSessionByEntityId(string entityId)
        => _sessionStore.GetAll().FirstOrDefault(s => s.EntityId == entityId);

    public IReadOnlyList<ClaudeSession> GetSessionsForProject(string projectId)
        => _sessionStore.GetAll().Where(s => s.ProjectId == projectId).ToList();

    public IReadOnlyList<ClaudeSession> GetAllSessions() => _sessionStore.GetAll().ToList();

    // --- Dispose ---

    public async ValueTask DisposeAsync()
    {
        await _lifecycle.DisposeAsync();
    }
}
