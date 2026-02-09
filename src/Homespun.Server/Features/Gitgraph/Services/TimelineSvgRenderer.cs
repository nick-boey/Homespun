using System.Globalization;
using System.Text;

namespace Homespun.Features.Gitgraph.Services;

/// <summary>
/// Helper class for generating SVG paths and elements for timeline rendering.
/// </summary>
public static class TimelineSvgRenderer
{
    /// <summary>
    /// Width of each lane in pixels.
    /// </summary>
    public const int LaneWidth = 24;

    /// <summary>
    /// Height of each row in pixels.
    /// </summary>
    public const int RowHeight = 40;

    /// <summary>
    /// Radius of node circles (PRs).
    /// </summary>
    public const int NodeRadius = 6;

    /// <summary>
    /// Size of diamond nodes (issues) - half-width/height.
    /// </summary>
    public const int DiamondSize = 7;

    /// <summary>
    /// Stroke width for lane lines.
    /// </summary>
    public const double LineStrokeWidth = 2;

    /// <summary>
    /// Calculate the SVG width needed for the given number of lanes.
    /// </summary>
    public static int CalculateSvgWidth(int maxLanes)
    {
        return LaneWidth * Math.Max(maxLanes, 1) + LaneWidth / 2;
    }

    /// <summary>
    /// Get the X coordinate for the center of a lane.
    /// </summary>
    public static int GetLaneCenterX(int laneIndex)
    {
        return LaneWidth / 2 + laneIndex * LaneWidth;
    }

    /// <summary>
    /// Get the Y coordinate for the center of a row.
    /// </summary>
    public static int GetRowCenterY()
    {
        return RowHeight / 2;
    }

    /// <summary>
    /// Generate an SVG path for a vertical lane line segment.
    /// </summary>
    /// <param name="laneIndex">The lane index.</param>
    /// <param name="hasNodeInLane">True if this lane has a node in this row.</param>
    /// <param name="drawTop">True to draw the top segment (above the node). Default true.</param>
    /// <param name="drawBottom">True to draw the bottom segment (below the node). Default true.</param>
    /// <param name="isPassThroughEnding">True if this is a pass-through lane that is ending at this row.</param>
    public static string GenerateVerticalLine(int laneIndex, bool hasNodeInLane, bool drawTop = true, bool drawBottom = true, bool isPassThroughEnding = false)
    {
        var x = GetLaneCenterX(laneIndex);
        var centerY = GetRowCenterY();

        if (hasNodeInLane)
        {
            // Draw line segments above and/or below the node based on flags
            var segments = new List<string>();
            if (drawTop)
            {
                segments.Add($"M {x} 0 L {x} {centerY - NodeRadius - 2}");
            }
            if (drawBottom)
            {
                segments.Add($"M {x} {centerY + NodeRadius + 2} L {x} {RowHeight}");
            }
            return string.Join(" ", segments);
        }

        // Pass-through lane - check if it's ending at this row
        if (isPassThroughEnding)
        {
            // Stop at top of arc for clean connection with connector
            var arcTopY = centerY - ConnectorArcRadius;
            return $"M {x} 0 L {x} {arcTopY}";
        }

        // Full vertical line through the row (pass-through lane continues)
        return $"M {x} 0 L {x} {RowHeight}";
    }

    /// <summary>
    /// Radius of the arc at the connector elbow.
    /// About half the vertical leg length for a subtle curve.
    /// </summary>
    public const int ConnectorArcRadius = 10;

    /// <summary>
    /// Generate an SVG path for a connector from one lane to another.
    /// Creates an L-shaped bend with a rounded corner connecting to the side of the node at mid-height.
    /// </summary>
    /// <param name="fromLane">Source lane index.</param>
    /// <param name="toLane">Target lane index.</param>
    public static string GenerateConnector(int fromLane, int toLane)
    {
        var fromX = GetLaneCenterX(fromLane);
        var toX = GetLaneCenterX(toLane);
        var centerY = GetRowCenterY();

        // Determine the horizontal endpoint based on node shape
        // Connect to the left side of the node (diamond or circle) at mid-height
        var nodeEdgeX = toX - DiamondSize - 2; // Small gap from the node edge

        // L-shaped connector with rounded corner:
        // - Vertical from top to (centerY - arcRadius)
        // - Quarter-circle arc turning from down to right
        // - Horizontal to the node edge
        var arcRadius = ConnectorArcRadius;
        var arcStartY = centerY - arcRadius;
        var arcEndX = fromX + arcRadius;

        // SVG arc: A rx ry x-rotation large-arc-flag sweep-flag x y
        // sweep-flag=0 for counter-clockwise (arc bulges inward, down and to the right)
        return $"M {fromX} 0 L {fromX} {arcStartY} A {arcRadius} {arcRadius} 0 0 0 {arcEndX} {centerY} L {nodeEdgeX} {centerY}";
    }

