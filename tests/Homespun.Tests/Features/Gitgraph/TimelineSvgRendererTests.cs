using Homespun.Features.Gitgraph.Services;

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

    [Test]
    public void DiamondSize_Is7()
    {
        Assert.That(TimelineSvgRenderer.DiamondSize, Is.EqualTo(7));
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

    #region GenerateCircleNode Tests

    [Test]
    public void GenerateCircleNode_Lane0_GeneratesCorrectSvg()
    {
        var svg = TimelineSvgRenderer.GenerateCircleNode(0, "#10b981");

        Assert.That(svg, Does.Contain("<circle"));
        Assert.That(svg, Does.Contain("cx=\"12\""));   // Lane 0 center
        Assert.That(svg, Does.Contain("cy=\"20\""));   // Row center
        Assert.That(svg, Does.Contain("r=\"6\""));     // Node radius
        Assert.That(svg, Does.Contain("fill=\"#10b981\""));
    }

    [Test]
    public void GenerateCircleNode_Lane1_PositionsCorrectly()
    {
        var svg = TimelineSvgRenderer.GenerateCircleNode(1, "#6b7280");

        Assert.That(svg, Does.Contain("cx=\"36\""));   // Lane 1 center
    }

    #endregion

    #region GenerateDiamondNode Tests

    [Test]
    public void GenerateDiamondNode_Lane0_GeneratesPathWithCorrectShape()
    {
        var svg = TimelineSvgRenderer.GenerateDiamondNode(0, "#a855f7");

        Assert.That(svg, Does.Contain("<path"));
        Assert.That(svg, Does.Contain("fill=\"#a855f7\""));
        // Diamond path should form a diamond shape around center (12, 20)
        // Points: top (12, 13), right (19, 20), bottom (12, 27), left (5, 20)
        Assert.That(svg, Does.Contain("M 12 13"));     // Start at top
        Assert.That(svg, Does.Contain("L 19 20"));     // Line to right
        Assert.That(svg, Does.Contain("L 12 27"));     // Line to bottom
        Assert.That(svg, Does.Contain("L 5 20"));      // Line to left
        Assert.That(svg, Does.Contain("Z"));           // Close path
    }

    [Test]
    public void GenerateDiamondNode_Lane2_PositionsCorrectly()
    {
        var svg = TimelineSvgRenderer.GenerateDiamondNode(2, "#3b82f6");

        // Lane 2 center = 60, so diamond starts at (60, 13) for top
        Assert.That(svg, Does.Contain("M 60 13"));
    }

    #endregion

    #region GenerateVerticalLine Tests

    [Test]
    public void GenerateVerticalLine_NoNode_GeneratesFullLine()
    {
        var path = TimelineSvgRenderer.GenerateVerticalLine(0, hasNodeInLane: false);

        // Full line from top (0) to bottom (40)
        Assert.That(path, Does.Contain("M 12 0"));
        Assert.That(path, Does.Contain("L 12 40"));
    }

    [Test]
    public void GenerateVerticalLine_WithNode_GeneratesGappedLine()
    {
        var path = TimelineSvgRenderer.GenerateVerticalLine(0, hasNodeInLane: true);

        // Should have gap around the node (radius 6 + 2 padding)
        // Top segment: M 12 0 L 12 12 (centerY - radius - 2 = 20 - 6 - 2 = 12)
        Assert.That(path, Does.Contain("M 12 0 L 12 12"));
        // Bottom segment: M 12 28 L 12 40 (centerY + radius + 2 = 20 + 6 + 2 = 28)
        Assert.That(path, Does.Contain("M 12 28 L 12 40"));
    }

    [Test]
    public void GenerateVerticalLine_Lane1_UsesCorrectXCoordinate()
    {
        var path = TimelineSvgRenderer.GenerateVerticalLine(1, hasNodeInLane: false);

        // Lane 1 center = 36
        Assert.That(path, Does.Contain("M 36 0"));
        Assert.That(path, Does.Contain("L 36 40"));
    }

    #endregion

    #region GenerateConnector Tests

    [Test]
    public void GenerateConnector_Lane0ToLane1_GeneratesLShapedPath()
    {
        var path = TimelineSvgRenderer.GenerateConnector(0, 1);

        // L-shaped path from lane 0 to lane 1
        // From: (12, 0) down to bendY (centerY - radius - 4 = 20 - 6 - 4 = 10)
        // Then horizontal to lane 1 (36)
        // Then down to just above node (centerY - radius - 2 = 12)
        Assert.That(path, Does.Contain("M 12 0"));     // Start at lane 0, top
        Assert.That(path, Does.Contain("L 12 10"));    // Down to bend
        Assert.That(path, Does.Contain("L 36 10"));    // Horizontal to lane 1
        Assert.That(path, Does.Contain("L 36 12"));    // Down to node
    }

    [Test]
    public void GenerateConnector_Lane1ToLane2_GeneratesLShapedPath()
    {
        var path = TimelineSvgRenderer.GenerateConnector(1, 2);

        Assert.That(path, Does.Contain("M 36 0"));     // Start at lane 1
        Assert.That(path, Does.Contain("L 60 10"));    // Horizontal bend to lane 2
    }

    #endregion

    #region GenerateRowSvg Tests

    [Test]
    public void GenerateRowSvg_PRNode_GeneratesCircle()
    {
        var svg = TimelineSvgRenderer.GenerateRowSvg(
            nodeLane: 0,
            activeLanes: new HashSet<int> { 0 },
            connectorFromLane: null,
            maxLanes: 1,
            nodeColor: "#10b981",
            isIssue: false,
            isLoadMore: false);

        Assert.That(svg, Does.Contain("<svg"));
        Assert.That(svg, Does.Contain("width=\"36\""));  // Single lane width
        Assert.That(svg, Does.Contain("height=\"40\""));
        Assert.That(svg, Does.Contain("<circle"));       // Circle for PR
        Assert.That(svg, Does.Contain("fill=\"#10b981\""));  // Node color
    }

    [Test]
    public void GenerateRowSvg_IssueNode_GeneratesDiamond()
    {
        var svg = TimelineSvgRenderer.GenerateRowSvg(
            nodeLane: 0,
            activeLanes: new HashSet<int> { 0 },
            connectorFromLane: null,
            maxLanes: 1,
            nodeColor: "#a855f7",
            isIssue: true,
            isLoadMore: false);

        Assert.That(svg, Does.Contain("<path"));
        Assert.That(svg, Does.Contain("fill=\"#a855f7\""));
    }

    [Test]
    public void GenerateRowSvg_WithConnector_IncludesConnectorPath()
    {
        var svg = TimelineSvgRenderer.GenerateRowSvg(
            nodeLane: 1,
            activeLanes: new HashSet<int> { 0, 1 },
            connectorFromLane: 0,
            maxLanes: 2,
            nodeColor: "#3b82f6",
            isIssue: false,
            isLoadMore: false);

        // Should have connector from lane 0 to lane 1
        Assert.That(svg, Does.Contain("M 12 0"));  // Connector starts at lane 0
    }

    [Test]
    public void GenerateRowSvg_MultipleLanes_DrawsAllActiveLines()
    {
        var svg = TimelineSvgRenderer.GenerateRowSvg(
            nodeLane: 0,
            activeLanes: new HashSet<int> { 0, 1, 2 },
            connectorFromLane: null,
            maxLanes: 3,
            nodeColor: "#6b7280",
            isIssue: false,
            isLoadMore: false);

        // Should have vertical lines for all three lanes
        Assert.That(svg, Does.Contain("stroke=\"#6b7280\"")); // Lane lines
        Assert.That(svg, Does.Contain("width=\"84\""));       // 3 lanes
    }

    [Test]
    public void GenerateRowSvg_LoadMore_GeneratesLoadMoreButton()
    {
        var svg = TimelineSvgRenderer.GenerateRowSvg(
            nodeLane: 0,
            activeLanes: new HashSet<int> { 0 },
            connectorFromLane: null,
            maxLanes: 1,
            nodeColor: "#51A5C1",
            isIssue: false,
            isLoadMore: true);

        Assert.That(svg, Does.Contain("<circle"));
        Assert.That(svg, Does.Contain("stroke=\"white\""));  // White stroke for button
        Assert.That(svg, Does.Contain("<text"));             // Plus sign text
    }

    [Test]
    public void GenerateRowSvg_SameLaneConnector_NoConnectorDrawn()
    {
        var svg = TimelineSvgRenderer.GenerateRowSvg(
            nodeLane: 0,
            activeLanes: new HashSet<int> { 0 },
            connectorFromLane: 0, // Same lane as node
            maxLanes: 1,
            nodeColor: "#6b7280",
            isIssue: false,
            isLoadMore: false);

        // Connector from same lane should not draw L-shaped path
        // Count occurrences of "M 12" - should only be from vertical line, not connector
        var occurrences = svg.Split("M 12 0").Length - 1;
        Assert.That(occurrences, Is.EqualTo(1), "Should only have one path starting at lane 0");
    }

    [Test]
    public void GenerateRowSvg_WithLaneColors_UsesProvidedColors()
    {
        var laneColors = new Dictionary<int, string>
        {
            { 0, "#ff0000" },
            { 1, "#00ff00" }
        };

        var svg = TimelineSvgRenderer.GenerateRowSvg(
            nodeLane: 0,
            activeLanes: new HashSet<int> { 0, 1 },
            connectorFromLane: null,
            maxLanes: 2,
            nodeColor: "#6b7280",
            isIssue: false,
            isLoadMore: false,
            laneColors: laneColors);

        Assert.That(svg, Does.Contain("stroke=\"#ff0000\""));  // Lane 0 color
        Assert.That(svg, Does.Contain("stroke=\"#00ff00\""));  // Lane 1 color
    }

    #endregion

    #region EscapeAttribute Tests (via public methods)

    [Test]
    public void GenerateCircleNode_EscapesSpecialCharacters()
    {
        // Color with special characters (edge case)
        var svg = TimelineSvgRenderer.GenerateCircleNode(0, "#color<>&\"'");

        Assert.That(svg, Does.Not.Contain("<&"));
        Assert.That(svg, Does.Contain("&lt;"));
        Assert.That(svg, Does.Contain("&gt;"));
        Assert.That(svg, Does.Contain("&amp;"));
    }

    #endregion
}
