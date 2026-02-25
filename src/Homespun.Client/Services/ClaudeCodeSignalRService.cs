using Homespun.Shared.Hubs;
using Homespun.Shared.Models.Sessions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace Homespun.Client.Services;

/// <summary>
/// Client-side SignalR service for real-time Claude Code session communication.
/// Manages the HubConnection lifecycle and exposes events for server-to-client messages.
/// Uses AG-UI events for message streaming.
/// </summary>
public class ClaudeCodeSignalRService : IAsyncDisposable
{
    private readonly NavigationManager _navigationManager;
    private HubConnection? _hubConnection;
    private readonly HashSet<string> _joinedSessions = new();

    public ClaudeCodeSignalRService(NavigationManager navigationManager)
    {
        _navigationManager = navigationManager;
    }

    /// <summary>
    /// Whether the hub connection is currently active.
    /// </summary>
    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    /// <summary>
    /// Session IDs currently joined for receiving group messages.
    /// Tracked for automatic re-joining after reconnection.
    /// </summary>
    public IReadOnlySet<string> JoinedSessions => _joinedSessions;

    // Server-to-client events for session lifecycle management

    /// <summary>Fired when a new session starts.</summary>
    public event Action<ClaudeSession>? OnSessionStarted;

    /// <summary>Fired when a session stops.</summary>
    public event Action<string>? OnSessionStopped;

    /// <summary>Fired when the current session state is received (on join).</summary>
    public event Action<ClaudeSession>? OnSessionState;

    /// <summary>Fired when a session's status changes.</summary>
    public event Action<string, ClaudeSessionStatus, bool>? OnSessionStatusChanged;

    /// <summary>Fired when a session's mode or model changes.</summary>
    public event Action<string, SessionMode, string>? OnSessionModeModelChanged;

    /// <summary>Fired when session result (cost, duration) is received.</summary>
    public event Action<string, decimal, long>? OnSessionResultReceived;

    /// <summary>Fired when context has been cleared for a session.</summary>
    public event Action<string>? OnContextCleared;

    /// <summary>
    /// Fired when a session encounters an error.
    /// Parameters: sessionId, errorMessage, errorSubtype, isRecoverable
    /// </summary>
    public event Action<string, string, string?, bool>? OnSessionError;

    #region AG-UI Events

    /// <summary>Fired when an agent run starts (AG-UI).</summary>
    public event Action<RunStartedEvent>? OnAGUIRunStarted;

    /// <summary>Fired when an agent run finishes (AG-UI).</summary>
    public event Action<RunFinishedEvent>? OnAGUIRunFinished;

    /// <summary>Fired when an agent run encounters an error (AG-UI).</summary>
    public event Action<RunErrorEvent>? OnAGUIRunError;

    /// <summary>Fired when a text message starts streaming (AG-UI).</summary>
    public event Action<TextMessageStartEvent>? OnAGUITextMessageStart;

    /// <summary>Fired when streaming text content is received (AG-UI).</summary>
    public event Action<TextMessageContentEvent>? OnAGUITextMessageContent;

    /// <summary>Fired when a text message finishes streaming (AG-UI).</summary>
    public event Action<TextMessageEndEvent>? OnAGUITextMessageEnd;

    /// <summary>Fired when a tool call starts (AG-UI).</summary>
    public event Action<ToolCallStartEvent>? OnAGUIToolCallStart;

    /// <summary>Fired when streaming tool call arguments are received (AG-UI).</summary>
    public event Action<ToolCallArgsEvent>? OnAGUIToolCallArgs;

    /// <summary>Fired when a tool call finishes (AG-UI).</summary>
    public event Action<ToolCallEndEvent>? OnAGUIToolCallEnd;

    /// <summary>Fired when a tool call result is available (AG-UI).</summary>
    public event Action<ToolCallResultEvent>? OnAGUIToolCallResult;

    /// <summary>Fired when a state snapshot is received (AG-UI).</summary>
    public event Action<StateSnapshotEvent>? OnAGUIStateSnapshot;

