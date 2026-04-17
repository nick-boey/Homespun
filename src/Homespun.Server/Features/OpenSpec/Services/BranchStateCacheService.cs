using System.Collections.Concurrent;
using Homespun.Shared.Models.OpenSpec;

namespace Homespun.Features.OpenSpec.Services;

/// <summary>
/// Thread-safe, TTL-bounded in-memory cache of branch snapshots.
/// </summary>
public class BranchStateCacheService(TimeProvider timeProvider) : IBranchStateCacheService
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new();

    /// <inheritdoc />
    public TimeSpan Ttl { get; } = DefaultTtl;

    /// <inheritdoc />
    public void Put(BranchStateSnapshot snapshot)
    {
        var key = Key(snapshot.ProjectId, snapshot.Branch);
        _entries[key] = new CacheEntry(snapshot, timeProvider.GetUtcNow());
    }

    /// <inheritdoc />
    public BranchStateSnapshot? TryGet(string projectId, string branch)
    {
        var key = Key(projectId, branch);
        if (!_entries.TryGetValue(key, out var entry))
        {
            return null;
        }

        if (timeProvider.GetUtcNow() - entry.StoredAt > Ttl)
        {
            _entries.TryRemove(key, out _);
            return null;
        }

        return entry.Snapshot;
    }

    /// <inheritdoc />
    public void Invalidate(string projectId, string branch)
    {
        _entries.TryRemove(Key(projectId, branch), out _);
    }

    private static string Key(string projectId, string branch) => $"{projectId}\0{branch}";

    private sealed record CacheEntry(BranchStateSnapshot Snapshot, DateTimeOffset StoredAt);
}
