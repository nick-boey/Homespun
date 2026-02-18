using Fleece.Core.Models;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.Gitgraph;

namespace Homespun.Client.Components;

public enum TaskGraphMarkerType { Actionable, Open, Complete, Closed }

public abstract record TaskGraphRenderLine;
public record TaskGraphIssueRenderLine(
    string IssueId, string Title, int Lane, TaskGraphMarkerType Marker,
    int? ParentLane, bool IsFirstChild, bool IsSeriesChild,
    bool DrawTopLine, bool DrawBottomLine, int? SeriesConnectorFromLane,
    IssueType IssueType, bool HasDescription, TaskGraphLinkedPr? LinkedPr, AgentStatusData? AgentStatus,
    bool DrawLane0Connector = false, bool IsLastLane0Connector = false, bool DrawLane0PassThrough = false) : TaskGraphRenderLine;
public record TaskGraphSeparatorRenderLine : TaskGraphRenderLine;
public record TaskGraphPrRenderLine(int PrNumber, string Title, string? Url, bool IsMerged, bool HasDescription, AgentStatusData? AgentStatus, bool DrawTopLine, bool DrawBottomLine) : TaskGraphRenderLine;
public record TaskGraphLoadMoreRenderLine : TaskGraphRenderLine;

/// <summary>
/// Converts a TaskGraphResponse into a list of render lines for the TaskGraphView component.
/// Ports the algorithm from TaskGraphTextRenderer (server-side text rendering) to produce
/// structured render instructions instead of plain text.
/// </summary>
public static class TaskGraphLayoutService
{
    public static List<TaskGraphRenderLine> ComputeLayout(TaskGraphResponse? taskGraph)
    {
        if (taskGraph == null)
            return [];

        // Return early if no nodes and no PRs
        if (taskGraph.Nodes.Count == 0 && taskGraph.MergedPrs.Count == 0)
            return [];

        var result = new List<TaskGraphRenderLine>();

        // Add load more button at the very top if there are more PRs
        if (taskGraph.HasMorePastPrs)
        {
            result.Add(new TaskGraphLoadMoreRenderLine());
        }

        // Add merged/closed PRs at the top with appropriate top/bottom line flags
        var hasIssues = taskGraph.Nodes.Count > 0;
        for (var prIdx = 0; prIdx < taskGraph.MergedPrs.Count; prIdx++)
        {
            var pr = taskGraph.MergedPrs[prIdx];
            var isFirstPr = prIdx == 0;
            var isLastPr = prIdx == taskGraph.MergedPrs.Count - 1;

            // First PR: no top line (nothing above it)
            // Last PR: has bottom line only if there are issues below
            // Middle PRs: both top and bottom lines
            var drawTopLine = !isFirstPr;
            var drawBottomLine = !isLastPr || (isLastPr && hasIssues);

            result.Add(new TaskGraphPrRenderLine(
                pr.Number,
                pr.Title,
                pr.Url,
                pr.IsMerged,
                pr.HasDescription,
                pr.AgentStatus,
                drawTopLine,
                drawBottomLine));
        }

        // Add separator if we have PRs and issues
        if (taskGraph.MergedPrs.Count > 0 && taskGraph.Nodes.Count > 0)
        {
            result.Add(new TaskGraphSeparatorRenderLine());
        }

        // When merged PRs exist, offset all lanes by +1 to make room for the
        // vertical connector line in lane 0 that connects PRs to issues
        var laneOffset = taskGraph.MergedPrs.Count > 0 ? 1 : 0;

        var groups = GroupNodes(taskGraph.Nodes);
        foreach (var group in groups)
        {
            RenderGroup(result, group, taskGraph.AgentStatuses, taskGraph.LinkedPrs, laneOffset);
        }

        // Post-process: connect merged PR vertical line at lane 0 to leftmost issue nodes
        if (laneOffset > 0)
        {
            var firstIdx = -1;
            var lastIdx = -1;
            for (var i = 0; i < result.Count; i++)
            {
                if (result[i] is TaskGraphIssueRenderLine irl && irl.Lane == laneOffset)
                {
                    if (firstIdx == -1) firstIdx = i;
                    lastIdx = i;
                }
            }

            if (firstIdx >= 0)
            {
                for (var i = firstIdx; i <= lastIdx; i++)
                {
                    if (result[i] is TaskGraphIssueRenderLine irl)
                    {
                        if (irl.Lane == laneOffset)
                        {
                            result[i] = irl with
                            {
                                DrawLane0Connector = true,
                                IsLastLane0Connector = i == lastIdx
                            };
                        }
                        else
                        {
                            result[i] = irl with { DrawLane0PassThrough = true };
                        }
                    }
                }
            }
        }

        return result;
    }

