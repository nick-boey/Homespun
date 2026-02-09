using Homespun.Shared.Hubs;
using Homespun.Shared.Models.Notifications;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace Homespun.Client.Services;

/// <summary>
/// Client-side SignalR service for real-time notification delivery.
/// Manages the HubConnection lifecycle and exposes events for notification messages.
/// </summary>
public class NotificationSignalRService : IAsyncDisposable
{
    private readonly NavigationManager _navigationManager;
    private HubConnection? _hubConnection;
    private readonly HashSet<string> _joinedProjectGroups = new();

    public NotificationSignalRService(NavigationManager navigationManager)
    {
        _navigationManager = navigationManager;
    }

    /// <summary>
    /// Whether the hub connection is currently active.
    /// </summary>
    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    // Server-to-client events

    /// <summary>Fired when a new notification is received.</summary>
    public event Action<NotificationDto>? OnNotificationReceived;

    /// <summary>Fired when a notification is dismissed.</summary>
    public event Action<string>? OnNotificationDismissed;

    /// <summary>
    /// Establishes the SignalR connection and registers all message handlers.
    /// </summary>
    public async Task ConnectAsync()
    {
        if (_hubConnection is not null)
            return;

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(_navigationManager.ToAbsoluteUri(HubConstants.NotificationHub))
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.On<NotificationDto>("NotificationAdded",
            notification => OnNotificationReceived?.Invoke(notification));

        _hubConnection.On<string>("NotificationDismissed",
            notificationId => OnNotificationDismissed?.Invoke(notificationId));

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

        _joinedProjectGroups.Clear();
    }

    // Client-to-server methods

    /// <summary>
    /// Join a project group to receive project-specific notifications.
    /// </summary>
    public async Task JoinProjectGroupAsync(string projectId)
    {
        if (_hubConnection is null) return;
        await _hubConnection.InvokeAsync("JoinProjectGroup", projectId);
        _joinedProjectGroups.Add(projectId);
    }

    /// <summary>
    /// Leave a project group.
    /// </summary>
    public async Task LeaveProjectGroupAsync(string projectId)
    {
        if (_hubConnection is null) return;
        await _hubConnection.InvokeAsync("LeaveProjectGroup", projectId);
        _joinedProjectGroups.Remove(projectId);
    }

    /// <summary>
    /// Get all active notifications, optionally filtered by project.
    /// </summary>
    public async Task<IReadOnlyList<NotificationDto>> GetActiveNotificationsAsync(string? projectId = null)
    {
        if (_hubConnection is null) return Array.Empty<NotificationDto>();
        return await _hubConnection.InvokeAsync<IReadOnlyList<NotificationDto>>(
            "GetActiveNotifications", projectId);
    }

    /// <summary>
    /// Dismiss a notification by ID.
    /// </summary>
    public async Task DismissNotificationAsync(string notificationId)
    {
        if (_hubConnection is null) return;
        await _hubConnection.InvokeAsync("DismissNotification", notificationId);
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }

    private async Task OnReconnected(string? connectionId)
    {
        // Re-join all tracked project groups after reconnection
        foreach (var projectId in _joinedProjectGroups)
        {
            if (_hubConnection is not null)
            {
                await _hubConnection.InvokeAsync("JoinProjectGroup", projectId);
            }
        }
    }
}
