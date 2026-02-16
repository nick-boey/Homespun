namespace Homespun.Shared.Models.Gitgraph;

/// <summary>
/// Calculates lane layout information for task graph nodes.
/// Computes lanes from parent-child relationships in the graph:
/// - Lane 0 (left): Leaf nodes (actionable items with no sub-tasks)
/// - Higher lanes (right): Parent/container issues that have sub-tasks
/// Connectors draw horizontally from parent (right) to child (left).
/// </summary>
public class TaskGraphLaneCalculator
{
    /// <summary>
    /// Calculate lane layout for task graph nodes.
    /// Lanes are computed from parent-child relationships, not pre-assigned values.
    /// </summary>
    /// <param name="nodes">Nodes in display order.</param>
    /// <returns>Layout information including computed lane assignments and per-row rendering info.</returns>
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

        // Step 1: Build children lookup by inverting ParentIds
        var nodeIds = new HashSet<string>(nodes.Select(n => n.Id), StringComparer.OrdinalIgnoreCase);
        var childrenByNodeId = BuildChildrenLookup(nodes, nodeIds);

        // Step 2: Topological sort (leaves first, roots last)
        var sortedNodes = TopologicalSort(nodes, childrenByNodeId);

        // Step 3: Assign lanes based on children
        var laneAssignments = AssignLanes(sortedNodes, childrenByNodeId);

        // Step 4: Build row infos in display order (original node order)
        var maxLane = laneAssignments.Count > 0 ? laneAssignments.Values.Max() : 0;
        var rowInfos = BuildRowInfos(nodes, laneAssignments, maxLane);

