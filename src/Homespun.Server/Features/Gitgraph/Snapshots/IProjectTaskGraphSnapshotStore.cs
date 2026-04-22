using Homespun.Shared.Models.Fleece;

namespace Homespun.Features.Gitgraph.Snapshots;

/// <summary>
/// In-memory store of the most recent <see cref="TaskGraphResponse"/> per
/// (projectId, maxPastPRs) key. Populated by <c>GraphController.GetTaskGraphData</c>
/// on first access and refreshed in the background by
/// <see cref="ITaskGraphSnapshotRefresher"/>.
/// </summary>
public interface IProjectTaskGraphSnapshotStore
{
    /// <summary>
    /// Fetch an existing snapshot. Updates <c>LastAccessedAt</c> on hit so the
    /// refresher keeps tracking this key.
    /// </summary>
    TaskGraphSnapshotEntry? TryGet(string projectId, int maxPastPRs);

    /// <summary>
    /// Store or replace the snapshot for the given key. Marks the project
    /// tracked for future background refresh.
    /// </summary>
    void Store(string projectId, int maxPastPRs, TaskGraphResponse response, DateTimeOffset builtAt);

    /// <summary>
    /// Drop every snapshot for the given project, forcing the next read to
    /// recompute synchronously.
    /// </summary>
    void InvalidateProject(string projectId);

    /// <summary>
    /// Snapshot of the keys currently tracked — used by the refresher.
    /// </summary>
    IReadOnlyCollection<(string ProjectId, int MaxPastPRs)> GetTrackedKeys();

    /// <summary>
    /// Removes entries whose <c>LastAccessedAt</c> is older than <paramref name="idleCutoff"/>.
    /// </summary>
    int EvictIdle(DateTimeOffset idleCutoff);

    /// <summary>
    /// Applies a structure-preserving field patch to every entry belonging to
    /// <paramref name="projectId"/>. Non-null properties on <paramref name="patch"/>
    /// overlay the matching node's <c>Issue</c>; nulls mean unchanged.
    /// No-op when no entry contains a matching node — never recreates an entry.
    /// Bumps <c>LastBuiltAt</c> on each mutated entry.
    /// </summary>
    void PatchIssueFields(string projectId, string issueId, IssueFieldPatch patch);
}

/// <summary>
/// Per-project snapshot entry. <c>LastAccessedAt</c> is mutated on read; the
/// refresher uses it to drop idle entries.
/// </summary>
public sealed class TaskGraphSnapshotEntry
{
    public required TaskGraphResponse Response { get; init; }
    public required DateTimeOffset LastBuiltAt { get; set; }
    public required DateTimeOffset LastAccessedAt { get; set; }
}
