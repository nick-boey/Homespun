namespace Homespun.Shared.Models.Gitgraph;

/// <summary>
/// Calculates lane layout information for task graph nodes.
/// Unlike TimelineLaneCalculator, this uses the lane assignments provided by Fleece.Core's TaskGraphService:
/// - Lane 0 (left): Actionable items (no blocking dependencies)
/// - Higher lanes (right): Parent/blocking issues
/// Connectors draw horizontally from parent (right) to child (left).
/// </summary>
public class TaskGraphLaneCalculator
{
    /// <summary>
    /// Calculate lane layout for task graph nodes.
    /// </summary>
    /// <param name="nodes">Nodes in display order (already sorted by row, then lane).</param>
    /// <returns>Layout information including lane assignments and per-row rendering info.</returns>
    public TimelineLaneLayout Calculate(IReadOnlyList<IGraphNode> nodes)
    {
        if (nodes.Count == 0)
        {
            return new TimelineLaneLayout
            {
                LaneAssignments = new Dictionary<string, int>(),
                MaxLanes = 1,
                RowInfos = []
            };
        }

        var laneAssignments = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var rowInfos = new List<RowLaneInfo>();
        var maxLane = 0;

        // Build lookup for node lane info using interface properties
        var nodeLanes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        {
            var lane = node.TaskGraphLane ?? 0;
            nodeLanes[node.Id] = lane;
        }

        // Track which lanes have been seen (for determining first/last in lane)
        var laneFirstSeen = new Dictionary<int, int>(); // lane -> row index
        var laneLastSeen = new Dictionary<int, int>();  // lane -> row index

        // First pass: collect lane assignments and track lane usage
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var nodeLane = nodeLanes.GetValueOrDefault(node.Id, 0);

            laneAssignments[node.Id] = nodeLane;
            maxLane = Math.Max(maxLane, nodeLane);

            if (!laneFirstSeen.ContainsKey(nodeLane))
            {
                laneFirstSeen[nodeLane] = i;
            }
            laneLastSeen[nodeLane] = i;
        }

        // Build parent lookup for connector calculation
        var parentsByNodeId = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        {
            parentsByNodeId[node.Id] = node.ParentIds.ToList();
        }

        // Second pass: build row infos
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var nodeLane = laneAssignments[node.Id];

            // Determine connector from parent lane
            int? connectorFromLane = null;
            if (parentsByNodeId.TryGetValue(node.Id, out var parentIds) && parentIds.Count > 0)
            {
                var primaryParentId = parentIds[0];
                if (laneAssignments.TryGetValue(primaryParentId, out var parentLane))
                {
                    // Only draw connector if parent is in a different lane (horizontal connector)
                    if (parentLane != nodeLane)
                    {
                        connectorFromLane = parentLane;
                    }
                }
            }

            // Calculate active lanes (all lanes that have nodes in this row or nearby)
            var activeLanes = CalculateActiveLanes(i, nodes, nodeLanes);

            // Determine if this is first/last in lane
            var isFirstInLane = laneFirstSeen.TryGetValue(nodeLane, out var firstRow) && firstRow == i;
            var isLastInLane = laneLastSeen.TryGetValue(nodeLane, out var lastRow) && lastRow == i;

            // For task graph, we don't use the same vertical line semantics
            // Instead, we draw horizontal connectors between parent (right) and child (left)
            var lanesEndingAtThisRow = new HashSet<int>();
            if (isLastInLane && nodeLane > 0)
            {
                lanesEndingAtThisRow.Add(nodeLane);
            }

            rowInfos.Add(new RowLaneInfo
            {
                NodeId = node.Id,
                NodeLane = nodeLane,
                ActiveLanes = activeLanes,
                ConnectorFromLane = connectorFromLane,
                IsFirstRowInLane = isFirstInLane && nodeLane > 0, // Lane 0 never has first/last
                IsLastRowInLane = isLastInLane && nodeLane > 0,
                LanesEndingAtThisRow = lanesEndingAtThisRow,
                ReservedLanes = new HashSet<int>(),
                IsDivider = node.NodeType == GraphNodeType.SectionDivider,
                IsFlatOrphan = false // Task graph doesn't have flat orphans
            });
        }

        return new TimelineLaneLayout
        {
            LaneAssignments = laneAssignments,
            MaxLanes = maxLane + 1,
            RowInfos = rowInfos
        };
    }

    /// <summary>
    /// Calculates which lanes should have vertical lines at a given row.
    /// For task graph, we show lines for lanes that have nodes above and below this row.
    /// </summary>
    private static HashSet<int> CalculateActiveLanes(
        int currentRow,
        IReadOnlyList<IGraphNode> nodes,
        Dictionary<string, int> nodeLanes)
    {
        var activeLanes = new HashSet<int>();

        // Always include lane 0 (the actionable column)
        activeLanes.Add(0);

        // Find which lanes have nodes at or spanning this row
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (nodeLanes.TryGetValue(node.Id, out var lane))
            {
                // Include this lane if the node is at or before this row,
                // and there are connections that span through this row
                if (i <= currentRow)
                {
                    activeLanes.Add(lane);
                }
            }
        }

        return activeLanes;
    }
}