    /// <summary>
    /// Generate SVG for a circle node (used for PRs).
    /// </summary>
    /// <param name="laneIndex">Lane where the node is positioned.</param>
    /// <param name="color">Fill color for the node.</param>
    public static string GenerateCircleNode(int laneIndex, string color)
    {
        var cx = GetLaneCenterX(laneIndex);
        var cy = GetRowCenterY();
        return $"<circle cx=\"{cx}\" cy=\"{cy}\" r=\"{NodeRadius}\" fill=\"{EscapeAttribute(color)}\" />";
    }

    /// <summary>
    /// Generate SVG for a diamond node (used for issues).
    /// </summary>
    /// <param name="laneIndex">Lane where the node is positioned.</param>
    /// <param name="color">Fill color for the node.</param>
    public static string GenerateDiamondNode(int laneIndex, string color)
    {
        var cx = GetLaneCenterX(laneIndex);
        var cy = GetRowCenterY();
        var s = DiamondSize;

        // Diamond path: top, right, bottom, left
        var path = $"M {cx} {cy - s} L {cx + s} {cy} L {cx} {cy + s} L {cx - s} {cy} Z";
        return $"<path d=\"{path}\" fill=\"{EscapeAttribute(color)}\" />";
    }

    /// <summary>
    /// Generate SVG for an outline-only diamond node (used for issues without description).
    /// </summary>
    /// <param name="laneIndex">Lane where the node is positioned.</param>
    /// <param name="color">Stroke color for the outline.</param>
    public static string GenerateDiamondNodeOutline(int laneIndex, string color)
    {
        var cx = GetLaneCenterX(laneIndex);
        var cy = GetRowCenterY();
        var s = DiamondSize;

        // Diamond path: top, right, bottom, left
        var path = $"M {cx} {cy - s} L {cx + s} {cy} L {cx} {cy + s} L {cx - s} {cy} Z";
        return $"<path d=\"{path}\" fill=\"none\" stroke=\"{EscapeAttribute(color)}\" stroke-width=\"2\" />";
    }

    /// <summary>
    /// Generate SVG for an error cross overlay inside a diamond node.
    /// Renders a small X/cross at the center of the diamond.
    /// </summary>
    /// <param name="laneIndex">Lane where the node is positioned.</param>
    /// <param name="color">Color for the cross lines.</param>
    public static string GenerateErrorCross(int laneIndex, string color)
    {
        var cx = GetLaneCenterX(laneIndex);
        var cy = GetRowCenterY();
        var crossSize = 3; // Half-size of the cross

        var sb = new StringBuilder();
        // Diagonal line from top-left to bottom-right
        sb.Append($"<line x1=\"{cx - crossSize}\" y1=\"{cy - crossSize}\" x2=\"{cx + crossSize}\" y2=\"{cy + crossSize}\" stroke=\"{EscapeAttribute(color)}\" stroke-width=\"2\" />");
        // Diagonal line from top-right to bottom-left
        sb.Append($"<line x1=\"{cx + crossSize}\" y1=\"{cy - crossSize}\" x2=\"{cx - crossSize}\" y2=\"{cy + crossSize}\" stroke=\"{EscapeAttribute(color)}\" stroke-width=\"2\" />");
        return sb.ToString();
    }

