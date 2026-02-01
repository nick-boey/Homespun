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
            // Only draw to the center, not the full height
            return $"M {x} 0 L {x} {centerY}";
        }

        // Full vertical line through the row (pass-through lane continues)
        return $"M {x} 0 L {x} {RowHeight}";
    }

    /// <summary>
    /// Generate an SVG path for a connector from one lane to another.
    /// Creates an L-shaped bend connecting to the side of the node at mid-height.
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

        // L-shaped connector: vertical from top to middle, then horizontal to node side
        return $"M {fromX} 0 L {fromX} {centerY} L {nodeEdgeX} {centerY}";
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
        IReadOnlySet<int>? lanesEndingThisRow = null)
    {
        var width = CalculateSvgWidth(maxLanes);
        var sb = new StringBuilder();

        sb.Append($"<svg width=\"{width}\" height=\"{RowHeight}\" xmlns=\"http://www.w3.org/2000/svg\">");

        // Draw vertical lane lines for all active lanes
        foreach (var lane in activeLanes.OrderBy(l => l))
        {
            var hasNode = lane == nodeLane;
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

        // Draw connector if coming from a different lane
        if (connectorFromLane.HasValue && connectorFromLane.Value != nodeLane)
        {
            var connectorPath = GenerateConnector(connectorFromLane.Value, nodeLane);
            sb.Append($"<path d=\"{connectorPath}\" stroke=\"{EscapeAttribute(nodeColor)}\" stroke-width=\"{LineStrokeWidth.ToString(CultureInfo.InvariantCulture)}\" fill=\"none\" />");
        }

        // Draw the node
        if (isLoadMore)
        {
            sb.Append(GenerateLoadMoreNode(nodeLane, nodeColor));
        }
        else if (isIssue)
        {
            sb.Append(GenerateDiamondNode(nodeLane, nodeColor));
        }
        else
        {
            sb.Append(GenerateCircleNode(nodeLane, nodeColor));
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
