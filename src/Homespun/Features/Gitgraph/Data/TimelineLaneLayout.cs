namespace Homespun.Features.Gitgraph.Data;

/// <summary>
/// Information about a single row's lane state for rendering.
/// </summary>
public record RowLaneInfo
{
    /// <summary>
    /// The lane index where the node in this row is positioned.
    /// </summary>
    public required int NodeLane { get; init; }

    /// <summary>
    /// Set of all lanes that have active lines passing through this row.
    /// </summary>
    public required IReadOnlySet<int> ActiveLanes { get; init; }

    /// <summary>
    /// If this node connects from a different lane (branch start/switch),
    /// this is the source lane index. Null if continuing in same lane.
    /// </summary>
    public int? ConnectorFromLane { get; init; }

    /// <summary>
    /// The node ID for this row.
    /// </summary>
    public required string NodeId { get; init; }

    /// <summary>
    /// True if this is the first row where NodeLane appears (no line above).
    /// Always false for lane 0 (main branch).
    /// </summary>
    public bool IsFirstRowInLane { get; init; }

    /// <summary>
    /// True if this is the last row where NodeLane is active (no line below).
    /// Always false for lane 0 (main branch).
    /// </summary>
    public bool IsLastRowInLane { get; init; }

    /// <summary>
    /// Set of lane indices that should NOT draw a bottom segment in this row.
    /// This includes pass-through lanes where a subtree has completed and the lane is being released.
    /// </summary>
    public IReadOnlySet<int> LanesEndingAtThisRow { get; init; } = new HashSet<int>();

    /// <summary>
    /// Set of lane indices that are reserved (allocated for layout) but should NOT render vertical lines.
    /// These are lanes where the node's direct children have all been processed, but descendants remain.
    /// The lane stays allocated to prevent reuse, but no visual line should be drawn.
    /// </summary>
    public IReadOnlySet<int> ReservedLanes { get; init; } = new HashSet<int>();
}

/// <summary>
/// Complete layout information for rendering the timeline graph.
/// </summary>
public record TimelineLaneLayout
{
    /// <summary>
    /// Map from node ID to assigned lane index.
    /// </summary>
    public required IReadOnlyDictionary<string, int> LaneAssignments { get; init; }

    /// <summary>
    /// Maximum number of lanes used (determines SVG width).
    /// </summary>
    public required int MaxLanes { get; init; }

    /// <summary>
    /// Per-row rendering information in display order.
    /// </summary>
    public required IReadOnlyList<RowLaneInfo> RowInfos { get; init; }
}
