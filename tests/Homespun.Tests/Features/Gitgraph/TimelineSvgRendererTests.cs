namespace Homespun.Tests.Features.Gitgraph;

[TestFixture]
public class TimelineSvgRendererTests
{
    #region Constants Tests

    [Test]
    public void LaneWidth_Is24()
    {
        Assert.That(TimelineSvgRenderer.LaneWidth, Is.EqualTo(24));
    }

    [Test]
    public void RowHeight_Is40()
    {
        Assert.That(TimelineSvgRenderer.RowHeight, Is.EqualTo(40));
    }

    [Test]
    public void NodeRadius_Is6()
    {
        Assert.That(TimelineSvgRenderer.NodeRadius, Is.EqualTo(6));
    }

    #endregion

    #region GetLaneCenterX Tests

    [Test]
    public void GetLaneCenterX_Lane0_Returns12()
    {
        // Lane 0 center = LaneWidth/2 + 0*LaneWidth = 12
        var result = TimelineSvgRenderer.GetLaneCenterX(0);
        Assert.That(result, Is.EqualTo(12));
    }

    [Test]
    public void GetLaneCenterX_Lane1_Returns36()
    {
        // Lane 1 center = LaneWidth/2 + 1*LaneWidth = 12 + 24 = 36
        var result = TimelineSvgRenderer.GetLaneCenterX(1);
        Assert.That(result, Is.EqualTo(36));
    }

    [Test]
    public void GetLaneCenterX_Lane2_Returns60()
    {
        // Lane 2 center = LaneWidth/2 + 2*LaneWidth = 12 + 48 = 60
        var result = TimelineSvgRenderer.GetLaneCenterX(2);
        Assert.That(result, Is.EqualTo(60));
    }

    #endregion

    #region GetRowCenterY Tests

    [Test]
    public void GetRowCenterY_Returns20()
    {
        // Row center = RowHeight/2 = 40/2 = 20
        var result = TimelineSvgRenderer.GetRowCenterY();
        Assert.That(result, Is.EqualTo(20));
    }

    #endregion

    #region CalculateSvgWidth Tests

    [Test]
    public void CalculateSvgWidth_OneLane_Returns36()
    {
        // Width = LaneWidth * maxLanes + LaneWidth/2 = 24*1 + 12 = 36
        var result = TimelineSvgRenderer.CalculateSvgWidth(1);
        Assert.That(result, Is.EqualTo(36));
    }

    [Test]
    public void CalculateSvgWidth_ThreeLanes_Returns84()
    {
        // Width = LaneWidth * 3 + LaneWidth/2 = 72 + 12 = 84
        var result = TimelineSvgRenderer.CalculateSvgWidth(3);
        Assert.That(result, Is.EqualTo(84));
    }

    [Test]
    public void CalculateSvgWidth_ZeroLanes_UsesMininumOfOne()
    {
        // Should treat 0 lanes as minimum of 1
        var result = TimelineSvgRenderer.CalculateSvgWidth(0);
        Assert.That(result, Is.EqualTo(36));
    }

    #endregion

    #region GenerateDividerRowSvg Tests

    [Test]
    public void GenerateDividerRowSvg_GeneratesCorrectSvg()
    {
        var svg = TimelineSvgRenderer.GenerateDividerRowSvg(maxLanes: 2);

        Assert.That(svg, Does.Contain("<svg"));
        Assert.That(svg, Does.Contain("width=\"60\""));  // 2 lanes
        Assert.That(svg, Does.Contain("height=\"40\""));
        Assert.That(svg, Does.Contain("M 12 0 L 12 40")); // Vertical line at lane 0
    }

    [Test]
    public void GenerateDividerRowSvg_WithLaneColors_UsesProvidedColor()
    {
        var laneColors = new Dictionary<int, string> { { 0, "#ff0000" } };
        var svg = TimelineSvgRenderer.GenerateDividerRowSvg(maxLanes: 1, laneColors: laneColors);

        Assert.That(svg, Does.Contain("stroke=\"#ff0000\""));
    }

    #endregion

    #region GenerateTaskGraphCircleSvg Tests

    [Test]
    public void GenerateTaskGraphCircleSvg_NoParent_OnlyCircle()
    {
        var svg = TimelineSvgRenderer.GenerateTaskGraphCircleSvg(
            nodeLane: 0, parentLane: null, isFirstChild: false, maxLanes: 1,
            nodeColor: "#51A5C1", isOutlineOnly: false, isActionable: false);

        Assert.That(svg, Does.Contain("<svg"));
        Assert.That(svg, Does.Contain("<circle"));
        Assert.That(svg, Does.Contain("fill=\"#51A5C1\""));
    }

