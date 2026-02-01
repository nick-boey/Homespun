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

    [Test]
    public void ConnectorArcRadius_Is10()
    {
        Assert.That(TimelineSvgRenderer.ConnectorArcRadius, Is.EqualTo(10));
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

    [Test]
    public void GenerateVerticalLine_FirstInLane_NoTopSegment()
    {
        var path = TimelineSvgRenderer.GenerateVerticalLine(1, hasNodeInLane: true, drawTop: false, drawBottom: true);

        // Should only have bottom segment, no top segment
        Assert.That(path, Does.Not.Contain("M 36 0 L 36 12"), "Should not have top segment");
        Assert.That(path, Does.Contain("M 36 28 L 36 40"), "Should have bottom segment");
    }

    [Test]
    public void GenerateVerticalLine_LastInLane_NoBottomSegment()
    {
        var path = TimelineSvgRenderer.GenerateVerticalLine(1, hasNodeInLane: true, drawTop: true, drawBottom: false);

        // Should only have top segment, no bottom segment
        Assert.That(path, Does.Contain("M 36 0 L 36 12"), "Should have top segment");
        Assert.That(path, Does.Not.Contain("M 36 28 L 36 40"), "Should not have bottom segment");
    }

    [Test]
    public void GenerateVerticalLine_FirstAndLastInLane_NoSegments()
    {
        var path = TimelineSvgRenderer.GenerateVerticalLine(1, hasNodeInLane: true, drawTop: false, drawBottom: false);

        // Single node in lane - no vertical segments at all
        Assert.That(path, Is.Empty, "Single node in lane should have no vertical segments");
    }

    #endregion

    #region GenerateConnector Tests

    [Test]
    public void GenerateConnector_Lane0ToLane1_GeneratesLShapedPathWithArc()
    {
        var path = TimelineSvgRenderer.GenerateConnector(0, 1);

        // L-shaped path with rounded corner from lane 0 to lane 1
        // From: (12, 0) down to (12, 10) [centerY - arcRadius = 20 - 10]
        // Arc to (22, 20) [fromX + arcRadius, centerY]
        // Horizontal to node edge (27) [36 - 7 - 2]
        Assert.That(path, Does.Contain("M 12 0"));     // Start at lane 0, top
        Assert.That(path, Does.Contain("L 12 10"));    // Down to arc start
        Assert.That(path, Does.Contain("A 10 10 0 0 1 22 20")); // Quarter-circle arc
        Assert.That(path, Does.Contain("L 27 20"));    // Horizontal to node side
    }

    [Test]
    public void GenerateConnector_Lane1ToLane2_GeneratesLShapedPathWithArc()
    {
        var path = TimelineSvgRenderer.GenerateConnector(1, 2);

        // L-shaped path with rounded corner from lane 1 to lane 2
        // arcEndX = 36 + 10 = 46, nodeEdgeX = 60 - 7 - 2 = 51
        Assert.That(path, Does.Contain("M 36 0"));     // Start at lane 1
        Assert.That(path, Does.Contain("L 36 10"));    // Down to arc start
        Assert.That(path, Does.Contain("A 10 10 0 0 1 46 20")); // Quarter-circle arc
        Assert.That(path, Does.Contain("L 51 20"));    // Horizontal to node side
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

    [Test]
    public void GenerateRowSvg_WithConnector_NoTopSegment()
    {
        // When a node has a connector coming in, we don't need a top segment
        // because the connector provides the visual connection
        var svg = TimelineSvgRenderer.GenerateRowSvg(
            nodeLane: 1,
            activeLanes: new HashSet<int> { 0, 1 },
            connectorFromLane: 0,
            maxLanes: 2,
            nodeColor: "#3b82f6",
            isIssue: false,
            isLoadMore: false,
            isFirstRowInLane: true,
            isLastRowInLane: true);

        // The node lane (1) should not have a top segment since it has a connector
        // and is first in lane
        // Count M 36 0 occurrences - should only be from connector, not vertical line
        var verticalTopOccurrences = CountOccurrences(svg, "M 36 0 L 36 12");
        Assert.That(verticalTopOccurrences, Is.EqualTo(0), "Should not have vertical top segment for node lane with connector");
    }

    [Test]
    public void GenerateRowSvg_FirstRowInLane_NoTopSegment()
    {
        var svg = TimelineSvgRenderer.GenerateRowSvg(
            nodeLane: 1,
            activeLanes: new HashSet<int> { 0, 1 },
            connectorFromLane: 0,
            maxLanes: 2,
            nodeColor: "#3b82f6",
            isIssue: false,
            isLoadMore: false,
            isFirstRowInLane: true,
            isLastRowInLane: false);

        // Should have bottom segment but not top segment for node lane
        Assert.That(svg, Does.Contain("M 36 28 L 36 40"), "Should have bottom segment for first row");
    }

    [Test]
    public void GenerateRowSvg_LastRowInLane_NoBottomSegment()
    {
        var svg = TimelineSvgRenderer.GenerateRowSvg(
            nodeLane: 1,
            activeLanes: new HashSet<int> { 0, 1 },
            connectorFromLane: null,
            maxLanes: 2,
            nodeColor: "#3b82f6",
            isIssue: false,
            isLoadMore: false,
            isFirstRowInLane: false,
            isLastRowInLane: true);

        // Should have top segment but not bottom segment
        Assert.That(svg, Does.Contain("M 36 0 L 36 12"), "Should have top segment");
        Assert.That(svg, Does.Not.Contain("M 36 28 L 36 40"), "Should not have bottom segment for last row");
    }

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

    [Test]
    public void GenerateRowSvg_PassThroughLane_NoBottomWhenEnding()
    {
        // Arrange - Pass-through lane (1) that is ending at this row
        // Node is in lane 2, lane 1 passes through but should stop at center
        var svg = TimelineSvgRenderer.GenerateRowSvg(
            nodeLane: 2,
            activeLanes: new HashSet<int> { 0, 1, 2 },
            connectorFromLane: null,
            maxLanes: 3,
            nodeColor: "#3b82f6",
            isIssue: false,
            isLoadMore: false,
            isFirstRowInLane: false,
            isLastRowInLane: false,
            lanesEndingThisRow: new HashSet<int> { 1 });

        // Assert - Lane 1's path should stop at center (y=20), not extend to bottom (y=40)
        // Lane 1 is at x=36
        Assert.That(svg, Does.Contain("M 36 0 L 36 20"), "Lane 1 should stop at center y=20");
        Assert.That(svg, Does.Not.Contain("M 36 0 L 36 40"), "Lane 1 should NOT extend to full height");
    }

    [Test]
    public void GenerateRowSvg_PassThroughLane_FullHeightWhenNotEnding()
    {
        // Arrange - Pass-through lane (1) that is NOT ending
        var svg = TimelineSvgRenderer.GenerateRowSvg(
            nodeLane: 2,
            activeLanes: new HashSet<int> { 0, 1, 2 },
            connectorFromLane: null,
            maxLanes: 3,
            nodeColor: "#3b82f6",
            isIssue: false,
            isLoadMore: false,
            isFirstRowInLane: false,
            isLastRowInLane: false,
            lanesEndingThisRow: new HashSet<int>()); // Lane 1 is NOT ending

        // Assert - Lane 1's path should extend full height
        Assert.That(svg, Does.Contain("M 36 0 L 36 40"), "Lane 1 should extend full height when not ending");
    }

    [Test]
    public void GenerateRowSvg_PassThroughLane_NullLanesEnding_FullHeight()
    {
        // Arrange - Backward compatibility: null lanesEndingThisRow should render full height
        var svg = TimelineSvgRenderer.GenerateRowSvg(
            nodeLane: 2,
            activeLanes: new HashSet<int> { 0, 1, 2 },
            connectorFromLane: null,
            maxLanes: 3,
            nodeColor: "#3b82f6",
            isIssue: false,
            isLoadMore: false,
            isFirstRowInLane: false,
            isLastRowInLane: false,
            lanesEndingThisRow: null);

        // Assert - Lane 1's path should extend full height when lanesEndingThisRow is null
        Assert.That(svg, Does.Contain("M 36 0 L 36 40"), "Lane 1 should extend full height when lanesEndingThisRow is null");
    }

    [Test]
    public void GenerateRowSvg_MultiplePassThroughLanes_SomeEnding()
    {
        // Arrange - Multiple pass-through lanes, only some ending
        // Node is in lane 3, lanes 1 and 2 pass through, but only lane 1 is ending
        var svg = TimelineSvgRenderer.GenerateRowSvg(
            nodeLane: 3,
            activeLanes: new HashSet<int> { 0, 1, 2, 3 },
            connectorFromLane: null,
            maxLanes: 4,
            nodeColor: "#3b82f6",
            isIssue: false,
            isLoadMore: false,
            isFirstRowInLane: false,
            isLastRowInLane: false,
            lanesEndingThisRow: new HashSet<int> { 1 }); // Only lane 1 is ending

        // Assert - Lane 1 (x=36) should stop at center
        Assert.That(svg, Does.Contain("M 36 0 L 36 20"), "Lane 1 should stop at center");

        // Lane 2 (x=60) should extend full height
        Assert.That(svg, Does.Contain("M 60 0 L 60 40"), "Lane 2 should extend full height");
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
