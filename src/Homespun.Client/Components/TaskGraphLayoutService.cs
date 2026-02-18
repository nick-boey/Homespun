using Fleece.Core.Models;
using Homespun.Shared.Models.Fleece;

namespace Homespun.Client.Components;

public enum TaskGraphMarkerType { Actionable, Open, Complete, Closed }

public abstract record TaskGraphRenderLine;
public record TaskGraphIssueRenderLine(
    string IssueId, string Title, int Lane, TaskGraphMarkerType Marker,
    int? ParentLane, bool IsFirstChild, bool IsSeriesChild,
    bool DrawTopLine, bool DrawBottomLine, int? SeriesConnectorFromLane) : TaskGraphRenderLine;
public record TaskGraphSeparatorRenderLine : TaskGraphRenderLine;

/// <summary>
/// Converts a TaskGraphResponse into a list of render lines for the TaskGraphView component.
/// Ports the algorithm from TaskGraphTextRenderer (server-side text rendering) to produce
/// structured render instructions instead of plain text.
/// </summary>
public static class TaskGraphLayoutService
{
    public static List<TaskGraphRenderLine> ComputeLayout(TaskGraphResponse? taskGraph)
    {
        if (taskGraph == null || taskGraph.Nodes.Count == 0)
            return [];

        var groups = GroupNodes(taskGraph.Nodes);
        var result = new List<TaskGraphRenderLine>();
        var firstGroup = true;

        foreach (var group in groups)
        {
            if (!firstGroup)
                result.Add(new TaskGraphSeparatorRenderLine());
            firstGroup = false;

            RenderGroup(result, group);
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

    private static void RenderGroup(List<TaskGraphRenderLine> result, List<TaskGraphNodeResponse> group)
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
            var lane = node.Lane - minLane;
            var marker = GetMarker(node);

            parentByNode.TryGetValue(node.Issue.Id, out var parentNode);
            var parentLane = parentNode != null ? parentNode.Lane - minLane : (int?)null;
            var isFirstChild = false;

            if (parentNode != null && parentLane > lane)
            {
                if (!childrenRendered.ContainsKey(parentNode.Issue.Id))
                    childrenRendered[parentNode.Issue.Id] = 0;
                childrenRendered[parentNode.Issue.Id]++;

                isFirstChild = childrenRendered[parentNode.Issue.Id] == 1;
            }

            // Determine if this is a series child (parent has Series execution mode)
            var isSeriesChild = parentNode != null
                && parentNode.Issue.ExecutionMode == ExecutionMode.Series;

            // Compute DrawTopLine
            var drawTopLine = false;
            if (i > 0)
            {
                var prevNode = group[i - 1];
                var prevLane = prevNode.Lane - minLane;
                parentByNode.TryGetValue(prevNode.Issue.Id, out var prevParentNode);
                var prevParentLane = prevParentNode != null ? prevParentNode.Lane - minLane : (int?)null;
                var prevIsSeriesChild = prevParentNode != null
                    && prevParentNode.Issue.ExecutionMode == ExecutionMode.Series;

                // Previous node is a parallel child whose junction is at this node's lane
                if (!prevIsSeriesChild && prevParentLane.HasValue && prevParentLane.Value == lane && prevParentLane.Value > prevLane)
                    drawTopLine = true;

                // Previous node is a series sibling of the same parent (vertical continuity)
                if (isSeriesChild && prevIsSeriesChild && prevParentNode != null && parentNode != null
                    && string.Equals(prevParentNode.Issue.Id, parentNode.Issue.Id, StringComparison.OrdinalIgnoreCase))
                    drawTopLine = true;
            }

            // Compute DrawBottomLine: true when this node is a series child
            var drawBottomLine = isSeriesChild;

            // Compute SeriesConnectorFromLane: set when this node is a parent receiving series children
            int? seriesConnectorFromLane = seriesChildLaneByParent.TryGetValue(node.Issue.Id, out var childLane)
                ? childLane : null;

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
                SeriesConnectorFromLane: seriesConnectorFromLane
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