    [Test]
    public void GenerateTaskGraphCircleSvg_Outline_DrawsStrokeNoFill()
    {
        var svg = TimelineSvgRenderer.GenerateTaskGraphCircleSvg(
            nodeLane: 0, parentLane: null, isFirstChild: false, maxLanes: 1,
            nodeColor: "#51A5C1", isOutlineOnly: true, isActionable: false);

        Assert.That(svg, Does.Contain("fill=\"none\""));
        Assert.That(svg, Does.Contain("stroke=\"#51A5C1\""));
    }

    [Test]
    public void GenerateTaskGraphCircleSvg_Actionable_ContainsGlowRing()
    {
        var svg = TimelineSvgRenderer.GenerateTaskGraphCircleSvg(
            nodeLane: 0, parentLane: null, isFirstChild: false, maxLanes: 1,
            nodeColor: "#51A5C1", isOutlineOnly: true, isActionable: true);

        Assert.That(svg, Does.Contain("opacity=\"0.4\""));
        // Should have two circles - glow ring and node
        Assert.That(CountOccurrences(svg, "<circle"), Is.EqualTo(2));
    }

    [Test]
    public void GenerateTaskGraphCircleSvg_WithParent_FirstChild_DrawsArcPath()
    {
        var svg = TimelineSvgRenderer.GenerateTaskGraphCircleSvg(
            nodeLane: 0, parentLane: 1, isFirstChild: true, maxLanes: 2,
            nodeColor: "#6b7280", isOutlineOnly: true, isActionable: false);

        var cx = TimelineSvgRenderer.GetLaneCenterX(0); // 12
        var px = TimelineSvgRenderer.GetLaneCenterX(1); // 36
        var startX = cx + TimelineSvgRenderer.NodeRadius + 2; // 20
        var r = TimelineSvgRenderer.NodeRadius; // 6

        // Merged path: horizontal → arc elbow → vertical down
        Assert.That(svg, Does.Contain($"M {startX} 20 L {px - r} 20 A {r} {r} 0 0 1 {px} {20 + r} L {px} {TimelineSvgRenderer.RowHeight}"));
    }

    [Test]
    public void GenerateTaskGraphCircleSvg_WithParent_SubsequentChild_FullHeightJunction()
    {
        var svg = TimelineSvgRenderer.GenerateTaskGraphCircleSvg(
            nodeLane: 0, parentLane: 1, isFirstChild: false, maxLanes: 2,
            nodeColor: "#6b7280", isOutlineOnly: true, isActionable: false);

        var px = TimelineSvgRenderer.GetLaneCenterX(1); // 36
        // Full-height junction: M px 0 L px 40
        Assert.That(svg, Does.Contain($"M {px} 0 L {px} 40"));
    }

    [Test]
    public void GenerateTaskGraphCircleSvg_DrawTopLine_DrawsVerticalAboveCircle()
    {
        var svg = TimelineSvgRenderer.GenerateTaskGraphCircleSvg(
            nodeLane: 1, parentLane: null, isFirstChild: false, maxLanes: 2,
            nodeColor: "#51A5C1", isOutlineOnly: true, isActionable: false, drawTopLine: true);

        var x = TimelineSvgRenderer.GetLaneCenterX(1); // 36
        var topLineEndY = 20 - TimelineSvgRenderer.NodeRadius - 2; // 12
        Assert.That(svg, Does.Contain($"M {x} 0 L {x} {topLineEndY}"));
    }

    [Test]
    public void GenerateTaskGraphCircleSvg_DrawBottomLine_DrawsVerticalBelowCircle()
    {
        var svg = TimelineSvgRenderer.GenerateTaskGraphCircleSvg(
            nodeLane: 1, parentLane: null, isFirstChild: false, maxLanes: 2,
            nodeColor: "#51A5C1", isOutlineOnly: true, isActionable: false, drawBottomLine: true);

        var x = TimelineSvgRenderer.GetLaneCenterX(1); // 36
        var bottomLineStartY = 20 + TimelineSvgRenderer.NodeRadius + 2; // 28
        Assert.That(svg, Does.Contain($"M {x} {bottomLineStartY} L {x} 40"));
    }

    [Test]
    public void GenerateTaskGraphCircleSvg_Lane0PassThrough_DrawsFullVerticalAtLane0()
    {
        var svg = TimelineSvgRenderer.GenerateTaskGraphCircleSvg(
            nodeLane: 2, parentLane: null, isFirstChild: false, maxLanes: 3,
            nodeColor: "#3b82f6", isOutlineOnly: false, isActionable: false,
            drawLane0PassThrough: true);

        var lane0X = TimelineSvgRenderer.GetLaneCenterX(0); // 12
        Assert.That(svg, Does.Contain($"M {lane0X} 0 L {lane0X} {TimelineSvgRenderer.RowHeight}"),
            "Should draw full vertical line at lane 0");
        Assert.That(svg, Does.Contain("stroke=\"#6b7280\""),
            "Lane 0 connector should use gray color");
    }

