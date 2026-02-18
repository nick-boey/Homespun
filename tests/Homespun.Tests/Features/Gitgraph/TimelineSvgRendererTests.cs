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

    [Test]
    public void TaskGraphArcRadius_EqualsDiamondSize()
    {
        Assert.That(TimelineSvgRenderer.TaskGraphArcRadius, Is.EqualTo(TimelineSvgRenderer.DiamondSize));
        Assert.That(TimelineSvgRenderer.TaskGraphArcRadius, Is.EqualTo(7));
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
        Assert.That(path, Does.Contain("A 10 10 0 0 0 22 20")); // Quarter-circle arc (counter-clockwise)
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
        Assert.That(path, Does.Contain("A 10 10 0 0 0 46 20")); // Quarter-circle arc (counter-clockwise)
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
        // Node is in lane 2, lane 1 passes through but should stop at top of arc
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

        // Assert - Lane 1's path should stop at top of arc (y=10), not extend to bottom (y=40)
        // centerY=20, arcRadius=10, so top of arc = 10
        // Lane 1 is at x=36
        Assert.That(svg, Does.Contain("M 36 0 L 36 10"), "Lane 1 should stop at arc top y=10");
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

        // Assert - Lane 1 (x=36) should stop at top of arc (y=10)
        Assert.That(svg, Does.Contain("M 36 0 L 36 10"), "Lane 1 should stop at arc top");

        // Lane 2 (x=60) should extend full height
        Assert.That(svg, Does.Contain("M 60 0 L 60 40"), "Lane 2 should extend full height");
    }

    [Test]
    public void GenerateRowSvg_ReservedLane_SkipsVerticalLine()
    {
        // Arrange - Lane 1 is active but reserved (shouldn't render vertical line)
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
            lanesEndingThisRow: null,
            reservedLanes: new HashSet<int> { 1 }); // Lane 1 is reserved

        // Assert - Lane 1 (x=36) should NOT have a vertical line
        Assert.That(svg, Does.Not.Contain("M 36 0"), "Reserved lane 1 should not render vertical line");

        // Lane 0 (x=12) and Lane 2 (x=60) should still render
        Assert.That(svg, Does.Contain("M 12 0"), "Lane 0 should render vertical line");
        Assert.That(svg, Does.Contain("M 60 0"), "Lane 2 should render vertical line");
    }

    [Test]
    public void GenerateRowSvg_ReservedLane_StillRendersIfNodeInLane()
    {
        // Arrange - Lane 1 is reserved but contains the node (should still render)
        var svg = TimelineSvgRenderer.GenerateRowSvg(
            nodeLane: 1,
            activeLanes: new HashSet<int> { 0, 1 },
            connectorFromLane: null,
            maxLanes: 2,
            nodeColor: "#3b82f6",
            isIssue: false,
            isLoadMore: false,
            isFirstRowInLane: false,
            isLastRowInLane: false,
            lanesEndingThisRow: null,
            reservedLanes: new HashSet<int> { 1 }); // Lane 1 is reserved but has node

        // Assert - Lane 1 should still render because it contains the node
        Assert.That(svg, Does.Contain("M 36 0 L 36 12"), "Node lane should render even if reserved");
    }

    [Test]
    public void GenerateRowSvg_MultipleReservedLanes_SkipsAll()
    {
        // Arrange - Lanes 1 and 2 are reserved
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
            lanesEndingThisRow: null,
            reservedLanes: new HashSet<int> { 1, 2 }); // Lanes 1 and 2 are reserved

        // Assert - Lanes 1 (x=36) and 2 (x=60) should NOT render
        Assert.That(svg, Does.Not.Contain("M 36 0"), "Reserved lane 1 should not render");
        Assert.That(svg, Does.Not.Contain("M 60 0"), "Reserved lane 2 should not render");

        // Lane 0 (x=12) and Lane 3 (x=84) should render
        Assert.That(svg, Does.Contain("M 12 0"), "Lane 0 should render");
        Assert.That(svg, Does.Contain("M 84 0"), "Lane 3 should render");
    }

    #endregion

    #region GenerateDiamondNodeOutline Tests

    [Test]
    public void GenerateDiamondNodeOutline_Lane0_GeneratesOutlineWithNoFill()
    {
        var svg = TimelineSvgRenderer.GenerateDiamondNodeOutline(0, "#3b82f6");

        Assert.That(svg, Does.Contain("<path"));
        Assert.That(svg, Does.Contain("fill=\"none\""));
        Assert.That(svg, Does.Contain("stroke=\"#3b82f6\""));
        Assert.That(svg, Does.Contain("stroke-width=\"2\""));
        // Diamond path should form a diamond shape around center (12, 20)
        Assert.That(svg, Does.Contain("M 12 13"));     // Start at top
        Assert.That(svg, Does.Contain("L 19 20"));     // Line to right
        Assert.That(svg, Does.Contain("L 12 27"));     // Line to bottom
        Assert.That(svg, Does.Contain("L 5 20"));      // Line to left
        Assert.That(svg, Does.Contain("Z"));           // Close path
    }

    [Test]
    public void GenerateDiamondNodeOutline_Lane2_PositionsCorrectly()
    {
        var svg = TimelineSvgRenderer.GenerateDiamondNodeOutline(2, "#ef4444");

        // Lane 2 center = 60
        Assert.That(svg, Does.Contain("M 60 13"));
        Assert.That(svg, Does.Contain("stroke=\"#ef4444\""));
    }

    #endregion

    #region GenerateErrorCross Tests

    [Test]
    public void GenerateErrorCross_Lane0_GeneratesCrossLines()
    {
        var svg = TimelineSvgRenderer.GenerateErrorCross(0, "white");

        // Cross at center (12, 20) with crossSize=3
        Assert.That(svg, Does.Contain("<line"));
        // Diagonal 1: (9, 17) to (15, 23)
        Assert.That(svg, Does.Contain("x1=\"9\""));
        Assert.That(svg, Does.Contain("y1=\"17\""));
        Assert.That(svg, Does.Contain("x2=\"15\""));
        Assert.That(svg, Does.Contain("y2=\"23\""));
        // Diagonal 2: (15, 17) to (9, 23)
        Assert.That(svg, Does.Contain("stroke=\"white\""));
        Assert.That(svg, Does.Contain("stroke-width=\"2\""));
    }

    [Test]
    public void GenerateErrorCross_Lane1_PositionsCorrectly()
    {
        var svg = TimelineSvgRenderer.GenerateErrorCross(1, "#ef4444");

        // Lane 1 center = 36, row center = 20, crossSize = 3
        // Diagonal: (33, 17) to (39, 23)
        Assert.That(svg, Does.Contain("x1=\"33\""));
        Assert.That(svg, Does.Contain("y1=\"17\""));
        Assert.That(svg, Does.Contain("x2=\"39\""));
        Assert.That(svg, Does.Contain("y2=\"23\""));
    }

    #endregion

    #region GenerateRowSvg Outline and Error Cross Tests

    [Test]
    public void GenerateRowSvg_IssueOutlineOnly_GeneratesOutlineDiamond()
    {
        var svg = TimelineSvgRenderer.GenerateRowSvg(
            nodeLane: 0,
            activeLanes: new HashSet<int> { 0 },
            connectorFromLane: null,
            maxLanes: 1,
            nodeColor: "#3b82f6",
            isIssue: true,
            isLoadMore: false,
            isOutlineOnly: true);

        Assert.That(svg, Does.Contain("fill=\"none\""));
        Assert.That(svg, Does.Contain("stroke=\"#3b82f6\""));
        Assert.That(svg, Does.Contain("stroke-width=\"2\""));
    }

    [Test]
    public void GenerateRowSvg_IssueNotOutline_GeneratesFilledDiamond()
    {
        var svg = TimelineSvgRenderer.GenerateRowSvg(
            nodeLane: 0,
            activeLanes: new HashSet<int> { 0 },
            connectorFromLane: null,
            maxLanes: 1,
            nodeColor: "#3b82f6",
            isIssue: true,
            isLoadMore: false,
            isOutlineOnly: false);

        // Diamond path should have fill color (not outline style)
        Assert.That(svg, Does.Contain("fill=\"#3b82f6\""));
        // Should NOT have a diamond path with outline-only style (stroke on diamond)
        Assert.That(svg, Does.Not.Contain("stroke=\"#3b82f6\" stroke-width=\"2\""),
            "Filled diamond should not have outline stroke on diamond path");
    }

    [Test]
    public void GenerateRowSvg_WithErrorCross_IncludesCrossLines()
    {
        var svg = TimelineSvgRenderer.GenerateRowSvg(
            nodeLane: 0,
            activeLanes: new HashSet<int> { 0 },
            connectorFromLane: null,
            maxLanes: 1,
            nodeColor: "#ef4444",
            isIssue: true,
            isLoadMore: false,
            showErrorCross: true);

        Assert.That(svg, Does.Contain("<line"));
        Assert.That(svg, Does.Contain("stroke=\"white\""));
    }

    [Test]
    public void GenerateRowSvg_NonIssue_OutlineAndErrorCrossIgnored()
    {
        // Outline and error cross only apply to issues, not PRs
        var svg = TimelineSvgRenderer.GenerateRowSvg(
            nodeLane: 0,
            activeLanes: new HashSet<int> { 0 },
            connectorFromLane: null,
            maxLanes: 1,
            nodeColor: "#10b981",
            isIssue: false,
            isLoadMore: false,
            isOutlineOnly: true,
            showErrorCross: true);

        // Should render as a circle, not diamond, and no cross
        Assert.That(svg, Does.Contain("<circle"));
        Assert.That(svg, Does.Not.Contain("<line"));
    }

    #endregion

    #region GenerateFlatOrphanRowSvg Outline and Error Cross Tests

    [Test]
    public void GenerateFlatOrphanRowSvg_OutlineOnly_GeneratesOutlineDiamond()
    {
        var svg = TimelineSvgRenderer.GenerateFlatOrphanRowSvg(
            maxLanes: 1,
            nodeColor: "#3b82f6",
            isOutlineOnly: true);

        Assert.That(svg, Does.Contain("fill=\"none\""));
        Assert.That(svg, Does.Contain("stroke=\"#3b82f6\""));
    }

    [Test]
    public void GenerateFlatOrphanRowSvg_Filled_GeneratesFilledDiamond()
    {
        var svg = TimelineSvgRenderer.GenerateFlatOrphanRowSvg(
            maxLanes: 1,
            nodeColor: "#ef4444",
            isOutlineOnly: false);

        Assert.That(svg, Does.Contain("fill=\"#ef4444\""));
        Assert.That(svg, Does.Not.Contain("fill=\"none\""));
    }

    [Test]
    public void GenerateFlatOrphanRowSvg_WithErrorCross_IncludesCrossLines()
    {
        var svg = TimelineSvgRenderer.GenerateFlatOrphanRowSvg(
            maxLanes: 1,
            nodeColor: "#ef4444",
            showErrorCross: true);

        Assert.That(svg, Does.Contain("<line"));
        Assert.That(svg, Does.Contain("stroke=\"white\""));
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

    #region GenerateTaskGraphIssueSvg Tests

    [Test]
    public void GenerateTaskGraphIssueSvg_NoParent_OnlyDiamondNoConnectors()
    {
        var svg = TimelineSvgRenderer.GenerateTaskGraphIssueSvg(
            nodeLane: 0, parentLane: null, isFirstChild: false, maxLanes: 1,
            nodeColor: "#51A5C1", isOutlineOnly: true, isActionable: false);

        Assert.That(svg, Does.Contain("<svg"));
        Assert.That(svg, Does.Contain("<path"));
        // No horizontal or junction paths since no parent
        Assert.That(CountOccurrences(svg, "<path"), Is.EqualTo(1), "Should only have the diamond path");
    }

    [Test]
    public void GenerateTaskGraphIssueSvg_WithParent_FirstChild_MergedArcPath()
    {
        // Node at lane 0, parent at lane 1 — first child gets merged horizontal+arc+vertical path
        var svg = TimelineSvgRenderer.GenerateTaskGraphIssueSvg(
            nodeLane: 0, parentLane: 1, isFirstChild: true, maxLanes: 2,
            nodeColor: "#6b7280", isOutlineOnly: true, isActionable: false);

        var cx = TimelineSvgRenderer.GetLaneCenterX(0); // 12
        var px = TimelineSvgRenderer.GetLaneCenterX(1); // 36
        var startX = cx + TimelineSvgRenderer.DiamondSize + 2; // 21
        var r = TimelineSvgRenderer.TaskGraphArcRadius; // 7
        // Merged path: horizontal → arc elbow → vertical down
        // M 21 20 L 29 20 A 7 7 0 0 1 36 27 L 36 40
        Assert.That(svg, Does.Contain($"M {startX} 20 L {px - r} 20 A {r} {r} 0 0 1 {px} {20 + r} L {px} {TimelineSvgRenderer.RowHeight}"));
    }

    [Test]
    public void GenerateTaskGraphIssueSvg_WithParent_SubsequentChild_FullHeightJunction()
    {
        // Node at lane 0, parent at lane 1 — subsequent child gets full-height junction
        var svg = TimelineSvgRenderer.GenerateTaskGraphIssueSvg(
            nodeLane: 0, parentLane: 1, isFirstChild: false, maxLanes: 2,
            nodeColor: "#6b7280", isOutlineOnly: true, isActionable: false);

        var px = TimelineSvgRenderer.GetLaneCenterX(1); // 36
        // Full-height junction: M px 0 L px 40
        Assert.That(svg, Does.Contain($"M {px} 0 L {px} 40"));
    }

    [Test]
    public void GenerateTaskGraphIssueSvg_WithParent_FirstChild_DrawsMergedArcPath()
    {
        // Node at lane 0, parent at lane 2 — first child gets merged horizontal+arc+vertical
        var svg = TimelineSvgRenderer.GenerateTaskGraphIssueSvg(
            nodeLane: 0, parentLane: 2, isFirstChild: true, maxLanes: 3,
            nodeColor: "#6b7280", isOutlineOnly: true, isActionable: false);

        var cx = TimelineSvgRenderer.GetLaneCenterX(0); // 12
        var px = TimelineSvgRenderer.GetLaneCenterX(2); // 60
        var startX = cx + TimelineSvgRenderer.DiamondSize + 2; // 21
        var r = TimelineSvgRenderer.TaskGraphArcRadius; // 7
        // Merged path: M 21 20 L 53 20 A 7 7 0 0 1 60 27 L 60 40
        Assert.That(svg, Does.Contain($"M {startX} 20 L {px - r} 20 A {r} {r} 0 0 1 {px} {20 + r} L {px} {TimelineSvgRenderer.RowHeight}"));
    }

    [Test]
    public void GenerateTaskGraphIssueSvg_Actionable_ContainsGlowRing()
    {
        var svg = TimelineSvgRenderer.GenerateTaskGraphIssueSvg(
            nodeLane: 0, parentLane: null, isFirstChild: false, maxLanes: 1,
            nodeColor: "#51A5C1", isOutlineOnly: true, isActionable: true);

        Assert.That(svg, Does.Contain("opacity=\"0.4\""));
        Assert.That(svg, Does.Contain("<circle"));
    }

    [Test]
    public void GenerateTaskGraphIssueSvg_NotActionable_NoGlowRing()
    {
        var svg = TimelineSvgRenderer.GenerateTaskGraphIssueSvg(
            nodeLane: 0, parentLane: null, isFirstChild: false, maxLanes: 1,
            nodeColor: "#6b7280", isOutlineOnly: true, isActionable: false);

        Assert.That(svg, Does.Not.Contain("<circle"));
    }

    [Test]
    public void GenerateTaskGraphIssueSvg_Filled_ContainsFillColor()
    {
        var svg = TimelineSvgRenderer.GenerateTaskGraphIssueSvg(
            nodeLane: 0, parentLane: null, isFirstChild: false, maxLanes: 1,
            nodeColor: "#10b981", isOutlineOnly: false, isActionable: false);

        Assert.That(svg, Does.Contain("fill=\"#10b981\""));
        Assert.That(svg, Does.Not.Contain("fill=\"none\""));
    }

    [Test]
    public void GenerateTaskGraphIssueSvg_Outline_ContainsStrokeNoFill()
    {
        var svg = TimelineSvgRenderer.GenerateTaskGraphIssueSvg(
            nodeLane: 0, parentLane: null, isFirstChild: false, maxLanes: 1,
            nodeColor: "#51A5C1", isOutlineOnly: true, isActionable: false);

        Assert.That(svg, Does.Contain("fill=\"none\""));
        Assert.That(svg, Does.Contain("stroke=\"#51A5C1\""));
    }

    [Test]
    public void GenerateTaskGraphIssueSvg_SvgWidth_MatchesCalculateSvgWidth()
    {
        var maxLanes = 3;
        var expectedWidth = TimelineSvgRenderer.CalculateSvgWidth(maxLanes);

        var svg = TimelineSvgRenderer.GenerateTaskGraphIssueSvg(
            nodeLane: 0, parentLane: null, isFirstChild: false, maxLanes: maxLanes,
            nodeColor: "#6b7280", isOutlineOnly: true, isActionable: false);

        Assert.That(svg, Does.Contain($"width=\"{expectedWidth}\""));
        Assert.That(svg, Does.Contain($"height=\"{TimelineSvgRenderer.RowHeight}\""));
    }

    [Test]
    public void GenerateTaskGraphIssueSvg_NonZeroLane_CorrectCoordinates()
    {
        // Node at lane 2, no parent
        var svg = TimelineSvgRenderer.GenerateTaskGraphIssueSvg(
            nodeLane: 2, parentLane: null, isFirstChild: false, maxLanes: 3,
            nodeColor: "#6b7280", isOutlineOnly: true, isActionable: false);

        var cx = TimelineSvgRenderer.GetLaneCenterX(2); // 60
        var cy = TimelineSvgRenderer.GetRowCenterY(); // 20
        var s = TimelineSvgRenderer.DiamondSize; // 7
        // Diamond at lane 2: M 60 13
        Assert.That(svg, Does.Contain($"M {cx} {cy - s}"));
    }

    [Test]
    public void GenerateTaskGraphIssueSvg_WithOpacity_AppliesOpacityToDiamond()
    {
        var svg = TimelineSvgRenderer.GenerateTaskGraphIssueSvg(
            nodeLane: 0, parentLane: null, isFirstChild: false, maxLanes: 1,
            nodeColor: "#6b7280", isOutlineOnly: false, isActionable: false, opacity: 0.5);

        Assert.That(svg, Does.Contain("opacity=\"0.5\""));
    }

    [Test]
    public void GenerateTaskGraphIssueSvg_DrawTopLine_DrawsVerticalAboveDiamond()
    {
        // drawTopLine draws a vertical line from y=0 to cy - DiamondSize - 2 = 20 - 7 - 2 = 11
        var svg = TimelineSvgRenderer.GenerateTaskGraphIssueSvg(
            nodeLane: 1, parentLane: null, isFirstChild: false, maxLanes: 2,
            nodeColor: "#51A5C1", isOutlineOnly: true, isActionable: false, drawTopLine: true);

        var x = TimelineSvgRenderer.GetLaneCenterX(1); // 36
        Assert.That(svg, Does.Contain($"M {x} 0 L {x} 11"));
    }

    [Test]
    public void GenerateTaskGraphIssueSvg_NoDrawTopLine_NoTopVertical()
    {
        // Without drawTopLine, there should be no vertical line above the diamond
        var svg = TimelineSvgRenderer.GenerateTaskGraphIssueSvg(
            nodeLane: 1, parentLane: null, isFirstChild: false, maxLanes: 2,
            nodeColor: "#51A5C1", isOutlineOnly: true, isActionable: false, drawTopLine: false);

        var x = TimelineSvgRenderer.GetLaneCenterX(1); // 36
        Assert.That(svg, Does.Not.Contain($"M {x} 0 L {x} 11"));
    }

    [Test]
    public void GenerateTaskGraphIssueSvg_SeriesChild_SkipsHorizontalAndJunction()
    {
        // Series child should not have horizontal line or junction vertical
        var svg = TimelineSvgRenderer.GenerateTaskGraphIssueSvg(
            nodeLane: 0, parentLane: 1, isFirstChild: true, maxLanes: 2,
            nodeColor: "#6b7280", isOutlineOnly: true, isActionable: false, isSeriesChild: true);

        var cx = TimelineSvgRenderer.GetLaneCenterX(0); // 12
        var px = TimelineSvgRenderer.GetLaneCenterX(1); // 36
        var startX = cx + TimelineSvgRenderer.DiamondSize + 2; // 21

        // No horizontal line from diamond to parent lane
        Assert.That(svg, Does.Not.Contain($"M {startX} 20 L {px} 20"));
        // No junction vertical at parent lane
        Assert.That(svg, Does.Not.Contain($"M {px} 20 L {px} 40"));
        Assert.That(svg, Does.Not.Contain($"M {px} 0 L {px} 40"));
    }

    [Test]
    public void GenerateTaskGraphIssueSvg_DrawBottomLine_DrawsVerticalBelowDiamond()
    {
        // drawBottomLine draws a vertical line from cy + DiamondSize + 2 = 20 + 7 + 2 = 29 to RowHeight = 40
        var svg = TimelineSvgRenderer.GenerateTaskGraphIssueSvg(
            nodeLane: 1, parentLane: null, isFirstChild: false, maxLanes: 2,
            nodeColor: "#51A5C1", isOutlineOnly: true, isActionable: false, drawBottomLine: true);

        var x = TimelineSvgRenderer.GetLaneCenterX(1); // 36
        Assert.That(svg, Does.Contain($"M {x} 29 L {x} 40"));
    }

    [Test]
    public void GenerateTaskGraphIssueSvg_NoDrawBottomLine_NoBottomVertical()
    {
        var svg = TimelineSvgRenderer.GenerateTaskGraphIssueSvg(
            nodeLane: 1, parentLane: null, isFirstChild: false, maxLanes: 2,
            nodeColor: "#51A5C1", isOutlineOnly: true, isActionable: false, drawBottomLine: false);

        var x = TimelineSvgRenderer.GetLaneCenterX(1); // 36
        Assert.That(svg, Does.Not.Contain($"M {x} 29 L {x} 40"));
    }

    [Test]
    public void GenerateTaskGraphIssueSvg_SeriesChild_WithDrawTopAndBottom_FullVertical()
    {
        // Series child with both top and bottom lines draws both verticals
        var svg = TimelineSvgRenderer.GenerateTaskGraphIssueSvg(
            nodeLane: 0, parentLane: 1, isFirstChild: false, maxLanes: 2,
            nodeColor: "#6b7280", isOutlineOnly: true, isActionable: false,
            drawTopLine: true, drawBottomLine: true, isSeriesChild: true);

        var cx = TimelineSvgRenderer.GetLaneCenterX(0); // 12
        // Top: M 12 0 L 12 11
        Assert.That(svg, Does.Contain($"M {cx} 0 L {cx} 11"));
        // Bottom: M 12 29 L 12 40
        Assert.That(svg, Does.Contain($"M {cx} 29 L {cx} 40"));
        // No horizontal connector
        var px = TimelineSvgRenderer.GetLaneCenterX(1); // 36
        var startX = cx + TimelineSvgRenderer.DiamondSize + 2; // 21
        Assert.That(svg, Does.Not.Contain($"M {startX} 20 L {px} 20"));
    }

    [Test]
    public void GenerateTaskGraphIssueSvg_SeriesConnectorFromLane_DrawsLShapedPathWithArc()
    {
        // Parent at lane 1 receiving series children from lane 0
        var svg = TimelineSvgRenderer.GenerateTaskGraphIssueSvg(
            nodeLane: 1, parentLane: null, isFirstChild: false, maxLanes: 2,
            nodeColor: "#6b7280", isOutlineOnly: true, isActionable: false,
            seriesConnectorFromLane: 0);

        var fromX = TimelineSvgRenderer.GetLaneCenterX(0); // 12
        var cx = TimelineSvgRenderer.GetLaneCenterX(1); // 36
        var cy = TimelineSvgRenderer.GetRowCenterY(); // 20
        var nodeEdgeX = cx - TimelineSvgRenderer.DiamondSize - 2; // 27
        var r = TimelineSvgRenderer.TaskGraphArcRadius; // 7

        // L-shaped path with arc: down, arc turn, then horizontal to diamond's left edge
        // M 12 0 L 12 13 A 7 7 0 0 0 19 20 L 27 20
        Assert.That(svg, Does.Contain($"M {fromX} 0 L {fromX} {cy - r} A {r} {r} 0 0 0 {fromX + r} {cy} L {nodeEdgeX} {cy}"));
    }

    [Test]
    public void GenerateTaskGraphIssueSvg_SeriesConnectorAndParallelConnector_BothRenderWithArcs()
    {
        // Parent at lane 1 receiving series children from lane 0, AND connecting to grandparent at lane 2
        var svg = TimelineSvgRenderer.GenerateTaskGraphIssueSvg(
            nodeLane: 1, parentLane: 2, isFirstChild: true, maxLanes: 3,
            nodeColor: "#6b7280", isOutlineOnly: true, isActionable: false,
            seriesConnectorFromLane: 0);

        var fromX = TimelineSvgRenderer.GetLaneCenterX(0); // 12
        var cx = TimelineSvgRenderer.GetLaneCenterX(1); // 36
        var px = TimelineSvgRenderer.GetLaneCenterX(2); // 60
        var cy = TimelineSvgRenderer.GetRowCenterY(); // 20
        var nodeEdgeLeft = cx - TimelineSvgRenderer.DiamondSize - 2; // 27
        var nodeEdgeRight = cx + TimelineSvgRenderer.DiamondSize + 2; // 45
        var r = TimelineSvgRenderer.TaskGraphArcRadius; // 7

        // L-shaped path from series children with arc
        // M 12 0 L 12 13 A 7 7 0 0 0 19 20 L 27 20
        Assert.That(svg, Does.Contain($"M {fromX} 0 L {fromX} {cy - r} A {r} {r} 0 0 0 {fromX + r} {cy} L {nodeEdgeLeft} {cy}"),
            "Should have L-shaped connector with arc from series children");
        // Merged horizontal + arc + vertical to grandparent (first child)
        // M 45 20 L 53 20 A 7 7 0 0 1 60 27 L 60 40
        Assert.That(svg, Does.Contain($"M {nodeEdgeRight} {cy} L {px - r} {cy} A {r} {r} 0 0 1 {px} {cy + r} L {px} {TimelineSvgRenderer.RowHeight}"),
            "Should have merged arc path to grandparent lane");
    }

    #endregion

    #region GenerateTaskGraphCircleSvg Lane 0 Connector Tests

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
        // Should NOT have full vertical (it terminates at the arc)
        Assert.That(svg, Does.Not.Contain($"M {lane0X} 0 L {lane0X} {TimelineSvgRenderer.RowHeight}"),
            "Last connector should not have full vertical at lane 0");
    }

    [Test]
    public void GenerateTaskGraphCircleSvg_NoLane0Flags_NoLane0Lines()
    {
        var svg = TimelineSvgRenderer.GenerateTaskGraphCircleSvg(
            nodeLane: 1, parentLane: null, isFirstChild: false, maxLanes: 2,
            nodeColor: "#3b82f6", isOutlineOnly: false, isActionable: false);

        var lane0X = TimelineSvgRenderer.GetLaneCenterX(0); // 12
        Assert.That(svg, Does.Not.Contain($"M {lane0X} 0"),
            "Without lane 0 flags, should not draw anything at lane 0");
    }

    #endregion

    #region GenerateTaskGraphConnectorSvg Tests

    [Test]
    public void GenerateTaskGraphConnectorSvg_DefaultHeight_CorrectSvgAndPath()
    {
        var svg = TimelineSvgRenderer.GenerateTaskGraphConnectorSvg(lane: 1, maxLanes: 2);

        var x = TimelineSvgRenderer.GetLaneCenterX(1); // 36
        Assert.That(svg, Does.Contain("height=\"16\""));
        Assert.That(svg, Does.Contain($"M {x} 0 L {x} 16"));
        Assert.That(svg, Does.Contain("stroke=\"#6b7280\""));
    }

    [Test]
    public void GenerateTaskGraphConnectorSvg_CustomHeight_Respected()
    {
        var svg = TimelineSvgRenderer.GenerateTaskGraphConnectorSvg(lane: 0, maxLanes: 1, height: 24);

        var x = TimelineSvgRenderer.GetLaneCenterX(0); // 12
        Assert.That(svg, Does.Contain("height=\"24\""));
        Assert.That(svg, Does.Contain($"M {x} 0 L {x} 24"));
    }

    [Test]
    public void GenerateTaskGraphConnectorSvg_SvgWidth_MatchesCalculateSvgWidth()
    {
        var maxLanes = 3;
        var expectedWidth = TimelineSvgRenderer.CalculateSvgWidth(maxLanes);

        var svg = TimelineSvgRenderer.GenerateTaskGraphConnectorSvg(lane: 1, maxLanes: maxLanes);

        Assert.That(svg, Does.Contain($"width=\"{expectedWidth}\""));
    }

    #endregion
}
