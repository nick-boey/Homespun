
namespace Homespun.Shared.Models.Gitgraph;

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
        var activeLanes = new HashSet<int> { 0 }; // Lane 0 is always active for main (for allocation)
        var renderingLanes = new HashSet<int> { 0 }; // Lanes that should render vertical lines (direct children pending)
        var rowInfos = new List<RowLaneInfo>();
        var lanesReleasedAfterRow = new List<HashSet<int>>(); // Track lanes released after each row
        var maxLaneUsed = 0;

        // Track lane segments (first/last row indices for each continuous usage of a lane)
        // Key: row index, Value: whether this row is the first in its lane segment
        var isFirstInLaneSegment = new HashSet<int>();
        // We'll compute IsLastRowInLane in a post-processing pass

        // Build lookups for parent-child relationships
        var nodeChildren = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var nodeToParents = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in nodes)
        {
            nodeToParents[node.Id] = node.ParentIds.ToList();
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

            // Handle section dividers - they just pass through in lane 0
            if (node.NodeType == GraphNodeType.SectionDivider)
            {
                nodeLane = 0;
                laneAssignments[node.Id] = nodeLane;

                // Build the set of active lanes for this row BEFORE releases
                var dividerActiveLanes = new HashSet<int>(activeLanes);
                var dividerRenderingLanes = new HashSet<int>(renderingLanes);
                var dividerReservedLanes = new HashSet<int>(dividerActiveLanes.Except(dividerRenderingLanes));

                rowInfos.Add(new RowLaneInfo
                {
                    NodeId = node.Id,
                    NodeLane = nodeLane,
                    ActiveLanes = dividerActiveLanes,
                    ConnectorFromLane = null,
                    IsFirstRowInLane = false,
                    IsLastRowInLane = false,
                    ReservedLanes = dividerReservedLanes,
                    IsDivider = true
                });

                lanesReleasedAfterRow.Add(new HashSet<int>());
                continue;
            }

            // Handle flat orphan issues (empty ParentIds) - they display in lane 0 with no connections
            if (node.NodeType == GraphNodeType.OrphanIssue && node.ParentIds.Count == 0)
            {
                nodeLane = 0;
                laneAssignments[node.Id] = nodeLane;

                rowInfos.Add(new RowLaneInfo
                {
                    NodeId = node.Id,
                    NodeLane = nodeLane,
                    ActiveLanes = new HashSet<int> { 0 },
                    ConnectorFromLane = null,
                    IsFirstRowInLane = false,
                    IsLastRowInLane = true, // Each flat orphan is isolated
                    ReservedLanes = new HashSet<int>(),
                    IsFlatOrphan = true
                });

                lanesReleasedAfterRow.Add(new HashSet<int>());
                continue;
            }

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
                renderingLanes.Add(nodeLane);
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

            // Track if this is the first row in a new lane segment (non-main lanes only)
            // A node is first in its lane segment if:
            // 1. The lane wasn't in activeLanes before we added it (new or reused lane)
            var rowIndex = rowInfos.Count;
            var isFirstInSegment = nodeLane != 0 && connectorFromLane.HasValue;
            if (isFirstInSegment)
            {
                isFirstInLaneSegment.Add(rowIndex);
            }

            // Build the set of active lanes for this row BEFORE releases
            // This ensures the current node's lane is included for vertical line rendering
            var currentActiveLanes = new HashSet<int>(activeLanes);
            var currentRenderingLanes = new HashSet<int>(renderingLanes);

            // Reserved lanes are allocated but shouldn't render vertical lines
            // (lanes where direct children are all processed but descendants remain)
            var reservedLanes = new HashSet<int>(currentActiveLanes.Except(currentRenderingLanes));

            rowInfos.Add(new RowLaneInfo
            {
                NodeId = node.Id,
                NodeLane = nodeLane,
                ActiveLanes = currentActiveLanes,
                ConnectorFromLane = connectorFromLane,
                // Set IsFirstRowInLane now; IsLastRowInLane will be computed in post-processing
                IsFirstRowInLane = isFirstInSegment,
                IsLastRowInLane = false,
                ReservedLanes = reservedLanes
            });

            // Capture active lanes before release to track which lanes are released
            var lanesBeforeRelease = new HashSet<int>(activeLanes);

            // Release from renderingLanes when direct children are processed (for vertical line rendering)
            ReleaseDirectChildLanes(node.Id, nodeChildren, nodeToParents, processedNodes, laneAssignments, renderingLanes);

            // Release from activeLanes when ALL descendants are processed (for lane allocation)
            PropagateAncestorRelease(node.Id, nodeChildren, nodeToParents, processedNodes, laneAssignments, activeLanes);

            // Track which lanes were released after this row
            var releasedLanes = new HashSet<int>(lanesBeforeRelease.Except(activeLanes));
            lanesReleasedAfterRow.Add(releasedLanes);
        }

        // Post-process: compute IsLastRowInLane and LanesEndingAtThisRow for each row
        // A row is last in its lane segment if:
        // - It's the last row in the list for that lane, OR
        // - The next row with the same lane has IsFirstRowInLane=true (meaning it's a new segment)
        var finalRowInfos = ComputeIsLastRowInLane(rowInfos, lanesReleasedAfterRow);

        return new TimelineLaneLayout
        {
            LaneAssignments = laneAssignments,
            MaxLanes = maxLaneUsed + 1,
            RowInfos = finalRowInfos
        };
    }

    /// <summary>
    /// Compute IsLastRowInLane and LanesEndingAtThisRow for each row by scanning forward.
    /// A row is last in its lane segment if:
    /// - Its lane doesn't appear in any subsequent row's ActiveLanes, OR
    /// - The next occurrence of the lane in a node has IsFirstRowInLane=true (new segment)
    /// Lane 0 (main) never has IsLastRowInLane set.
    ///
    /// LanesEndingAtThisRow is populated from the lanes that were released after processing each row.
    /// This correctly handles lane reuse (where a lane is released then immediately reused).
    /// </summary>
    private static List<RowLaneInfo> ComputeIsLastRowInLane(List<RowLaneInfo> rowInfos, List<HashSet<int>> lanesReleasedAfterRow)
    {
        var result = new List<RowLaneInfo>(rowInfos.Count);

        for (var i = 0; i < rowInfos.Count; i++)
        {
            var row = rowInfos[i];
            var nodeLane = row.NodeLane;

            // Get lanes that were released after this row (these are the lanes ending at this row)
            // For the last row, all non-main active lanes are ending
            HashSet<int> lanesEndingAtThisRow;
            if (i < lanesReleasedAfterRow.Count)
            {
                lanesEndingAtThisRow = new HashSet<int>(lanesReleasedAfterRow[i]);
            }
            else
            {
                // Safety fallback for last row
                lanesEndingAtThisRow = new HashSet<int>(row.ActiveLanes.Where(l => l != 0));
            }

            // Lane 0 (main) never has last flag
            if (nodeLane == 0)
            {
                result.Add(row with { LanesEndingAtThisRow = lanesEndingAtThisRow });
                continue;
            }

            // Check if this lane appears in any subsequent row's ActiveLanes
            var isLastInSegment = true;
            for (var j = i + 1; j < rowInfos.Count; j++)
            {
                var futureRow = rowInfos[j];

                // If a future row has the same node lane and it's the start of a new segment,
                // then the current row is the end of its segment
                if (futureRow.NodeLane == nodeLane)
                {
                    if (futureRow.IsFirstRowInLane)
                    {
                        // Current row is last in its segment, new segment starts at futureRow
                        isLastInSegment = true;
                    }
                    else
                    {
                        // Same lane continues, so current row is not last
                        isLastInSegment = false;
                    }
                    break;
                }

                // If the lane is still active in a future row, it's not last yet
                if (futureRow.ActiveLanes.Contains(nodeLane))
                {
                    isLastInSegment = false;
                    break;
                }
            }

            result.Add(row with { IsLastRowInLane = isLastInSegment, LanesEndingAtThisRow = lanesEndingAtThisRow });
        }

        return result;
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

    /// <summary>
    /// Release lanes when ALL descendants (any generation) are processed.
    /// Uses a queue-based approach to propagate releases up the ancestor chain.
    /// A lane is only released when all of its descendants have been processed,
    /// ensuring vertical lines remain active through entire subtrees.
    /// </summary>
    private void PropagateAncestorRelease(
        string nodeId,
        Dictionary<string, HashSet<string>> nodeChildren,
        Dictionary<string, List<string>> nodeToParents,
        HashSet<string> processedNodes,
        Dictionary<string, int> laneAssignments,
        HashSet<int> activeLanes)
    {
        var nodesToCheck = new Queue<string>();
        nodesToCheck.Enqueue(nodeId);
        var checkedNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (nodesToCheck.Count > 0)
        {
            var currentId = nodesToCheck.Dequeue();
            if (!checkedNodes.Add(currentId)) continue;

            if (!laneAssignments.TryGetValue(currentId, out var lane) || lane == 0)
            {
                // Still check ancestors even if this node is on main
                if (nodeToParents.TryGetValue(currentId, out var parents))
                {
                    foreach (var parentId in parents)
                        nodesToCheck.Enqueue(parentId);
                }
                continue;
            }

            var hasUnprocessedChildren = nodeChildren.TryGetValue(currentId, out var children) &&
                                          children.Any(childId => !processedNodes.Contains(childId));

            if (!hasUnprocessedChildren && activeLanes.Contains(lane))
            {
                activeLanes.Remove(lane);
                // Propagate to ancestors
                if (nodeToParents.TryGetValue(currentId, out var ancestorParents))
                {
                    foreach (var parentId in ancestorParents)
                        nodesToCheck.Enqueue(parentId);
                }
            }
        }
    }

    /// <summary>
    /// Release lanes from rendering when direct children are processed.
    /// Unlike PropagateAncestorRelease, this only checks direct parents (not the full ancestor chain).
    /// This determines which lanes should render vertical lines - only lanes with unprocessed direct children.
    /// </summary>
    private void ReleaseDirectChildLanes(
        string nodeId,
        Dictionary<string, HashSet<string>> nodeChildren,
        Dictionary<string, List<string>> nodeToParents,
        HashSet<string> processedNodes,
        Dictionary<string, int> laneAssignments,
        HashSet<int> renderingLanes)
    {
        // First, check if the just-processed node's lane should stop rendering
        // (node has no children, or all direct children are processed)
        if (laneAssignments.TryGetValue(nodeId, out var nodeLane) && nodeLane != 0)
        {
            var nodeHasUnprocessedChildren = nodeChildren.TryGetValue(nodeId, out var children) &&
                                              children.Any(childId => !processedNodes.Contains(childId));

            if (!nodeHasUnprocessedChildren && renderingLanes.Contains(nodeLane))
            {
                renderingLanes.Remove(nodeLane);
            }
        }

        // Then, check the direct parents of the just-processed node
        if (!nodeToParents.TryGetValue(nodeId, out var parents))
        {
            return;
        }

        foreach (var parentId in parents)
        {
            // Skip if no lane assigned or if it's lane 0 (main branch - always renders)
            if (!laneAssignments.TryGetValue(parentId, out var lane) || lane == 0)
            {
                continue;
            }

            // Check if all of this parent's direct children are now processed
            if (!nodeChildren.TryGetValue(parentId, out var parentChildren))
            {
                continue;
            }

            var allDirectChildrenProcessed = parentChildren.All(childId => processedNodes.Contains(childId));

            if (allDirectChildrenProcessed && renderingLanes.Contains(lane))
            {
                // All direct children processed - stop rendering this parent's lane
                // Do NOT propagate to grandparents
                renderingLanes.Remove(lane);
            }
        }
    }
}