    [Test]
    public void GenerateTaskGraphCircleSvg_Lane0Connector_NonLast_DrawsVerticalAndBranch()
    {
        var svg = TimelineSvgRenderer.GenerateTaskGraphCircleSvg(
            nodeLane: 1, parentLane: null, isFirstChild: false, maxLanes: 2,
            nodeColor: "#3b82f6", isOutlineOnly: false, isActionable: false,
            drawLane0Connector: true, isLastLane0Connector: false);

        var lane0X = TimelineSvgRenderer.GetLaneCenterX(0); // 12
        var cx = TimelineSvgRenderer.GetLaneCenterX(1); // 36
        var cy = TimelineSvgRenderer.GetRowCenterY(); // 20
        var nodeEdgeX = cx - TimelineSvgRenderer.NodeRadius - 2; // 28

        // Full vertical at lane 0
        Assert.That(svg, Does.Contain($"M {lane0X} 0 L {lane0X} {TimelineSvgRenderer.RowHeight}"),
            "Non-last connector should have full vertical at lane 0");
        // Horizontal branch from lane 0 to node left edge
        Assert.That(svg, Does.Contain($"M {lane0X} {cy} L {nodeEdgeX} {cy}"),
            "Non-last connector should have horizontal branch to node");
    }

    [Test]
    public void GenerateTaskGraphCircleSvg_Lane0Connector_Last_DrawsArcToNode()
    {
        var svg = TimelineSvgRenderer.GenerateTaskGraphCircleSvg(
            nodeLane: 1, parentLane: null, isFirstChild: false, maxLanes: 2,
            nodeColor: "#3b82f6", isOutlineOnly: false, isActionable: false,
            drawLane0Connector: true, isLastLane0Connector: true);

        var lane0X = TimelineSvgRenderer.GetLaneCenterX(0); // 12
        var cx = TimelineSvgRenderer.GetLaneCenterX(1); // 36
        var cy = TimelineSvgRenderer.GetRowCenterY(); // 20
        var nodeEdgeX = cx - TimelineSvgRenderer.NodeRadius - 2; // 28
        var r = TimelineSvgRenderer.NodeRadius; // 6

        // Arc path: vertical down to junction, arc, horizontal to node
        Assert.That(svg, Does.Contain(
            $"M {lane0X} 0 L {lane0X} {cy - r} A {r} {r} 0 0 0 {lane0X + r} {cy} L {nodeEdgeX} {cy}"),
            "Last connector should have arc from lane 0 to node");
    }

    [Test]
    public void GenerateTaskGraphCircleSvg_SeriesConnectorFromLane_DrawsLShapedPath()
    {
        var svg = TimelineSvgRenderer.GenerateTaskGraphCircleSvg(
            nodeLane: 1, parentLane: null, isFirstChild: false, maxLanes: 2,
            nodeColor: "#6b7280", isOutlineOnly: true, isActionable: false,
            seriesConnectorFromLane: 0);

        var fromX = TimelineSvgRenderer.GetLaneCenterX(0); // 12
        var cx = TimelineSvgRenderer.GetLaneCenterX(1); // 36
        var cy = TimelineSvgRenderer.GetRowCenterY(); // 20
        var nodeEdgeX = cx - TimelineSvgRenderer.NodeRadius - 2; // 28
        var r = TimelineSvgRenderer.NodeRadius; // 6

        // L-shaped path with arc
        Assert.That(svg, Does.Contain($"M {fromX} 0 L {fromX} {cy - r} A {r} {r} 0 0 0 {fromX + r} {cy} L {nodeEdgeX} {cy}"));
    }

    #endregion

    #region GenerateTaskGraphCircleSvg Lane 0 Color Tests

    [Test]
    public void GenerateTaskGraphCircleSvg_Lane0PassThrough_WithCustomColor_UsesProvidedColor()
    {
        var svg = TimelineSvgRenderer.GenerateTaskGraphCircleSvg(
            nodeLane: 2, parentLane: null, isFirstChild: false, maxLanes: 3,
            nodeColor: "#3b82f6", isOutlineOnly: false, isActionable: false,
            drawLane0PassThrough: true, lane0Color: "#ef4444");

        var lane0X = TimelineSvgRenderer.GetLaneCenterX(0); // 12
        Assert.That(svg, Does.Contain($"M {lane0X} 0 L {lane0X} {TimelineSvgRenderer.RowHeight}"),
            "Should draw full vertical line at lane 0");
        // Should use the provided color, not the default gray
        Assert.That(svg, Does.Contain("stroke=\"#ef4444\""),
            "Lane 0 connector should use provided priority color");
    }

