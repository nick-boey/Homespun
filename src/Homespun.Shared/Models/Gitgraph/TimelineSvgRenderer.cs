using System.Globalization;
using System.Text;

namespace Homespun.Shared.Models.Gitgraph;

/// <summary>
/// Helper class for generating SVG paths and elements for task graph rendering.
/// </summary>
public static class TimelineSvgRenderer
{
    public const int LaneWidth = 24;
    public const int RowHeight = 40;
    public const int NodeRadius = 6;
    public const double LineStrokeWidth = 2;

    public static int CalculateSvgWidth(int maxLanes)
    {
        return LaneWidth * Math.Max(maxLanes, 1) + LaneWidth / 2;
    }

    /// <summary>
    /// Maps a priority value (0-4) to a color for the task graph lane 0 vertical line.
    /// P0 (critical) = Red, P1 = Orange, P2 = Yellow, P3 = Green, P4 (low) = Blue.
    /// Null or invalid priorities return grey.
    /// </summary>
    public static string GetPriorityColor(int? priority) => priority switch
    {
        0 => "#ef4444",  // P0: Red (critical)
        1 => "#f97316",  // P1: Orange
        2 => "#eab308",  // P2: Yellow
        3 => "#22c55e",  // P3: Green
        4 => "#3b82f6",  // P4: Blue (low)
        _ => "#6b7280"   // Default: Grey (no priority or invalid)
    };

    public static int GetLaneCenterX(int laneIndex)
    {
        return LaneWidth / 2 + laneIndex * LaneWidth;
    }

    public static int GetRowCenterY()
    {
        return RowHeight / 2;
    }

    public static string GenerateDividerRowSvg(
        int maxLanes,
        IReadOnlyDictionary<int, string>? laneColors = null)
    {
        var width = CalculateSvgWidth(maxLanes);
        var sb = new StringBuilder();
        sb.Append($"<svg width=\"{width}\" height=\"{RowHeight}\" xmlns=\"http://www.w3.org/2000/svg\">");
        var lineColor = laneColors?.GetValueOrDefault(0) ?? "#6b7280";
        var x = GetLaneCenterX(0);
        var linePath = $"M {x} 0 L {x} {RowHeight}";
        sb.Append(
            $"<path d=\"{linePath}\" stroke=\"{EscapeAttribute(lineColor)}\" stroke-width=\"{LineStrokeWidth.ToString(CultureInfo.InvariantCulture)}\" fill=\"none\" />");
        sb.Append("</svg>");
        return sb.ToString();
    }

