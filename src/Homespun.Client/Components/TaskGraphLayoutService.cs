using Fleece.Core.Models;
using Homespun.Shared.Models.Fleece;
using Homespun.Shared.Models.Gitgraph;

namespace Homespun.Client.Components;

public enum TaskGraphMarkerType { Actionable, Open, Complete, Closed }

/// <summary>
/// Context for computing a draft issue's render line before it's created.
/// </summary>
public record DraftIssueContext(
    string? ReferenceIssueId,
    bool IsAbove,
    string? PendingParentId,
    string? PendingChildId,
    string? InheritedParentId);

public abstract record TaskGraphRenderLine;
public record TaskGraphIssueRenderLine(
    string IssueId, string Title, int Lane, TaskGraphMarkerType Marker,
    int? ParentLane, bool IsFirstChild, bool IsSeriesChild,
    bool DrawTopLine, bool DrawBottomLine, int? SeriesConnectorFromLane,
    IssueType IssueType, IssueStatus Status, bool HasDescription, TaskGraphLinkedPr? LinkedPr, AgentStatusData? AgentStatus,
    bool DrawLane0Connector = false, bool IsLastLane0Connector = false, bool DrawLane0PassThrough = false,
    string? Lane0Color = null, bool HasHiddenParent = false, bool HiddenParentIsSeriesMode = false) : TaskGraphRenderLine;
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
    /// <summary>
    /// Computes the layout for a task graph, optionally filtering nodes by depth.
    /// </summary>
    /// <param name="taskGraph">The task graph response to layout</param>
    /// <param name="maxDepth">Maximum depth (lane) to display. Default is 3 (shows lanes 0-3).
    /// Use int.MaxValue to show all levels.</param>
    /// <returns>List of render lines for the task graph view</returns>
    public static List<TaskGraphRenderLine> ComputeLayout(TaskGraphResponse? taskGraph, int maxDepth = 3)
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

        // Build lookup for hidden parent detection
        var allNodesByIssueId = taskGraph.Nodes.ToDictionary(n => n.Issue.Id, n => n, StringComparer.OrdinalIgnoreCase);

        var groups = GroupNodes(taskGraph.Nodes);
        foreach (var group in groups)
        {
            // Filter nodes by maxDepth within each group
            // First compute minLane for this group to normalize lanes
            var minLane = group.Min(n => n.Lane);
            var visibleNodes = group.Where(n => n.Lane - minLane <= maxDepth).ToList();

            // Compute hidden parent info: which nodes have parents filtered out
            var hiddenParentInfo = ComputeHiddenParentInfo(visibleNodes, group, allNodesByIssueId, minLane, maxDepth);

            RenderGroup(result, visibleNodes, taskGraph.AgentStatuses, taskGraph.LinkedPrs, laneOffset, hiddenParentInfo);
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
                // First pass: find the last issue that actually gets a connector
                // (blocked series siblings don't get connectors)
                var lastConnectorIdx = -1;
                for (var i = firstIdx; i <= lastIdx; i++)
                {
                    if (result[i] is TaskGraphIssueRenderLine irl && irl.Lane == laneOffset)
                    {
                        var isBlockedSeriesSibling = irl.IsSeriesChild && irl.DrawTopLine;
                        if (!isBlockedSeriesSibling)
                        {
                            lastConnectorIdx = i;
                        }
                    }
                }

                // Second pass: apply the flags
                for (var i = firstIdx; i <= lastIdx; i++)
                {
                    if (result[i] is TaskGraphIssueRenderLine irl)
                    {
                        if (irl.Lane == laneOffset)
                        {
                            var isBlockedSeriesSibling = irl.IsSeriesChild && irl.DrawTopLine;
                            if (isBlockedSeriesSibling)
                            {
                                result[i] = irl with { DrawLane0PassThrough = true };
                            }
                            else
                            {
                                result[i] = irl with
                                {
                                    DrawLane0Connector = true,
                                    IsLastLane0Connector = i == lastConnectorIdx
                                };
                            }
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

    /// <summary>
    /// Computes which visible nodes have parents that were filtered out due to maxDepth.
    /// </summary>
    private static Dictionary<string, (bool HasHiddenParent, bool HiddenParentIsSeriesMode)> ComputeHiddenParentInfo(
        List<TaskGraphNodeResponse> visibleNodes,
        List<TaskGraphNodeResponse> allGroupNodes,
        Dictionary<string, TaskGraphNodeResponse> allNodesByIssueId,
        int minLane,
        int maxDepth)
    {
        var result = new Dictionary<string, (bool HasHiddenParent, bool HiddenParentIsSeriesMode)>(StringComparer.OrdinalIgnoreCase);
        var visibleIds = new HashSet<string>(visibleNodes.Select(n => n.Issue.Id), StringComparer.OrdinalIgnoreCase);

        foreach (var node in visibleNodes)
        {
            var hasHiddenParent = false;
            var hiddenParentIsSeriesMode = false;

            // Check each parent reference
            foreach (var parentRef in node.Issue.ParentIssues)
            {
                // Find the parent node in all nodes (not just visible)
                if (allNodesByIssueId.TryGetValue(parentRef.ParentIssue, out var parentNode))
                {
                    // Check if parent is in this group and was filtered out
                    var parentInGroup = allGroupNodes.Any(n =>
                        string.Equals(n.Issue.Id, parentRef.ParentIssue, StringComparison.OrdinalIgnoreCase));

                    if (parentInGroup && !visibleIds.Contains(parentRef.ParentIssue))
                    {
                        // Parent was filtered out
                        hasHiddenParent = true;
                        hiddenParentIsSeriesMode = parentNode.Issue.ExecutionMode == ExecutionMode.Series;
                    }
                }
            }

            result[node.Issue.Id] = (hasHiddenParent, hiddenParentIsSeriesMode);
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
        int laneOffset = 0,
        Dictionary<string, (bool HasHiddenParent, bool HiddenParentIsSeriesMode)>? hiddenParentInfo = null)
    {
        if (group.Count == 0) return;

        var minLane = group.Min(n => n.Lane);

        // Find the "next" (actionable) issue - the one at lane 0 (before offset)
        // Its priority determines the lane0Color for the entire group
        var nextIssue = group.FirstOrDefault(n => n.Lane == minLane);
        var groupPriority = nextIssue?.Issue.Priority;
        var groupLane0Color = laneOffset > 0 ? TimelineSvgRenderer.GetPriorityColor(groupPriority) : null;

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

            // Get hidden parent info (defaults to false if not provided)
            var hasHiddenParent = false;
            var hiddenParentIsSeriesMode = false;
            if (hiddenParentInfo != null && hiddenParentInfo.TryGetValue(node.Issue.Id, out var info))
            {
                hasHiddenParent = info.HasHiddenParent;
                hiddenParentIsSeriesMode = info.HiddenParentIsSeriesMode;
            }

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
                Status: node.Issue.Status,
                HasDescription: !string.IsNullOrWhiteSpace(node.Issue.Description),
                LinkedPr: linkedPr,
                AgentStatus: agentStatus,
                Lane0Color: groupLane0Color,
                HasHiddenParent: hasHiddenParent,
                HiddenParentIsSeriesMode: hiddenParentIsSeriesMode
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

    /// <summary>
    /// Computes the render line for a draft issue that hasn't been created yet.
    /// This allows the graph to render edges correctly during insert mode.
    /// </summary>
    public static TaskGraphIssueRenderLine? ComputeDraftIssueLine(
        TaskGraphResponse? taskGraph,
        DraftIssueContext draft,
        List<TaskGraphRenderLine> existingLines)
    {
        if (taskGraph == null)
            return null;

        var issueLines = existingLines.OfType<TaskGraphIssueRenderLine>().ToList();
        var laneOffset = taskGraph.MergedPrs.Count > 0 ? 1 : 0;

        // Find the reference issue index and line
        var refIndex = -1;
        TaskGraphIssueRenderLine? refLine = null;
        if (!string.IsNullOrEmpty(draft.ReferenceIssueId))
        {
            for (var i = 0; i < issueLines.Count; i++)
            {
                if (string.Equals(issueLines[i].IssueId, draft.ReferenceIssueId, StringComparison.OrdinalIgnoreCase))
                {
                    refIndex = i;
                    refLine = issueLines[i];
                    break;
                }
            }
        }

        // Determine insertion position
        int insertIndex;
        if (refIndex < 0)
        {
            // No reference - inserting first issue
            insertIndex = 0;
        }
        else if (draft.IsAbove)
        {
            insertIndex = refIndex;
        }
        else
        {
            insertIndex = refIndex + 1;
        }

        // Determine lane and parent relationships
        int lane;
        int? parentLane = null;
        var isSeriesChild = false;
        var seriesConnectorFromLane = (int?)null;

        if (draft.PendingChildId != null)
        {
            // Tab pressed: draft becomes parent of the reference issue
            // Draft goes to a higher lane, reference stays at its current lane
            lane = (refLine?.Lane ?? 0) + 1;
        }
        else if (draft.PendingParentId != null)
        {
            // Shift+Tab pressed: draft becomes child of the reference issue
            // Draft stays at the reference's lane, reference conceptually moves to higher lane
            lane = refLine?.Lane ?? laneOffset;
            parentLane = lane + 1;

            // Check if the reference has Series execution mode
            var refNode = taskGraph.Nodes.FirstOrDefault(n =>
                string.Equals(n.Issue.Id, draft.PendingParentId, StringComparison.OrdinalIgnoreCase));
            if (refNode?.Issue.ExecutionMode == ExecutionMode.Series)
            {
                isSeriesChild = true;
            }
        }
        else if (draft.InheritedParentId != null)
        {
            // Sibling creation: inheriting parent from reference
            // Find the parent's execution mode to determine if this is a series child
            var parentNode = taskGraph.Nodes.FirstOrDefault(n =>
                string.Equals(n.Issue.Id, draft.InheritedParentId, StringComparison.OrdinalIgnoreCase));

            lane = refLine?.Lane ?? laneOffset;
            parentLane = refLine?.ParentLane;

            if (parentNode?.Issue.ExecutionMode == ExecutionMode.Series)
            {
                isSeriesChild = true;
            }
        }
        else
        {
            // No parent relationship - simple sibling insertion
            lane = refLine?.Lane ?? laneOffset;
        }

        // Determine DrawTopLine and DrawBottomLine based on adjacent issues
        var drawTopLine = false;
        var drawBottomLine = false;

        // Get adjacent issues based on insertion position
        TaskGraphIssueRenderLine? prevIssue = insertIndex > 0 && insertIndex <= issueLines.Count
            ? issueLines[insertIndex - 1]
            : null;
        TaskGraphIssueRenderLine? nextIssue = insertIndex < issueLines.Count
            ? issueLines[insertIndex]
            : null;

        // When inserting above, the "prev" is actually the issue before our reference
        // When inserting below, nextIssue is the one after our reference
        if (draft.IsAbove && refIndex >= 0)
        {
            prevIssue = refIndex > 0 ? issueLines[refIndex - 1] : null;
            nextIssue = refLine;
        }
        else if (!draft.IsAbove && refIndex >= 0)
        {
            prevIssue = refLine;
            nextIssue = refIndex + 1 < issueLines.Count ? issueLines[refIndex + 1] : null;
        }

        // Compute edge flags based on context
        if (isSeriesChild)
        {
            // Series children always have bottom lines (connecting down to next sibling or parent)
            drawBottomLine = true;

            // Has top line if there's a previous sibling at the same lane
            if (prevIssue != null && prevIssue.Lane == lane && prevIssue.IsSeriesChild)
            {
                drawTopLine = true;
            }
        }
        else
        {
            // Non-series case: simple vertical lines connecting siblings at the same lane
            // Has top line if there's a sibling above at the same lane (including inherited siblings)
            if (prevIssue != null && prevIssue.Lane == lane)
            {
                drawTopLine = true;
            }

            // Has bottom line if there's a sibling below at the same lane
            if (nextIssue != null && nextIssue.Lane == lane)
            {
                drawBottomLine = true;
            }
        }

        // Compute lane 0 connector flags for merged PRs
        var drawLane0Connector = false;
        var isLastLane0Connector = false;
        var drawLane0PassThrough = false;

        if (laneOffset > 0 && lane == laneOffset)
        {
            drawLane0Connector = true;
            // Check if this would be the last lane 0 connector
            var hasMoreLane0IssuesBelow = issueLines.Skip(insertIndex)
                .Any(l => l.Lane == laneOffset);
            isLastLane0Connector = !hasMoreLane0IssuesBelow;
        }
        else if (laneOffset > 0 && lane > laneOffset)
        {
            // Pass-through if there are lane 0 issues
            drawLane0PassThrough = issueLines.Any(l => l.Lane == laneOffset);
        }

        return new TaskGraphIssueRenderLine(
            IssueId: "DRAFT",
            Title: "",
            Lane: lane,
            Marker: TaskGraphMarkerType.Open,
            ParentLane: parentLane,
            IsFirstChild: false,
            IsSeriesChild: isSeriesChild,
            DrawTopLine: drawTopLine,
            DrawBottomLine: drawBottomLine,
            SeriesConnectorFromLane: seriesConnectorFromLane,
            IssueType: IssueType.Task,
            HasDescription: false,
            LinkedPr: null,
            AgentStatus: null,
            DrawLane0Connector: drawLane0Connector,
            IsLastLane0Connector: isLastLane0Connector,
            DrawLane0PassThrough: drawLane0PassThrough);
    }
}
