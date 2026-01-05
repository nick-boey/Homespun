using Homespun.Features.Notifications;
using Homespun.Features.PullRequests.Data;
using Homespun.Features.Roadmap.Sync;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Homespun.Features.Roadmap;

/// <summary>
/// Background service that polls for roadmap changes in the main branch.
/// </summary>
public class RoadmapPollingService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly RoadmapPollingOptions _options;
    private readonly ILogger<RoadmapPollingService> _logger;

    public RoadmapPollingService(
        IServiceScopeFactory scopeFactory,
        IHubContext<NotificationHub> hubContext,
        IOptions<RoadmapPollingOptions> options,
        ILogger<RoadmapPollingService> logger)
    {
        _scopeFactory = scopeFactory;
        _hubContext = hubContext;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Roadmap polling service started with {IntervalSeconds}s interval",
            _options.PollingIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollRoadmapsAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error during roadmap polling");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.PollingIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("Roadmap polling service stopped");
    }

    private async Task PollRoadmapsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dataStore = scope.ServiceProvider.GetRequiredService<IDataStore>();
        var syncService = scope.ServiceProvider.GetRequiredService<IRoadmapSyncService>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var projects = dataStore.Projects;

        foreach (var project in projects)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                await PollProjectRoadmapAsync(project.Id, syncService, notificationService, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling roadmap for project {ProjectId}", project.Id);
            }
        }
    }

    private async Task PollProjectRoadmapAsync(
        string projectId,
        IRoadmapSyncService syncService,
        INotificationService notificationService,
        CancellationToken ct)
    {
        // Compare local roadmap with main branch
        var diff = await syncService.CompareWithMainAsync(projectId);

        if (diff.HasChanges)
        {
            var deduplicationKey = $"roadmap-changes-{projectId}";

            // Only add notification if one doesn't already exist
            if (!notificationService.HasNotificationWithKey(deduplicationKey))
            {
                var notification = new Notification
                {
                    Type = NotificationType.Info,
                    Title = "Roadmap Changes Pending",
                    Message = $"{diff.AddedChanges.Count + diff.ModifiedChanges.Count} changes ready to sync to main branch",
                    ProjectId = projectId,
                    ActionLabel = "Create PR",
                    DeduplicationKey = deduplicationKey,
                    Action = async () =>
                    {
                        // This will be handled by the UI
                        await Task.CompletedTask;
                    }
                };

                notificationService.AddNotification(notification);
                await _hubContext.BroadcastNotificationAdded(notification);

                _logger.LogInformation(
                    "Roadmap changes detected for project {ProjectId}: {AddedCount} added, {ModifiedCount} modified, {RemovedCount} removed",
                    projectId, diff.AddedChanges.Count, diff.ModifiedChanges.Count, diff.RemovedChanges.Count);
            }
        }
    }

    /// <summary>
    /// Trigger an immediate check for a specific project.
    /// Called after pulling main branch.
    /// </summary>
    public async Task CheckProjectRoadmapAsync(string projectId)
    {
        using var scope = _scopeFactory.CreateScope();
        var syncService = scope.ServiceProvider.GetRequiredService<IRoadmapSyncService>();
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();

        try
        {
            await PollProjectRoadmapAsync(projectId, syncService, notificationService, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking roadmap for project {ProjectId}", projectId);
        }
    }
}

/// <summary>
/// Configuration options for roadmap polling.
/// </summary>
public class RoadmapPollingOptions
{
    public const string SectionName = "RoadmapPolling";

    /// <summary>
    /// Interval between polls in seconds. Default: 60.
    /// </summary>
    public int PollingIntervalSeconds { get; set; } = 60;
}