    [Test]
    public void GenerateTaskGraphCircleSvg_Lane0Connector_WithCustomColor_UsesProvidedColor()
    {
        var svg = TimelineSvgRenderer.GenerateTaskGraphCircleSvg(
            nodeLane: 1, parentLane: null, isFirstChild: false, maxLanes: 2,
            nodeColor: "#3b82f6", isOutlineOnly: false, isActionable: false,
            drawLane0Connector: true, isLastLane0Connector: false, lane0Color: "#f97316");

        // Should use the provided orange color
        Assert.That(svg, Does.Contain("stroke=\"#f97316\""),
            "Lane 0 connector should use provided priority color");
    }

    [Test]
    public void GenerateTaskGraphCircleSvg_Lane0Connector_Last_WithCustomColor_UsesProvidedColor()
    {
        var svg = TimelineSvgRenderer.GenerateTaskGraphCircleSvg(
            nodeLane: 1, parentLane: null, isFirstChild: false, maxLanes: 2,
            nodeColor: "#3b82f6", isOutlineOnly: false, isActionable: false,
            drawLane0Connector: true, isLastLane0Connector: true, lane0Color: "#22c55e");

        // Should use the provided green color for the arc path
        Assert.That(svg, Does.Contain("stroke=\"#22c55e\""),
            "Last lane 0 connector should use provided priority color");
    }

    [Test]
    public void GenerateTaskGraphCircleSvg_Lane0Connector_NullColor_UsesDefaultGrey()
    {
        var svg = TimelineSvgRenderer.GenerateTaskGraphCircleSvg(
            nodeLane: 1, parentLane: null, isFirstChild: false, maxLanes: 2,
            nodeColor: "#3b82f6", isOutlineOnly: false, isActionable: false,
            drawLane0Connector: true, isLastLane0Connector: false, lane0Color: null);

        Assert.That(svg, Does.Contain("stroke=\"#6b7280\""),
            "Lane 0 connector should use default grey when no color provided");
    }

    #endregion

    #region GenerateTaskGraphCircleSvg Size Tests

    [Test]
    public void GenerateTaskGraphCircleSvg_SvgWidth_MatchesCalculateSvgWidth()
    {
        var maxLanes = 3;
        var expectedWidth = TimelineSvgRenderer.CalculateSvgWidth(maxLanes);

        var svg = TimelineSvgRenderer.GenerateTaskGraphCircleSvg(
            nodeLane: 0, parentLane: null, isFirstChild: false, maxLanes: maxLanes,
            nodeColor: "#6b7280", isOutlineOnly: true, isActionable: false);

        Assert.That(svg, Does.Contain($"width=\"{expectedWidth}\""));
        Assert.That(svg, Does.Contain($"height=\"{TimelineSvgRenderer.RowHeight}\""));
    }

    #endregion

    #region GenerateTaskGraphLoadMoreSvg Tests

    [Test]
    public void GenerateTaskGraphLoadMoreSvg_GeneratesCorrectSvg()
    {
        var svg = TimelineSvgRenderer.GenerateTaskGraphLoadMoreSvg(maxLanes: 2);

        Assert.That(svg, Does.Contain("<svg"));
        Assert.That(svg, Does.Contain("<circle"));
        Assert.That(svg, Does.Contain("fill=\"#51A5C1\""));  // Ocean color
        Assert.That(svg, Does.Contain("stroke=\"white\""));
        Assert.That(svg, Does.Contain("<text"));
        Assert.That(svg, Does.Contain(">+<"));  // Plus sign
    }

    [Test]
    public void GenerateTaskGraphLoadMoreSvg_DrawsVerticalLineBelowButton()
    {
        var svg = TimelineSvgRenderer.GenerateTaskGraphLoadMoreSvg(maxLanes: 1);

        var cx = TimelineSvgRenderer.GetLaneCenterX(0); // 12
        var cy = TimelineSvgRenderer.GetRowCenterY(); // 20
        var r = TimelineSvgRenderer.NodeRadius + 2; // 8
        var bottomLineStartY = cy + r + 2; // 30

        Assert.That(svg, Does.Contain($"M {cx} {bottomLineStartY} L {cx} {TimelineSvgRenderer.RowHeight}"));
    }

    #endregion

    private static int CountOccurrences(string source, string pattern)
    {
        var count = 0;
        var index = 0;
        while ((index = source.IndexOf(pattern, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += pattern.Length;
        }
        return count;
    }
}
