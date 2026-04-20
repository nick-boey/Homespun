using System.Collections.Concurrent;
using Homespun.Features.Gitgraph.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Homespun.Features.Gitgraph.Snapshots;

/// <summary>
/// Hosted service that iterates <see cref="IProjectTaskGraphSnapshotStore.GetTrackedKeys"/>
/// on a configurable interval and rebuilds the snapshot via
/// <see cref="IGraphService.BuildEnhancedTaskGraphAsync"/>. Refreshes per project
/// are serialised through a <see cref="SemaphoreSlim"/> so concurrent invocations
/// coalesce instead of stacking.
/// </summary>
public sealed class TaskGraphSnapshotRefresher(
    IServiceProvider services,
    IProjectTaskGraphSnapshotStore store,
    IOptions<TaskGraphSnapshotOptions> options,
    TimeProvider timeProvider,
    ILogger<TaskGraphSnapshotRefresher> logger) : ITaskGraphSnapshotRefresher
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _projectLocks = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("TaskGraphSnapshot:Enabled=false — refresher not starting");
            return Task.CompletedTask;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loop = Task.Run(() => RunAsync(_cts.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _cts?.Cancel();
        if (_loop is not null)
        {
            try
            {
                await _loop.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException) { }
        }
    }

    public Task RefreshOnceAsync(CancellationToken ct) => RefreshTrackedKeysAsync(ct);

    private async Task RunAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(1, options.Value.RefreshIntervalSeconds));
        var idleWindow = TimeSpan.FromMinutes(Math.Max(1, options.Value.IdleEvictionMinutes));

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RefreshTrackedKeysAsync(ct);
                store.EvictIdle(timeProvider.GetUtcNow() - idleWindow);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "TaskGraphSnapshotRefresher iteration failed");
            }

            try
            {
                await Task.Delay(interval, timeProvider, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task RefreshTrackedKeysAsync(CancellationToken ct)
    {
        foreach (var key in store.GetTrackedKeys())
        {
            ct.ThrowIfCancellationRequested();

            var gate = _projectLocks.GetOrAdd(key.ProjectId, _ => new SemaphoreSlim(1, 1));
            if (!await gate.WaitAsync(0, ct))
            {
                // Another refresh for this project is already in-flight; let it
                // do the work. Coalesces spikes when invalidation + the tick
                // fire in rapid succession.
                continue;
            }

            try
            {
                using var scope = services.CreateScope();
                var graphService = scope.ServiceProvider.GetRequiredService<IGraphService>();
                var response = await graphService.BuildEnhancedTaskGraphAsync(key.ProjectId, key.MaxPastPRs);
                if (response is not null)
                {
                    store.Store(key.ProjectId, key.MaxPastPRs, response, timeProvider.GetUtcNow());
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Snapshot refresh failed for project {ProjectId} maxPastPRs={Max}",
                    key.ProjectId, key.MaxPastPRs);
            }
            finally
            {
                gate.Release();
            }
        }
    }
}