    private static List<List<TaskGraphNodeResponse>> GroupNodes(List<TaskGraphNodeResponse> nodes)
    {
        var nodeById = new Dictionary<string, TaskGraphNodeResponse>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
            nodeById[node.Issue.Id] = node;

        var adjacency = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        {
            if (!adjacency.ContainsKey(node.Issue.Id))
                adjacency[node.Issue.Id] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var parentRef in node.Issue.ParentIssues)
            {
                if (!nodeById.ContainsKey(parentRef.ParentIssue)) continue;

                adjacency[node.Issue.Id].Add(parentRef.ParentIssue);

                if (!adjacency.ContainsKey(parentRef.ParentIssue))
                    adjacency[parentRef.ParentIssue] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                adjacency[parentRef.ParentIssue].Add(node.Issue.Id);
            }
        }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groups = new List<List<TaskGraphNodeResponse>>();

        foreach (var node in nodes)
        {
            if (visited.Contains(node.Issue.Id)) continue;

            var component = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var queue = new Queue<string>();
            queue.Enqueue(node.Issue.Id);
            visited.Add(node.Issue.Id);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                component.Add(current);

                if (adjacency.TryGetValue(current, out var neighbors))
                {
                    foreach (var neighbor in neighbors)
                    {
                        if (visited.Add(neighbor))
                            queue.Enqueue(neighbor);
                    }
                }
            }