    /// <summary>
    /// Generates a circle-based node for the task graph.
    /// Hollow circles indicate issues without descriptions, solid circles indicate issues with descriptions.
    /// </summary>
    /// <param name="hasHiddenParent">If true, shows a faded continuation indicator</param>
    /// <param name="hiddenParentIsSeriesMode">If true, shows dots below (series), otherwise to the right (parallel)</param>
    public static string GenerateTaskGraphCircleSvg(
        int nodeLane, int? parentLane, bool isFirstChild, int maxLanes,
        string nodeColor, bool isOutlineOnly, bool isActionable,
        bool drawTopLine = false, bool drawBottomLine = false, bool isSeriesChild = false,
        int? seriesConnectorFromLane = null,
        bool drawLane0Connector = false, bool isLastLane0Connector = false, bool drawLane0PassThrough = false,
string? lane0Color = null,
        bool hasHiddenParent = false, bool hiddenParentIsSeriesMode = false)
    {
        var width = CalculateSvgWidth(maxLanes);
        var sb = new StringBuilder();
        sb.Append($"<svg width=\"{width}\" height=\"{RowHeight}\" xmlns=\"http://www.w3.org/2000/svg\">");

        var cx = GetLaneCenterX(nodeLane);
        var cy = GetRowCenterY();
        var effectiveLane0Color = lane0Color ?? "#6b7280";

        // Lane 0 merged-PR connector: vertical line at lane 0 with optional branch to this node
        if (drawLane0PassThrough)
        {
            var lane0X = GetLaneCenterX(0);
            sb.Append(
                $"<path d=\"M {lane0X} 0 L {lane0X} {RowHeight}\" stroke=\"{effectiveLane0Color}\" stroke-width=\"{LineStrokeWidth.ToString(CultureInfo.InvariantCulture)}\" fill=\"none\" />");
        }
        else if (drawLane0Connector)
        {
            var lane0X = GetLaneCenterX(0);
            var nodeEdgeX = cx - NodeRadius - 2;
            var r = NodeRadius;

            if (isLastLane0Connector)
            {
                // Last connector: vertical from top to junction, arc, horizontal to node
                sb.Append(
                    $"<path d=\"M {lane0X} 0 L {lane0X} {cy - r} A {r} {r} 0 0 0 {lane0X + r} {cy} L {nodeEdgeX} {cy}\" stroke=\"{effectiveLane0Color}\" stroke-width=\"{LineStrokeWidth.ToString(CultureInfo.InvariantCulture)}\" fill=\"none\" />");
            }
            else
            {
                // Non-last connector: full vertical at lane 0 + horizontal branch to node
                sb.Append(
                    $"<path d=\"M {lane0X} 0 L {lane0X} {RowHeight}\" stroke=\"{effectiveLane0Color}\" stroke-width=\"{LineStrokeWidth.ToString(CultureInfo.InvariantCulture)}\" fill=\"none\" />");
                sb.Append(
                    $"<path d=\"M {lane0X} {cy} L {nodeEdgeX} {cy}\" stroke=\"{effectiveLane0Color}\" stroke-width=\"{LineStrokeWidth.ToString(CultureInfo.InvariantCulture)}\" fill=\"none\" />");
            }
        }

        if (!isSeriesChild && parentLane.HasValue && parentLane.Value > nodeLane)
        {
            var px = GetLaneCenterX(parentLane.Value);
            var startX = cx + NodeRadius + 2;

            // Junction at parent lane
            if (isFirstChild)
            {
                // Merged horizontal + arc elbow + vertical down
                var r = NodeRadius;
                sb.Append(
                    $"<path d=\"M {startX} {cy} L {px - r} {cy} A {r} {r} 0 0 1 {px} {cy + r} L {px} {RowHeight}\" stroke=\"{EscapeAttribute(nodeColor)}\" stroke-width=\"{LineStrokeWidth.ToString(CultureInfo.InvariantCulture)}\" fill=\"none\" />");
            }
            else
            {
                // Horizontal line from circle right edge to parent lane center
                sb.Append(
                    $"<path d=\"M {startX} {cy} L {px} {cy}\" stroke=\"{EscapeAttribute(nodeColor)}\" stroke-width=\"{LineStrokeWidth.ToString(CultureInfo.InvariantCulture)}\" fill=\"none\" />");
                // Full-height vertical
                sb.Append(
                    $"<path d=\"M {px} 0 L {px} {RowHeight}\" stroke=\"{EscapeAttribute(nodeColor)}\" stroke-width=\"{LineStrokeWidth.ToString(CultureInfo.InvariantCulture)}\" fill=\"none\" />");
            }
        }

        // L-shaped connector from series children's lane to this node (parent receiving series children)
        if (seriesConnectorFromLane.HasValue)
        {
            var fromX = GetLaneCenterX(seriesConnectorFromLane.Value);
            var nodeEdgeX = cx - NodeRadius - 2;
            var r = NodeRadius;
            sb.Append(
                $"<path d=\"M {fromX} 0 L {fromX} {cy - r} A {r} {r} 0 0 0 {fromX + r} {cy} L {nodeEdgeX} {cy}\" stroke=\"{EscapeAttribute(nodeColor)}\" stroke-width=\"{LineStrokeWidth.ToString(CultureInfo.InvariantCulture)}\" fill=\"none\" />");
        }

        if (drawTopLine)
        {
            var topLineEndY = cy - NodeRadius - 2;
            sb.Append(
                $"<path d=\"M {cx} 0 L {cx} {topLineEndY}\" stroke=\"{EscapeAttribute(nodeColor)}\" stroke-width=\"{LineStrokeWidth.ToString(CultureInfo.InvariantCulture)}\" fill=\"none\" />");
        }

        if (drawBottomLine)
        {
            var bottomLineStartY = cy + NodeRadius + 2;
            sb.Append(
                $"<path d=\"M {cx} {bottomLineStartY} L {cx} {RowHeight}\" stroke=\"{EscapeAttribute(nodeColor)}\" stroke-width=\"{LineStrokeWidth.ToString(CultureInfo.InvariantCulture)}\" fill=\"none\" />");
        }

        if (isActionable)
        {
            // Glow ring for actionable items
            var outerRadius = NodeRadius + 4;
            sb.Append(
                $"<circle cx=\"{cx}\" cy=\"{cy}\" r=\"{outerRadius}\" fill=\"none\" stroke=\"{EscapeAttribute(nodeColor)}\" stroke-width=\"1\" opacity=\"0.4\" />");
        }

        if (isOutlineOnly)
        {
            sb.Append($"<circle cx=\"{cx}\" cy=\"{cy}\" r=\"{NodeRadius}\" fill=\"none\" stroke=\"{EscapeAttribute(nodeColor)}\" stroke-width=\"2\" />");
        }
        else
        {
            sb.Append($"<circle cx=\"{cx}\" cy=\"{cy}\" r=\"{NodeRadius}\" fill=\"{EscapeAttribute(nodeColor)}\" />");
        }

        // Hidden parent continuation indicator (3 small faded dots)
        if (hasHiddenParent)
        {
            sb.Append(GenerateHiddenParentIndicator(nodeLane, nodeColor, hiddenParentIsSeriesMode));
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    /// <summary>
    /// Generates a continuation indicator showing there are hidden parent tasks beyond the current depth.
    /// Renders 3 small faded dots: below the node for series mode, to the right for parallel mode.
    /// </summary>
    private static string GenerateHiddenParentIndicator(int nodeLane, string nodeColor, bool isSeriesMode)
    {
        var cx = GetLaneCenterX(nodeLane);
        var cy = GetRowCenterY();
        var sb = new StringBuilder();

        const int dotRadius = 2;
        const int dotSpacing = 5;
        const double opacity = 0.4;

        if (isSeriesMode)
        {
            // Series: dots below the node (vertical arrangement)
            var startY = cy + NodeRadius + 6;
            for (var i = 0; i < 3; i++)
            {
                var dotY = startY + i * dotSpacing;
                sb.Append($"<circle cx=\"{cx}\" cy=\"{dotY}\" r=\"{dotRadius}\" fill=\"{EscapeAttribute(nodeColor)}\" opacity=\"{opacity.ToString(CultureInfo.InvariantCulture)}\" />");
            }
        }
        else
        {
            // Parallel: dots to the right of the node (horizontal arrangement)
            var startX = cx + NodeRadius + 6;
            for (var i = 0; i < 3; i++)
            {
                var dotX = startX + i * dotSpacing;
                sb.Append($"<circle cx=\"{dotX}\" cy=\"{cy}\" r=\"{dotRadius}\" fill=\"{EscapeAttribute(nodeColor)}\" opacity=\"{opacity.ToString(CultureInfo.InvariantCulture)}\" />");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates a load more button node for the task graph.
    /// </summary>
    public static string GenerateTaskGraphLoadMoreSvg(int maxLanes)
    {
        var width = CalculateSvgWidth(maxLanes);
        var sb = new StringBuilder();
        sb.Append($"<svg width=\"{width}\" height=\"{RowHeight}\" xmlns=\"http://www.w3.org/2000/svg\">");

        var cx = GetLaneCenterX(0);
        var cy = GetRowCenterY();
        var r = NodeRadius + 2;

        // Draw vertical line below the load more button
        var bottomLineStartY = cy + r + 2;
        sb.Append(
            $"<path d=\"M {cx} {bottomLineStartY} L {cx} {RowHeight}\" stroke=\"#6b7280\" stroke-width=\"{LineStrokeWidth.ToString(CultureInfo.InvariantCulture)}\" fill=\"none\" />");

        // Draw the load more button
        sb.Append(
            $"<circle cx=\"{cx}\" cy=\"{cy}\" r=\"{r}\" fill=\"#51A5C1\" stroke=\"white\" stroke-width=\"2\" />");
        sb.Append(
            $"<text x=\"{cx}\" y=\"{cy}\" text-anchor=\"middle\" dominant-baseline=\"central\" fill=\"white\" font-size=\"14\" font-weight=\"bold\">+</text>");

        sb.Append("</svg>");
        return sb.ToString();
    }

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
