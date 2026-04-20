using Microsoft.Extensions.Hosting;

namespace Homespun.Features.Gitgraph.Snapshots;

/// <summary>
/// Marker interface for the background refresher so DI can distinguish the
/// hosted service from the generic <see cref="IHostedService"/> collection
/// when tests need to trigger a tick directly.
/// </summary>
public interface ITaskGraphSnapshotRefresher : IHostedService
{
    /// <summary>
    /// Run a single refresh pass over the currently-tracked keys. Test-only
    /// hook — the hosted service loop calls the same internal method.
    /// </summary>
    Task RefreshOnceAsync(CancellationToken ct);
}