    /// <summary>Fired when a state delta is received (AG-UI).</summary>
    public event Action<StateDeltaEvent>? OnAGUIStateDelta;

    /// <summary>Fired when a custom AG-UI event is received.</summary>
    public event Action<CustomEvent>? OnAGUICustomEvent;

    #endregion

    /// <summary>
    /// Establishes the SignalR connection and registers all message handlers.
    /// </summary>
    public async Task ConnectAsync()
    {
        if (_hubConnection is not null)
            return;

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(_navigationManager.ToAbsoluteUri(HubConstants.ClaudeCodeHub))
            .WithAutomaticReconnect()
            .Build();

        RegisterHandlers(_hubConnection);

        _hubConnection.Reconnected += OnReconnected;

        await _hubConnection.StartAsync();
    }

    /// <summary>
    /// Disconnects from the SignalR hub.
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (_hubConnection is not null)
        {
            _hubConnection.Reconnected -= OnReconnected;
            await _hubConnection.StopAsync();
            await _hubConnection.DisposeAsync();
            _hubConnection = null;
        }

        _joinedSessions.Clear();
    }

    // Client-to-server methods

    /// <summary>
    /// Join a session group to receive session-specific messages.
    /// </summary>
    public async Task JoinSessionAsync(string sessionId)
    {
        if (_hubConnection is null) return;
        await _hubConnection.InvokeAsync("JoinSession", sessionId);
        _joinedSessions.Add(sessionId);
    }

    /// <summary>
    /// Leave a session group.
    /// </summary>
    public async Task LeaveSessionAsync(string sessionId)
    {
        if (_hubConnection is null) return;
        await _hubConnection.InvokeAsync("LeaveSession", sessionId);
        _joinedSessions.Remove(sessionId);
    }

    /// <summary>
    /// Send a message to a session.
    /// </summary>
    public async Task SendMessageAsync(string sessionId, string message, PermissionMode permissionMode = PermissionMode.BypassPermissions)
    {
        if (_hubConnection is null) return;
        await _hubConnection.InvokeAsync("SendMessage", sessionId, message, permissionMode);
    }

    /// <summary>
    /// Stop a session.
    /// </summary>
    public async Task StopSessionAsync(string sessionId)
    {
        if (_hubConnection is null) return;
        await _hubConnection.InvokeAsync("StopSession", sessionId);
    }

    /// <summary>
    /// Interrupt a session's current execution without fully stopping it.
    /// </summary>
    public async Task InterruptSessionAsync(string sessionId)
    {
        if (_hubConnection is null) return;
        await _hubConnection.InvokeAsync("InterruptSession", sessionId);
    }

    /// <summary>
    /// Get all active sessions.
    /// </summary>
    public async Task<IReadOnlyList<ClaudeSession>> GetAllSessionsAsync()
    {
        if (_hubConnection is null) return Array.Empty<ClaudeSession>();
        return await _hubConnection.InvokeAsync<IReadOnlyList<ClaudeSession>>("GetAllSessions");
    }

    /// <summary>
    /// Get sessions for a specific project.
    /// </summary>
    public async Task<IReadOnlyList<ClaudeSession>> GetProjectSessionsAsync(string projectId)
    {
        if (_hubConnection is null) return Array.Empty<ClaudeSession>();
        return await _hubConnection.InvokeAsync<IReadOnlyList<ClaudeSession>>("GetProjectSessions", projectId);
    }

    /// <summary>
    /// Get a specific session by ID.
    /// </summary>
    public async Task<ClaudeSession?> GetSessionAsync(string sessionId)
    {
        if (_hubConnection is null) return null;
        return await _hubConnection.InvokeAsync<ClaudeSession?>("GetSession", sessionId);
    }

    /// <summary>
    /// Answer a pending question in a session.
    /// </summary>
    public async Task AnswerQuestionAsync(string sessionId, string answersJson)
    {
        if (_hubConnection is null) return;
        await _hubConnection.InvokeAsync("AnswerQuestion", sessionId, answersJson);
    }

    /// <summary>
    /// Execute a plan by optionally clearing context and sending it as a message.
    /// </summary>
    public async Task ExecutePlanAsync(string sessionId, bool clearContext = true)
    {
        if (_hubConnection is null) return;
        await _hubConnection.InvokeAsync("ExecutePlan", sessionId, clearContext);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }

    private void RegisterHandlers(HubConnection connection)
    {
        // Session lifecycle events
        connection.On<ClaudeSession>("SessionStarted",
            session => OnSessionStarted?.Invoke(session));

        connection.On<string>("SessionStopped",
            sessionId => OnSessionStopped?.Invoke(sessionId));

        connection.On<ClaudeSession>("SessionState",
            session => OnSessionState?.Invoke(session));

connection.On<string, ClaudeSessionStatus, bool>("SessionStatusChanged",
            (sessionId, status, hasPendingPlanApproval) => OnSessionStatusChanged?.Invoke(sessionId, status, hasPendingPlanApproval));

        connection.On<string, SessionMode, string>("SessionModeModelChanged",
            (sessionId, mode, model) => OnSessionModeModelChanged?.Invoke(sessionId, mode, model));

        connection.On<string, decimal, long>("SessionResultReceived",
            (sessionId, totalCostUsd, durationMs) =>
                OnSessionResultReceived?.Invoke(sessionId, totalCostUsd, durationMs));

        connection.On<string>("ContextCleared",
            sessionId => OnContextCleared?.Invoke(sessionId));

        connection.On<string, string, string?, bool>("SessionError",
            (sessionId, errorMessage, errorSubtype, isRecoverable) =>
                OnSessionError?.Invoke(sessionId, errorMessage, errorSubtype, isRecoverable));

        // Register AG-UI event handlers for message streaming
        connection.On<RunStartedEvent>(AGUIEventType.RunStarted,
            evt => OnAGUIRunStarted?.Invoke(evt));

        connection.On<RunFinishedEvent>(AGUIEventType.RunFinished,
            evt => OnAGUIRunFinished?.Invoke(evt));

        connection.On<RunErrorEvent>(AGUIEventType.RunError,
            evt => OnAGUIRunError?.Invoke(evt));

        connection.On<TextMessageStartEvent>(AGUIEventType.TextMessageStart,
            evt => OnAGUITextMessageStart?.Invoke(evt));

        connection.On<TextMessageContentEvent>(AGUIEventType.TextMessageContent,
            evt => OnAGUITextMessageContent?.Invoke(evt));

        connection.On<TextMessageEndEvent>(AGUIEventType.TextMessageEnd,
            evt => OnAGUITextMessageEnd?.Invoke(evt));

        connection.On<ToolCallStartEvent>(AGUIEventType.ToolCallStart,
            evt => OnAGUIToolCallStart?.Invoke(evt));

        connection.On<ToolCallArgsEvent>(AGUIEventType.ToolCallArgs,
            evt => OnAGUIToolCallArgs?.Invoke(evt));

        connection.On<ToolCallEndEvent>(AGUIEventType.ToolCallEnd,
            evt => OnAGUIToolCallEnd?.Invoke(evt));

        connection.On<ToolCallResultEvent>(AGUIEventType.ToolCallResult,
            evt => OnAGUIToolCallResult?.Invoke(evt));

        connection.On<StateSnapshotEvent>(AGUIEventType.StateSnapshot,
            evt => OnAGUIStateSnapshot?.Invoke(evt));

        connection.On<StateDeltaEvent>(AGUIEventType.StateDelta,
            evt => OnAGUIStateDelta?.Invoke(evt));

        connection.On<CustomEvent>(AGUIEventType.Custom,
            evt => OnAGUICustomEvent?.Invoke(evt));
    }

    private async Task OnReconnected(string? connectionId)
    {
        // Re-join all tracked session groups after reconnection
        foreach (var sessionId in _joinedSessions)
        {
            if (_hubConnection is not null)
            {
                await _hubConnection.InvokeAsync("JoinSession", sessionId);
            }
        }
    }
}
