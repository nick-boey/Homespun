using Homespun.Features.Gitgraph.Data;

namespace Homespun.Features.Gitgraph.Services;

/// <summary>
/// Calculates lane assignments for timeline graph nodes using a DFS-based algorithm.
/// Lanes run vertically, with the main branch in lane 0 and new branches claiming
/// the next available lane.
/// </summary>
public class TimelineLaneCalculator
{
    private readonly string _mainBranchName;

    public TimelineLaneCalculator(string mainBranchName = "main")
    {
        _mainBranchName = mainBranchName;
    }

    /// <summary>
    /// Calculate lane assignments for the given ordered nodes.
    /// </summary>
    /// <param name="nodes">Nodes in display order (as returned by GraphBuilder).</param>
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
        var branchToLane = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var activeLanes = new HashSet<int> { 0 }; // Lane 0 is always active for main
        var rowInfos = new List<RowLaneInfo>();
        var maxLaneUsed = 0;

        // Build a lookup of node ID -> list of child node IDs (nodes that have this node as a parent)
        var nodeChildren = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        {
            foreach (var parentId in node.ParentIds)
            {
                if (!nodeChildren.TryGetValue(parentId, out var children))
                {
                    children = [];
                    nodeChildren[parentId] = children;
                }
                children.Add(node.Id);
            }
        }

        // Track which nodes have been processed for lane release calculation
        var processedNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Track which child of a multi-child parent has already been processed
        // When the first child is processed, it can reuse the parent's lane
        // Subsequent siblings need new lanes
        var firstChildProcessed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Main branch always uses lane 0
        branchToLane[_mainBranchName] = 0;

        foreach (var node in nodes)
        {
            int nodeLane;
            int? connectorFromLane = null;

            if (node.BranchName == _mainBranchName)
            {
                // Main branch nodes are always in lane 0
                nodeLane = 0;
            }
            else if (branchToLane.TryGetValue(node.BranchName, out var existingLane))
            {
                // Branch already has a lane assigned
                nodeLane = existingLane;
            }
            else
            {
                // New branch: determine lane based on parent's children count
                // If parent has multiple children and we're not the first one, we need a new lane
                var parentId = node.ParentIds.FirstOrDefault();
                var parentHasMultipleChildren = parentId != null &&
                                                 nodeChildren.TryGetValue(parentId, out var siblings) &&
                                                 siblings.Count > 1;

                var isFirstSibling = parentId != null && !firstChildProcessed.Contains(parentId);

                if (parentHasMultipleChildren && !isFirstSibling)
                {
                    // Sibling of already-processed child: force a new lane
                    nodeLane = GetNextAvailableLane(activeLanes);
                }
                else
                {
                    // First child or single child: can reuse released lanes
                    nodeLane = GetNextAvailableLane(activeLanes);
                }

                branchToLane[node.BranchName] = nodeLane;
                activeLanes.Add(nodeLane);
                maxLaneUsed = Math.Max(maxLaneUsed, nodeLane);

                // Mark parent as having had a child processed
                if (parentId != null)
                {
                    firstChildProcessed.Add(parentId);
                }

                // Determine connector from parent
                connectorFromLane = GetParentLane(node, laneAssignments);
            }

            laneAssignments[node.Id] = nodeLane;
            processedNodes.Add(node.Id);

            // Build the set of active lanes for this row
            // Include all lanes that have nodes below (not yet ended)
            var currentActiveLanes = new HashSet<int>(activeLanes);

            rowInfos.Add(new RowLaneInfo
            {
                NodeId = node.Id,
                NodeLane = nodeLane,
                ActiveLanes = currentActiveLanes,
                ConnectorFromLane = connectorFromLane
            });

            // Release lane if this node has no unprocessed children
            // and the parent doesn't have unprocessed siblings
            if (node.BranchName != _mainBranchName)
            {
                var hasUnprocessedChildren = nodeChildren.TryGetValue(node.Id, out var children) &&
                                              children.Any(childId => !processedNodes.Contains(childId));
                if (!hasUnprocessedChildren)
                {
                    activeLanes.Remove(nodeLane);
                }
            }
        }

        return new TimelineLaneLayout
        {
            LaneAssignments = laneAssignments,
            MaxLanes = maxLaneUsed + 1,
            RowInfos = rowInfos
        };
    }

    /// <summary>
    /// Get the next available lane index (lowest unused lane).
    /// </summary>
    private static int GetNextAvailableLane(HashSet<int> activeLanes)
    {
        var lane = 1; // Start from 1 (0 is main)
        while (activeLanes.Contains(lane))
        {
            lane++;
        }
        return lane;
    }

    /// <summary>
    /// Get the lane of the primary parent node, defaulting to lane 0 (main).
    /// </summary>
    private static int? GetParentLane(IGraphNode node, Dictionary<string, int> laneAssignments)
    {
        if (node.ParentIds.Count == 0)
        {
            return 0; // Branch from main
        }

        // Use the first parent's lane
        var primaryParentId = node.ParentIds[0];
        return laneAssignments.TryGetValue(primaryParentId, out var parentLane) ? parentLane : 0;
    }
}
