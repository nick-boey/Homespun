using System.Globalization;
using System.Text;

namespace Homespun.Shared.Models.Gitgraph;

/// <summary>
/// Helper class for generating SVG paths and elements for timeline rendering.
/// </summary>
public static class TimelineSvgRenderer
{
    public const int LaneWidth = 24;
    public const int RowHeight = 40;
    public const int NodeRadius = 6;
    public const int DiamondSize = 7;
    public const double LineStrokeWidth = 2;
    public const int ConnectorArcRadius = 10;

    public static int CalculateSvgWidth(int maxLanes)
    {
        return LaneWidth * Math.Max(maxLanes, 1) + LaneWidth / 2;
    }

    public static int GetLaneCenterX(int laneIndex)
    {
        return LaneWidth / 2 + laneIndex * LaneWidth;
    }

    public static int GetRowCenterY()
    {
        return RowHeight / 2;
    }

    public static string GenerateVerticalLine(int laneIndex, bool hasNodeInLane, bool drawTop = true,
        bool drawBottom = true, bool isPassThroughEnding = false)
    {
        var x = GetLaneCenterX(laneIndex);
        var centerY = GetRowCenterY();

        if (hasNodeInLane)
        {
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

        if (isPassThroughEnding)
        {
            var arcTopY = centerY - ConnectorArcRadius;
            return $"M {x} 0 L {x} {arcTopY}";
        }

        return $"M {x} 0 L {x} {RowHeight}";
    }

    public static string GenerateConnector(int fromLane, int toLane)
    {
        var fromX = GetLaneCenterX(fromLane);
        var toX = GetLaneCenterX(toLane);
        var centerY = GetRowCenterY();

        var nodeEdgeX = toX - DiamondSize - 2;
        var arcRadius = ConnectorArcRadius;
        var arcStartY = centerY - arcRadius;
        var arcEndX = fromX + arcRadius;

        return
            $"M {fromX} 0 L {fromX} {arcStartY} A {arcRadius} {arcRadius} 0 0 0 {arcEndX} {centerY} L {nodeEdgeX} {centerY}";
    }

    public static string GenerateCircleNode(int laneIndex, string color)
    {
        var cx = GetLaneCenterX(laneIndex);
        var cy = GetRowCenterY();
        return $"<circle cx=\"{cx}\" cy=\"{cy}\" r=\"{NodeRadius}\" fill=\"{EscapeAttribute(color)}\" />";
    }

    public static string GenerateDiamondNode(int laneIndex, string color)
    {
        var cx = GetLaneCenterX(laneIndex);
        var cy = GetRowCenterY();
        var s = DiamondSize;
        var path = $"M {cx} {cy - s} L {cx + s} {cy} L {cx} {cy + s} L {cx - s} {cy} Z";
        return $"<path d=\"{path}\" fill=\"{EscapeAttribute(color)}\" />";
    }

    public static string GenerateDiamondNodeOutline(int laneIndex, string color)
    {
        var cx = GetLaneCenterX(laneIndex);
        var cy = GetRowCenterY();
        var s = DiamondSize;
        var path = $"M {cx} {cy - s} L {cx + s} {cy} L {cx} {cy + s} L {cx - s} {cy} Z";
        return $"<path d=\"{path}\" fill=\"none\" stroke=\"{EscapeAttribute(color)}\" stroke-width=\"2\" />";
    }

    public static string GenerateErrorCross(int laneIndex, string color)
    {
        var cx = GetLaneCenterX(laneIndex);
        var cy = GetRowCenterY();
        var crossSize = 3;
        var sb = new StringBuilder();
        sb.Append(
            $"<line x1=\"{cx - crossSize}\" y1=\"{cy - crossSize}\" x2=\"{cx + crossSize}\" y2=\"{cy + crossSize}\" stroke=\"{EscapeAttribute(color)}\" stroke-width=\"2\" />");
        sb.Append(
            $"<line x1=\"{cx + crossSize}\" y1=\"{cy - crossSize}\" x2=\"{cx - crossSize}\" y2=\"{cy + crossSize}\" stroke=\"{EscapeAttribute(color)}\" stroke-width=\"2\" />");
        return sb.ToString();
    }

    public static string GenerateLoadMoreNode(int laneIndex, string color)
    {
        var cx = GetLaneCenterX(laneIndex);
        var cy = GetRowCenterY();
        var r = NodeRadius + 2;
        var sb = new StringBuilder();
        sb.Append(
            $"<circle cx=\"{cx}\" cy=\"{cy}\" r=\"{r}\" fill=\"{EscapeAttribute(color)}\" stroke=\"white\" stroke-width=\"2\" />");
        sb.Append(
            $"<text x=\"{cx}\" y=\"{cy}\" text-anchor=\"middle\" dominant-baseline=\"central\" fill=\"white\" font-size=\"14\" font-weight=\"bold\">+</text>");
        return sb.ToString();
    }

    /// <summary>
    /// Generates a pulsing glow effect ring around actionable issue nodes.
    /// Actionable items (lane 0 in task graph mode) get a subtle glow to indicate they're ready to work on.
    /// </summary>
    public static string GenerateActionableIndicator(int laneIndex, string color)
    {
        var cx = GetLaneCenterX(laneIndex);
        var cy = GetRowCenterY();
        var outerRadius = DiamondSize + 4;
        // Subtle glow ring around actionable items
        return
            $"<circle cx=\"{cx}\" cy=\"{cy}\" r=\"{outerRadius}\" fill=\"none\" stroke=\"{EscapeAttribute(color)}\" stroke-width=\"1\" opacity=\"0.4\" />";
    }

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
        bool showErrorCross = false,
        bool isActionable = false)
    {
        var width = CalculateSvgWidth(maxLanes);
        var sb = new StringBuilder();
        sb.Append($"<svg width=\"{width}\" height=\"{RowHeight}\" xmlns=\"http://www.w3.org/2000/svg\">");

        if (connectorFromLane.HasValue && connectorFromLane.Value != nodeLane)
        {
            var connectorPath = GenerateConnector(connectorFromLane.Value, nodeLane);
            sb.Append(
                $"<path d=\"{connectorPath}\" stroke=\"{EscapeAttribute(nodeColor)}\" stroke-width=\"{LineStrokeWidth.ToString(CultureInfo.InvariantCulture)}\" fill=\"none\" />");
        }

        foreach (var lane in activeLanes.OrderBy(l => l))
        {
            var hasNode = lane == nodeLane;
            if (!hasNode && (reservedLanes?.Contains(lane) ?? false))
            {
                continue;
            }

            var lineColor = laneColors?.GetValueOrDefault(lane) ?? "#6b7280";
            bool drawTop = true;
            bool drawBottom = true;
            bool isPassThroughEnding = false;

            if (hasNode)
            {
                var hasValidConnector = connectorFromLane.HasValue && connectorFromLane.Value != lane;
                drawTop = !isFirstRowInLane && !hasValidConnector;
                drawBottom = !isLastRowInLane;
            }
            else
            {
                isPassThroughEnding = lanesEndingThisRow?.Contains(lane) ?? false;
            }

            var linePath = GenerateVerticalLine(lane, hasNode, drawTop, drawBottom, isPassThroughEnding);
            if (!string.IsNullOrEmpty(linePath))
            {
                sb.Append(
                    $"<path d=\"{linePath}\" stroke=\"{EscapeAttribute(lineColor)}\" stroke-width=\"{LineStrokeWidth.ToString(CultureInfo.InvariantCulture)}\" fill=\"none\" />");
            }
        }

        if (isLoadMore)
        {
            sb.Append(GenerateLoadMoreNode(nodeLane, nodeColor));
        }
        else if (isIssue)
        {
            // Add actionable indicator ring before the diamond (so it appears behind)
            if (isActionable)
            {
                sb.Append(GenerateActionableIndicator(nodeLane, nodeColor));
            }

            sb.Append(isOutlineOnly
                ? GenerateDiamondNodeOutline(nodeLane, nodeColor)
                : GenerateDiamondNode(nodeLane, nodeColor));
            if (showErrorCross)
            {
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

    public static string GenerateFlatOrphanRowSvg(
        int maxLanes,
        string nodeColor,
        bool isOutlineOnly = false,
        bool showErrorCross = false)
    {
        var width = CalculateSvgWidth(maxLanes);
        var sb = new StringBuilder();
        sb.Append($"<svg width=\"{width}\" height=\"{RowHeight}\" xmlns=\"http://www.w3.org/2000/svg\">");
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

    public static string GenerateTaskGraphIssueSvg(
        int nodeLane, int? parentLane, bool isFirstChild, int maxLanes,
        string nodeColor, bool isOutlineOnly, bool isActionable, double opacity = 1.0,
        bool drawTopLine = false, bool drawBottomLine = false, bool isSeriesChild = false,
        int? seriesConnectorFromLane = null)
    {
        var width = CalculateSvgWidth(maxLanes);
        var sb = new StringBuilder();
        sb.Append($"<svg width=\"{width}\" height=\"{RowHeight}\" xmlns=\"http://www.w3.org/2000/svg\">");

        var cx = GetLaneCenterX(nodeLane);
        var cy = GetRowCenterY();

        if (!isSeriesChild && parentLane.HasValue && parentLane.Value > nodeLane)
        {
            var px = GetLaneCenterX(parentLane.Value);
            var startX = cx + DiamondSize + 2;

            // Horizontal line from diamond right edge to parent lane center
            sb.Append(
                $"<path d=\"M {startX} {cy} L {px} {cy}\" stroke=\"{EscapeAttribute(nodeColor)}\" stroke-width=\"{LineStrokeWidth.ToString(CultureInfo.InvariantCulture)}\" fill=\"none\" />");

            // Junction at parent lane
            if (isFirstChild)
            {
                // Bottom-half vertical: from center to bottom (toward parent below)
                sb.Append(
                    $"<path d=\"M {px} {cy} L {px} {RowHeight}\" stroke=\"{EscapeAttribute(nodeColor)}\" stroke-width=\"{LineStrokeWidth.ToString(CultureInfo.InvariantCulture)}\" fill=\"none\" />");
            }
            else
            {
                // Full-height vertical
                sb.Append(
                    $"<path d=\"M {px} 0 L {px} {RowHeight}\" stroke=\"{EscapeAttribute(nodeColor)}\" stroke-width=\"{LineStrokeWidth.ToString(CultureInfo.InvariantCulture)}\" fill=\"none\" />");
            }
        }

        // L-shaped connector from series children's lane to this node (parent receiving series children)
        if (seriesConnectorFromLane.HasValue)
        {
            var fromX = GetLaneCenterX(seriesConnectorFromLane.Value);
            var nodeEdgeX = cx - DiamondSize - 2;
            sb.Append(
                $"<path d=\"M {fromX} 0 L {fromX} {cy} L {nodeEdgeX} {cy}\" stroke=\"{EscapeAttribute(nodeColor)}\" stroke-width=\"{LineStrokeWidth.ToString(CultureInfo.InvariantCulture)}\" fill=\"none\" />");
        }

        if (drawTopLine)
        {
            var topLineEndY = cy - DiamondSize - 2;
            sb.Append(
                $"<path d=\"M {cx} 0 L {cx} {topLineEndY}\" stroke=\"{EscapeAttribute(nodeColor)}\" stroke-width=\"{LineStrokeWidth.ToString(CultureInfo.InvariantCulture)}\" fill=\"none\" />");
        }

        if (drawBottomLine)
        {
            var bottomLineStartY = cy + DiamondSize + 2;
            sb.Append(
                $"<path d=\"M {cx} {bottomLineStartY} L {cx} {RowHeight}\" stroke=\"{EscapeAttribute(nodeColor)}\" stroke-width=\"{LineStrokeWidth.ToString(CultureInfo.InvariantCulture)}\" fill=\"none\" />");
        }

        if (isActionable)
        {
            sb.Append(GenerateActionableIndicator(nodeLane, nodeColor));
        }

        if (isOutlineOnly)
        {
            sb.Append(GenerateDiamondNodeOutline(nodeLane, nodeColor));
        }
        else
        {
            var diamond = GenerateDiamondNode(nodeLane, nodeColor);
            if (opacity < 1.0)
            {
                // Insert opacity attribute before the closing />
                diamond = diamond.Replace(" />", $" opacity=\"{opacity.ToString(CultureInfo.InvariantCulture)}\" />");
            }
            sb.Append(diamond);
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    public static string GenerateTaskGraphConnectorSvg(int lane, int maxLanes, int height = 16)
    {
        var width = CalculateSvgWidth(maxLanes);
        var x = GetLaneCenterX(lane);
        var sb = new StringBuilder();
        sb.Append($"<svg width=\"{width}\" height=\"{height}\" xmlns=\"http://www.w3.org/2000/svg\">");
        sb.Append(
            $"<path d=\"M {x} 0 L {x} {height}\" stroke=\"#6b7280\" stroke-width=\"{LineStrokeWidth.ToString(CultureInfo.InvariantCulture)}\" fill=\"none\" />");
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