        return new TimelineLaneLayout
        {
            LaneAssignments = laneAssignments,
            MaxLanes = maxLane + 1,
            RowInfos = rowInfos
        };
    }

    /// <summary>
    /// Builds a lookup of children for each node by inverting ParentIds.
    /// Only includes parents that are in the current graph.
    /// </summary>
    private static Dictionary<string, List<string>> BuildChildrenLookup(
        IReadOnlyList<IGraphNode> nodes,
        HashSet<string> nodeIds)
    {
        var children = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        // Initialize empty lists for all nodes
        foreach (var node in nodes)
        {
            children[node.Id] = [];
        }

        // For each node, register it as a child of its parents (that are in the graph)
        foreach (var node in nodes)
        {
            foreach (var parentId in node.ParentIds)
            {
                if (nodeIds.Contains(parentId) && children.TryGetValue(parentId, out var parentChildren))
                {
                    parentChildren.Add(node.Id);
                }
            }
        }

        return children;
    }

    /// <summary>
    /// Topological sort with leaves (no children) first and root parents last.
    /// Uses Kahn's algorithm for cycle-safe ordering.
    /// </summary>
    private static List<IGraphNode> TopologicalSort(
        IReadOnlyList<IGraphNode> nodes,
        Dictionary<string, List<string>> childrenByNodeId)
    {
        var nodeLookup = nodes.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);

        // Count how many children each node has (in-degree for reverse topo sort)
        var childCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        {
            childCount[node.Id] = childrenByNodeId.TryGetValue(node.Id, out var children) ? children.Count : 0;
        }

        // Start with leaf nodes (no children in graph)
        var queue = new Queue<string>();
        foreach (var (id, count) in childCount)
        {
            if (count == 0)
            {
                queue.Enqueue(id);
            }
        }

        // Build parent lookup for traversal
        var parentsByNodeId = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        {
            var parentsInGraph = node.ParentIds
                .Where(pid => nodeLookup.ContainsKey(pid))
                .ToList();
            parentsByNodeId[node.Id] = parentsInGraph;
        }

        var sorted = new List<IGraphNode>();
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            sorted.Add(nodeLookup[id]);

            // For each parent of this node, decrement their child count
            if (parentsByNodeId.TryGetValue(id, out var parents))
            {
                foreach (var parentId in parents)
                {
                    childCount[parentId]--;
                    if (childCount[parentId] == 0)
                    {
                        queue.Enqueue(parentId);
                    }
                }
            }
        }

        // If there are remaining nodes (cycle), add them at lane 0
        foreach (var node in nodes)
        {
            if (!sorted.Any(n => string.Equals(n.Id, node.Id, StringComparison.OrdinalIgnoreCase)))
            {
                sorted.Add(node);
            }
        }

        return sorted;
    }

    /// <summary>
    /// Assigns lanes based on children: leaf nodes get lane 0,
    /// parent nodes get max(children lanes) + 1.
    /// Nodes must be in topological order (leaves first).
    /// </summary>
    private static Dictionary<string, int> AssignLanes(
        List<IGraphNode> sortedNodes,
        Dictionary<string, List<string>> childrenByNodeId)
    {
        var lanes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in sortedNodes)
        {
            if (childrenByNodeId.TryGetValue(node.Id, out var children) && children.Count > 0)
            {
                // Parent node: lane = max(children lanes) + 1
                var maxChildLane = children
                    .Where(cid => lanes.ContainsKey(cid))
                    .Select(cid => lanes[cid])
                    .DefaultIfEmpty(0)
                    .Max();
                lanes[node.Id] = maxChildLane + 1;
            }
            else
            {
                // Leaf node: lane 0 (actionable)
                lanes[node.Id] = 0;
            }
        }

        return lanes;
    }

    /// <summary>
    /// Builds row info for each node in display order.
    /// </summary>
    private static List<RowLaneInfo> BuildRowInfos(
        IReadOnlyList<IGraphNode> nodes,
        Dictionary<string, int> laneAssignments,
        int maxLane)
    {
        var rowInfos = new List<RowLaneInfo>();

        // Track first/last appearances in each lane
        var laneFirstSeen = new Dictionary<int, int>();
        var laneLastSeen = new Dictionary<int, int>();

        for (var i = 0; i < nodes.Count; i++)
        {
            var nodeLane = laneAssignments.GetValueOrDefault(nodes[i].Id, 0);
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

        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            var nodeLane = laneAssignments.GetValueOrDefault(node.Id, 0);

            // Determine connector from parent lane
            int? connectorFromLane = null;
            if (parentsByNodeId.TryGetValue(node.Id, out var parentIds) && parentIds.Count > 0)
            {
                var primaryParentId = parentIds[0];
                if (laneAssignments.TryGetValue(primaryParentId, out var parentLane))
                {
                    if (parentLane != nodeLane)
                    {
                        connectorFromLane = parentLane;
                    }
                }
            }

            // Calculate active lanes
            var activeLanes = CalculateActiveLanes(i, nodes, laneAssignments);

            // Determine if this is first/last in lane
            var isFirstInLane = laneFirstSeen.TryGetValue(nodeLane, out var firstRow) && firstRow == i;
            var isLastInLane = laneLastSeen.TryGetValue(nodeLane, out var lastRow) && lastRow == i;

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
                IsFirstRowInLane = isFirstInLane && nodeLane > 0,
                IsLastRowInLane = isLastInLane && nodeLane > 0,
                LanesEndingAtThisRow = lanesEndingAtThisRow,
                ReservedLanes = new HashSet<int>(),
                IsDivider = node.NodeType == GraphNodeType.SectionDivider,
                IsFlatOrphan = false
            });
        }

        return rowInfos;
    }

    /// <summary>
    /// Calculates which lanes should have vertical lines at a given row.
    /// </summary>
    private static HashSet<int> CalculateActiveLanes(
        int currentRow,
        IReadOnlyList<IGraphNode> nodes,
        Dictionary<string, int> laneAssignments)
    {
        var activeLanes = new HashSet<int>();

        // Always include lane 0
        activeLanes.Add(0);

        // Include lanes for nodes at or before this row
        for (var i = 0; i <= currentRow && i < nodes.Count; i++)
        {
            if (laneAssignments.TryGetValue(nodes[i].Id, out var lane))
            {
                activeLanes.Add(lane);
            }
        }

        return activeLanes;
    }
}