    /// <summary>
    /// Generate SVG for a "load more" button node.
    /// </summary>
    /// <param name="laneIndex">Lane where the node is positioned.</param>
    /// <param name="color">Color for the node.</param>
    public static string GenerateLoadMoreNode(int laneIndex, string color)
    {
        var cx = GetLaneCenterX(laneIndex);
        var cy = GetRowCenterY();
        var r = NodeRadius + 2;

        var sb = new StringBuilder();
        sb.Append($"<circle cx=\"{cx}\" cy=\"{cy}\" r=\"{r}\" fill=\"{EscapeAttribute(color)}\" stroke=\"white\" stroke-width=\"2\" />");
        sb.Append($"<text x=\"{cx}\" y=\"{cy}\" text-anchor=\"middle\" dominant-baseline=\"central\" fill=\"white\" font-size=\"14\" font-weight=\"bold\">+</text>");
        return sb.ToString();
    }

    /// <summary>
    /// Generate the complete SVG element for a row's graph cell.
    /// </summary>
    /// <param name="nodeLane">Lane containing the node.</param>
    /// <param name="activeLanes">Set of lanes with active lines.</param>
    /// <param name="connectorFromLane">Source lane for connector, if any.</param>
    /// <param name="maxLanes">Maximum number of lanes (determines width).</param>
    /// <param name="nodeColor">Color for the node.</param>
    /// <param name="isIssue">True for diamond shape, false for circle.</param>
    /// <param name="isLoadMore">True for load more button style.</param>
    /// <param name="laneColors">Colors for each lane line.</param>
    /// <param name="isFirstRowInLane">True if this is the first row where nodeLane appears.</param>
    /// <param name="isLastRowInLane">True if this is the last row where nodeLane is active.</param>
    /// <param name="lanesEndingThisRow">Set of lane indices that should not draw a bottom segment (pass-through lanes ending at this row).</param>
    /// <param name="reservedLanes">Set of lane indices that are allocated but should not render vertical lines.</param>
    /// <param name="isOutlineOnly">True to render outline-only diamond (no description), false for filled. Only applies when isIssue is true.</param>
    /// <param name="showErrorCross">True to render an error cross overlay inside the diamond. Only applies when isIssue is true.</param>
    public static string GenerateRowSvg(
        int nodeLane,
        IReadOnlySet<int> activeLanes,
        int? connectorFromLane,
        int maxLanes,
        string nodeColor,
        bool isIssue,
        bool isLoadMore,
        IReadOnlyDictionary<int, string>? laneColors = null,
        bool isFirstRowInLane = false,
        bool isLastRowInLane = false,
        IReadOnlySet<int>? lanesEndingThisRow = null,
        IReadOnlySet<int>? reservedLanes = null,
        bool isOutlineOnly = false,
        bool showErrorCross = false)
    {
        var width = CalculateSvgWidth(maxLanes);
        var sb = new StringBuilder();

        sb.Append($"<svg width=\"{width}\" height=\"{RowHeight}\" xmlns=\"http://www.w3.org/2000/svg\">");

        // Draw connector FIRST (behind vertical lines) if coming from a different lane
        if (connectorFromLane.HasValue && connectorFromLane.Value != nodeLane)
        {
            var connectorPath = GenerateConnector(connectorFromLane.Value, nodeLane);
            sb.Append($"<path d=\"{connectorPath}\" stroke=\"{EscapeAttribute(nodeColor)}\" stroke-width=\"{LineStrokeWidth.ToString(CultureInfo.InvariantCulture)}\" fill=\"none\" />");
        }

        // Draw vertical lane lines for all active lanes (on top of connector)
        foreach (var lane in activeLanes.OrderBy(l => l))
        {
            var hasNode = lane == nodeLane;

            // Skip reserved lanes (allocated but not rendering) unless this lane has the node
            // Reserved lanes don't render vertical lines because their direct children are all processed
            if (!hasNode && (reservedLanes?.Contains(lane) ?? false))
            {
                continue;
            }

            var lineColor = laneColors?.GetValueOrDefault(lane) ?? "#6b7280";

            // For the node's lane, determine which segments to draw
            bool drawTop = true;
            bool drawBottom = true;
            bool isPassThroughEnding = false;

            if (hasNode)
            {
                // Check if there's a valid connector from a different lane
                var hasValidConnector = connectorFromLane.HasValue && connectorFromLane.Value != lane;

                // Don't draw top segment if:
                // - This is the first row in the lane (nothing to connect to above), OR
                // - There's a valid connector coming in from another lane (connector provides the visual connection)
                drawTop = !isFirstRowInLane && !hasValidConnector;

                // Don't draw bottom segment if this is the last row in the lane
                drawBottom = !isLastRowInLane;
            }
            else
            {
                // Pass-through lane: check if it's ending at this row
                isPassThroughEnding = lanesEndingThisRow?.Contains(lane) ?? false;
            }

            var linePath = GenerateVerticalLine(lane, hasNode, drawTop, drawBottom, isPassThroughEnding);
            if (!string.IsNullOrEmpty(linePath))
            {
                sb.Append($"<path d=\"{linePath}\" stroke=\"{EscapeAttribute(lineColor)}\" stroke-width=\"{LineStrokeWidth.ToString(CultureInfo.InvariantCulture)}\" fill=\"none\" />");
            }
        }

        // Draw the node
        if (isLoadMore)
        {
            sb.Append(GenerateLoadMoreNode(nodeLane, nodeColor));
        }
        else if (isIssue)
        {
            sb.Append(isOutlineOnly
                ? GenerateDiamondNodeOutline(nodeLane, nodeColor)
                : GenerateDiamondNode(nodeLane, nodeColor));

            if (showErrorCross)
            {
                // White cross on filled diamond for visibility
                sb.Append(GenerateErrorCross(nodeLane, "white"));
            }
        }
        else
        {
            sb.Append(GenerateCircleNode(nodeLane, nodeColor));
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    /// <summary>
    /// Generate the SVG element for a divider row (just a vertical line in lane 0).
    /// </summary>
    /// <param name="maxLanes">Maximum number of lanes (determines width).</param>
    /// <param name="laneColors">Colors for each lane line.</param>
    public static string GenerateDividerRowSvg(
        int maxLanes,
        IReadOnlyDictionary<int, string>? laneColors = null)
    {
        var width = CalculateSvgWidth(maxLanes);
        var sb = new StringBuilder();

        sb.Append($"<svg width=\"{width}\" height=\"{RowHeight}\" xmlns=\"http://www.w3.org/2000/svg\">");

        // Draw vertical line in lane 0 only
        var lineColor = laneColors?.GetValueOrDefault(0) ?? "#6b7280";
        var x = GetLaneCenterX(0);
        var linePath = $"M {x} 0 L {x} {RowHeight}";
        sb.Append($"<path d=\"{linePath}\" stroke=\"{EscapeAttribute(lineColor)}\" stroke-width=\"{LineStrokeWidth.ToString(CultureInfo.InvariantCulture)}\" fill=\"none\" />");

        sb.Append("</svg>");
        return sb.ToString();
    }

    /// <summary>
    /// Generate SVG for a flat orphan issue row (diamond node in lane 0 with no connecting lines).
    /// </summary>
    /// <param name="maxLanes">Maximum number of lanes (determines width).</param>
    /// <param name="nodeColor">Color for the diamond node.</param>
    /// <param name="isOutlineOnly">True to render outline-only diamond (no description), false for filled.</param>
    /// <param name="showErrorCross">True to render an error cross overlay inside the diamond.</param>
    public static string GenerateFlatOrphanRowSvg(
        int maxLanes,
        string nodeColor,
        bool isOutlineOnly = false,
        bool showErrorCross = false)
    {
        var width = CalculateSvgWidth(maxLanes);
        var sb = new StringBuilder();

        sb.Append($"<svg width=\"{width}\" height=\"{RowHeight}\" xmlns=\"http://www.w3.org/2000/svg\">");

        // Draw diamond node in lane 0 only - no vertical lines
        sb.Append(isOutlineOnly
            ? GenerateDiamondNodeOutline(0, nodeColor)
            : GenerateDiamondNode(0, nodeColor));

        if (showErrorCross)
        {
            sb.Append(GenerateErrorCross(0, "white"));
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    /// <summary>
    /// Escape a string for use in an XML/SVG attribute.
    /// </summary>
    private static string EscapeAttribute(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("\"", "&quot;")
            .Replace("'", "&apos;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");
    }
}
