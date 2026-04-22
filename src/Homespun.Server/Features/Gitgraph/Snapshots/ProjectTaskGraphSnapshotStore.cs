using System.Collections.Concurrent;
using Homespun.Features.Gitgraph.Telemetry;
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

    public void PatchIssueFields(string projectId, string issueId, IssueFieldPatch patch)
    {
        using var activity = GraphgraphActivitySource.Instance.StartActivity("graph.snapshot.patch");
        activity?.SetTag("project.id", projectId);
        activity?.SetTag("issue.id", issueId);
        activity?.SetTag("patch.fields", JoinPatchedFieldNames(patch));

        var now = timeProvider.GetUtcNow();

        foreach (var key in _entries.Keys)
        {
            if (!string.Equals(key.ProjectId, projectId, StringComparison.Ordinal))
            {
                continue;
            }

            if (!_entries.TryGetValue(key, out var existing))
            {
                // Concurrent InvalidateProject removed this entry. A no-op is
                // the correct outcome — patches never re-create evicted entries.
                continue;
            }

            var nodeIndex = existing.Response.Nodes.FindIndex(n =>
                string.Equals(n.Issue.Id, issueId, StringComparison.Ordinal));
            if (nodeIndex < 0)
            {
                continue;
            }

            var existingNode = existing.Response.Nodes[nodeIndex];
            var patchedNode = new TaskGraphNodeResponse
            {
                Issue = ApplyPatch(existingNode.Issue, patch),
                Lane = existingNode.Lane,
                Row = existingNode.Row,
                IsActionable = existingNode.IsActionable,
            };

            var newNodes = new List<TaskGraphNodeResponse>(existing.Response.Nodes);
            newNodes[nodeIndex] = patchedNode;

            var newResponse = new TaskGraphResponse
            {
                Nodes = newNodes,
                TotalLanes = existing.Response.TotalLanes,
                MergedPrs = existing.Response.MergedPrs,
                HasMorePastPrs = existing.Response.HasMorePastPrs,
                TotalPastPrsShown = existing.Response.TotalPastPrsShown,
                AgentStatuses = existing.Response.AgentStatuses,
                LinkedPrs = existing.Response.LinkedPrs,
                OpenSpecStates = existing.Response.OpenSpecStates,
                MainOrphanChanges = existing.Response.MainOrphanChanges,
            };

            var replacement = new TaskGraphSnapshotEntry
            {
                Response = newResponse,
                LastBuiltAt = now,
                LastAccessedAt = existing.LastAccessedAt,
            };

            // TryUpdate is atomic CAS on the observed `existing` reference.
            // If a racing Store/PatchIssueFields replaced the entry we lose the
            // patch — but that is identical to "patch then invalidate" and
            // consistent with D7 in the design doc.
            _entries.TryUpdate(key, replacement, existing);
        }
    }

    private static IssueResponse ApplyPatch(IssueResponse original, IssueFieldPatch patch) => new()
    {
        Id = original.Id,
        Title = patch.Title ?? original.Title,
        Description = patch.Description ?? original.Description,
        Status = original.Status,
        Type = original.Type,
        Priority = patch.Priority ?? original.Priority,
        LinkedPRs = original.LinkedPRs,
        LinkedIssues = original.LinkedIssues,
        ParentIssues = original.ParentIssues,
        Tags = patch.Tags ?? original.Tags,
        WorkingBranchId = original.WorkingBranchId,
        ExecutionMode = patch.ExecutionMode ?? original.ExecutionMode,
        CreatedBy = patch.CreatedBy ?? original.CreatedBy,
        AssignedTo = patch.AssignedTo ?? original.AssignedTo,
        LastUpdate = patch.LastUpdate ?? original.LastUpdate,
        CreatedAt = original.CreatedAt,
    };

    private static string JoinPatchedFieldNames(IssueFieldPatch patch)
    {
        var names = new List<string>(8);
        if (patch.Title is not null) names.Add(nameof(IssueFieldPatch.Title));
        if (patch.Description is not null) names.Add(nameof(IssueFieldPatch.Description));
        if (patch.Priority is not null) names.Add(nameof(IssueFieldPatch.Priority));
        if (patch.Tags is not null) names.Add(nameof(IssueFieldPatch.Tags));
        if (patch.AssignedTo is not null) names.Add(nameof(IssueFieldPatch.AssignedTo));
        if (patch.CreatedBy is not null) names.Add(nameof(IssueFieldPatch.CreatedBy));
        if (patch.ExecutionMode is not null) names.Add(nameof(IssueFieldPatch.ExecutionMode));
        if (patch.LastUpdate is not null) names.Add(nameof(IssueFieldPatch.LastUpdate));
        return string.Join(",", names);
    }

    private readonly record struct SnapshotKey(string ProjectId, int MaxPastPRs);
}
