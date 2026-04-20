using System.Collections.Concurrent;
using Homespun.Shared.Models.Fleece;

namespace Homespun.Features.Gitgraph.Snapshots;

/// <summary>
/// In-memory <see cref="IProjectTaskGraphSnapshotStore"/>. Keyed by
/// <c>(projectId, maxPastPRs)</c>. All operations are thread-safe.
/// </summary>
public sealed class ProjectTaskGraphSnapshotStore(TimeProvider timeProvider) : IProjectTaskGraphSnapshotStore
{
    private readonly ConcurrentDictionary<SnapshotKey, TaskGraphSnapshotEntry> _entries = new();

    public TaskGraphSnapshotEntry? TryGet(string projectId, int maxPastPRs)
    {
        if (_entries.TryGetValue(new SnapshotKey(projectId, maxPastPRs), out var entry))
        {
            entry.LastAccessedAt = timeProvider.GetUtcNow();
            return entry;
        }
        return null;
    }

    public void Store(string projectId, int maxPastPRs, TaskGraphResponse response, DateTimeOffset builtAt)
    {
        var now = timeProvider.GetUtcNow();
        _entries[new SnapshotKey(projectId, maxPastPRs)] = new TaskGraphSnapshotEntry
        {
            Response = response,
            LastBuiltAt = builtAt,
            LastAccessedAt = now,
        };
    }

    public void InvalidateProject(string projectId)
    {
        foreach (var key in _entries.Keys)
        {
            if (string.Equals(key.ProjectId, projectId, StringComparison.Ordinal))
            {
                _entries.TryRemove(key, out _);
            }
        }
    }

    public IReadOnlyCollection<(string ProjectId, int MaxPastPRs)> GetTrackedKeys()
        => _entries.Keys.Select(k => (k.ProjectId, k.MaxPastPRs)).ToArray();

    public int EvictIdle(DateTimeOffset idleCutoff)
    {
        var evicted = 0;
        foreach (var kvp in _entries)
        {
            if (kvp.Value.LastAccessedAt < idleCutoff
                && _entries.TryRemove(kvp.Key, out _))
            {
                evicted++;
            }
        }
        return evicted;
    }

    private readonly record struct SnapshotKey(string ProjectId, int MaxPastPRs);
}