            var group = nodes
                .Where(n => component.Contains(n.Issue.Id))
                .OrderBy(n => n.Row)
                .ToList();
            groups.Add(group);
        }

        groups.Sort((a, b) => a[0].Row.CompareTo(b[0].Row));

        return groups;
    }

    private static void RenderGroup(
        List<TaskGraphRenderLine> result,
        List<TaskGraphNodeResponse> group,
        Dictionary<string, AgentStatusData> agentStatuses,
        Dictionary<string, TaskGraphLinkedPr> linkedPrs,
        int laneOffset = 0)
    {
        if (group.Count == 0) return;

        var minLane = group.Min(n => n.Lane);

        // Pre-compute parent assignments and children-per-parent counts
        var parentByNode = new Dictionary<string, TaskGraphNodeResponse>(StringComparer.OrdinalIgnoreCase);
        var childrenCountByParent = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in group)
        {
            TaskGraphNodeResponse? parentNode = null;
            foreach (var parentRef in node.Issue.ParentIssues)
            {
                var candidate = group.FirstOrDefault(n =>
                    string.Equals(n.Issue.Id, parentRef.ParentIssue, StringComparison.OrdinalIgnoreCase));
                if (candidate != null && (parentNode == null || candidate.Lane > parentNode.Lane))
                    parentNode = candidate;
            }

            if (parentNode != null && parentNode.Lane - minLane > node.Lane - minLane)
            {
                parentByNode[node.Issue.Id] = parentNode;
                childrenCountByParent.TryGetValue(parentNode.Issue.Id, out var count);
                childrenCountByParent[parentNode.Issue.Id] = count + 1;
            }
        }

        // Pre-compute series child lane by parent: for parents with Series execution mode
        // Store the original (non-offset) lanes for internal calculations
        var seriesChildLaneByParent = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in group)
        {
            if (parentByNode.TryGetValue(node.Issue.Id, out var pNode)
                && pNode.Issue.ExecutionMode == ExecutionMode.Series)
            {
                seriesChildLaneByParent[pNode.Issue.Id] = node.Lane - minLane;
            }
        }

        var childrenRendered = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < group.Count; i++)
        {
            var node = group[i];
            var baseLane = node.Lane - minLane;
            var lane = baseLane + laneOffset; // Apply lane offset for final position
            var marker = GetMarker(node);

            parentByNode.TryGetValue(node.Issue.Id, out var parentNode);
            var baseParentLane = parentNode != null ? parentNode.Lane - minLane : (int?)null;
            var parentLane = baseParentLane.HasValue ? baseParentLane.Value + laneOffset : (int?)null;
            var isFirstChild = false;

            if (parentNode != null && baseParentLane > baseLane)
            {
                if (!childrenRendered.ContainsKey(parentNode.Issue.Id))
                    childrenRendered[parentNode.Issue.Id] = 0;
                childrenRendered[parentNode.Issue.Id]++;

                isFirstChild = childrenRendered[parentNode.Issue.Id] == 1;
            }

            // Determine if this is a series child (parent has Series execution mode)
            var isSeriesChild = parentNode != null
                && parentNode.Issue.ExecutionMode == ExecutionMode.Series;

            // Compute DrawTopLine (uses base lanes for calculations)
            var drawTopLine = false;
            if (i > 0)
            {
                var prevNode = group[i - 1];
                var prevBaseLane = prevNode.Lane - minLane;
                parentByNode.TryGetValue(prevNode.Issue.Id, out var prevParentNode);
                var prevBaseParentLane = prevParentNode != null ? prevParentNode.Lane - minLane : (int?)null;
                var prevIsSeriesChild = prevParentNode != null
                    && prevParentNode.Issue.ExecutionMode == ExecutionMode.Series;

                // Previous node is a parallel child whose junction is at this node's lane
                if (!prevIsSeriesChild && prevBaseParentLane.HasValue && prevBaseParentLane.Value == baseLane && prevBaseParentLane.Value > prevBaseLane)
                    drawTopLine = true;

                // Previous node is a series sibling of the same parent (vertical continuity)
                if (isSeriesChild && prevIsSeriesChild && prevParentNode != null && parentNode != null
                    && string.Equals(prevParentNode.Issue.Id, parentNode.Issue.Id, StringComparison.OrdinalIgnoreCase))
                    drawTopLine = true;
            }

            // Compute DrawBottomLine: true when this node is a series child
            var drawBottomLine = isSeriesChild;

            // Compute SeriesConnectorFromLane: set when this node is a parent receiving series children
            // Apply lane offset to the series connector lane as well
            int? seriesConnectorFromLane = seriesChildLaneByParent.TryGetValue(node.Issue.Id, out var childLane)
                ? childLane + laneOffset : null;

            // Get linked PR and agent status for this issue
            linkedPrs.TryGetValue(node.Issue.Id, out var linkedPr);
            agentStatuses.TryGetValue(node.Issue.Id, out var agentStatus);

            result.Add(new TaskGraphIssueRenderLine(
                IssueId: node.Issue.Id,
                Title: node.Issue.Title,
                Lane: lane,
                Marker: marker,
                ParentLane: parentLane,
                IsFirstChild: isFirstChild,
                IsSeriesChild: isSeriesChild,
                DrawTopLine: drawTopLine,
                DrawBottomLine: drawBottomLine,
                SeriesConnectorFromLane: seriesConnectorFromLane,
                IssueType: node.Issue.Type,
                HasDescription: !string.IsNullOrWhiteSpace(node.Issue.Description),
                LinkedPr: linkedPr,
                AgentStatus: agentStatus
            ));
        }
    }

    private static TaskGraphMarkerType GetMarker(TaskGraphNodeResponse node)
    {
        return node.Issue.Status switch
        {
            IssueStatus.Complete => TaskGraphMarkerType.Complete,
            IssueStatus.Closed or IssueStatus.Archived => TaskGraphMarkerType.Closed,
            _ => node.IsActionable ? TaskGraphMarkerType.Actionable : TaskGraphMarkerType.Open
        };
    }
}
