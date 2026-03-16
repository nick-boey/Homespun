using System.Text.RegularExpressions;
using Homespun.Shared.Models.Gitgraph;
using Homespun.Shared.Models.Sessions;

namespace Homespun.Tests.Shared.Models.Gitgraph;

/// <summary>
/// Tests for the TimelineSvgRenderer's agent status ring functionality.
/// </summary>
[TestFixture]
public class TimelineSvgRendererTests
{
    private const int TestNodeLane = 1;
    private const int TestMaxLanes = 3;
    private const string TestNodeColor = "#3b82f6";

    #region Agent Status Ring Tests

    [Test]
    public void GenerateTaskGraphCircleSvg_WithNullAgentStatus_DoesNotRenderRing()
    {
        // Arrange & Act
        var svg = TimelineSvgRenderer.GenerateTaskGraphCircleSvg(
            nodeLane: TestNodeLane,
            parentLane: null,
            isFirstChild: false,
            maxLanes: TestMaxLanes,
            nodeColor: TestNodeColor,
            isOutlineOnly: false,
            isActionable: false,
            agentStatus: null);

        // Assert - Should only have the main node circle, no ring
        var circleCount = CountSvgElements(svg, "circle");
        Assert.That(circleCount, Is.EqualTo(1), "Should only render the main node circle");
    }

    [Test]
    public void GenerateTaskGraphCircleSvg_WithInactiveAgent_DoesNotRenderRing()
    {
        // Arrange
        var agentStatus = new AgentStatusData
        {
            IsActive = false,
            Status = ClaudeSessionStatus.Running,
            SessionId = "test-session"
        };

        // Act
        var svg = TimelineSvgRenderer.GenerateTaskGraphCircleSvg(
            nodeLane: TestNodeLane,
            parentLane: null,
            isFirstChild: false,
            maxLanes: TestMaxLanes,
            nodeColor: TestNodeColor,
            isOutlineOnly: false,
            isActionable: false,
            agentStatus: agentStatus);

        // Assert - Should only have the main node circle, no ring
        var circleCount = CountSvgElements(svg, "circle");
        Assert.That(circleCount, Is.EqualTo(1), "Should only render the main node circle");
    }

    [Test]
    [TestCase(ClaudeSessionStatus.Starting, "#3b82f6")] // Blue
    [TestCase(ClaudeSessionStatus.RunningHooks, "#3b82f6")] // Blue
    [TestCase(ClaudeSessionStatus.Running, "#3b82f6")] // Blue
    public void GenerateTaskGraphCircleSvg_WithRunningStatus_RendersBlueRing(ClaudeSessionStatus status, string expectedColor)
    {
        // Arrange
        var agentStatus = new AgentStatusData
        {
            IsActive = true,
            Status = status,
            SessionId = "test-session"
        };

        // Act
        var svg = TimelineSvgRenderer.GenerateTaskGraphCircleSvg(
            nodeLane: TestNodeLane,
            parentLane: null,
            isFirstChild: false,
            maxLanes: TestMaxLanes,
            nodeColor: TestNodeColor,
            isOutlineOnly: false,
            isActionable: false,
            agentStatus: agentStatus);

        // Assert
        var circleCount = CountSvgElements(svg, "circle");
        Assert.That(circleCount, Is.EqualTo(2), "Should render both ring and node");

        AssertRingHasColor(svg, expectedColor);
        AssertRingHasAnimation(svg);
    }

    [Test]
    [TestCase(ClaudeSessionStatus.WaitingForInput, "#eab308")] // Yellow
    [TestCase(ClaudeSessionStatus.WaitingForQuestionAnswer, "#eab308")] // Yellow
    [TestCase(ClaudeSessionStatus.WaitingForPlanExecution, "#eab308")] // Yellow
    public void GenerateTaskGraphCircleSvg_WithWaitingStatus_RendersYellowRing(ClaudeSessionStatus status, string expectedColor)
    {
        // Arrange
        var agentStatus = new AgentStatusData
        {
            IsActive = true,
            Status = status,
            SessionId = "test-session"
        };

        // Act
        var svg = TimelineSvgRenderer.GenerateTaskGraphCircleSvg(
            nodeLane: TestNodeLane,
            parentLane: null,
            isFirstChild: false,
            maxLanes: TestMaxLanes,
            nodeColor: TestNodeColor,
            isOutlineOnly: false,
            isActionable: false,
            agentStatus: agentStatus);

        // Assert
        var circleCount = CountSvgElements(svg, "circle");
        Assert.That(circleCount, Is.EqualTo(2), "Should render both ring and node");

        AssertRingHasColor(svg, expectedColor);
        AssertRingHasAnimation(svg);
    }

    [Test]
    public void GenerateTaskGraphCircleSvg_WithErrorStatus_RendersRedRing()
    {
        // Arrange
        var agentStatus = new AgentStatusData
        {
            IsActive = true,
            Status = ClaudeSessionStatus.Error,
            SessionId = "test-session"
        };

        // Act
        var svg = TimelineSvgRenderer.GenerateTaskGraphCircleSvg(
            nodeLane: TestNodeLane,
            parentLane: null,
            isFirstChild: false,
            maxLanes: TestMaxLanes,
            nodeColor: TestNodeColor,
            isOutlineOnly: false,
            isActionable: false,
            agentStatus: agentStatus);

        // Assert
        var circleCount = CountSvgElements(svg, "circle");
        Assert.That(circleCount, Is.EqualTo(2), "Should render both ring and node");

        AssertRingHasColor(svg, "#ef4444"); // Red
        AssertRingHasAnimation(svg);
    }

    [Test]
    public void GenerateTaskGraphCircleSvg_WithStoppedStatus_DoesNotRenderRing()
    {
        // Arrange
        var agentStatus = new AgentStatusData
        {
            IsActive = true,
            Status = ClaudeSessionStatus.Stopped,
            SessionId = "test-session"
        };

        // Act
        var svg = TimelineSvgRenderer.GenerateTaskGraphCircleSvg(
            nodeLane: TestNodeLane,
            parentLane: null,
            isFirstChild: false,
            maxLanes: TestMaxLanes,
            nodeColor: TestNodeColor,
            isOutlineOnly: false,
            isActionable: false,
            agentStatus: agentStatus);

        // Assert - Should only have the main node circle, no ring
        var circleCount = CountSvgElements(svg, "circle");
        Assert.That(circleCount, Is.EqualTo(1), "Should only render the main node circle");
    }

    [Test]
    public void GenerateTaskGraphCircleSvg_WithAgentStatusRing_HasCorrectRadius()
    {
        // Arrange
        var agentStatus = new AgentStatusData
        {
            IsActive = true,
            Status = ClaudeSessionStatus.Running,
            SessionId = "test-session"
        };

        // Act
        var svg = TimelineSvgRenderer.GenerateTaskGraphCircleSvg(
            nodeLane: TestNodeLane,
            parentLane: null,
            isFirstChild: false,
            maxLanes: TestMaxLanes,
            nodeColor: TestNodeColor,
            isOutlineOnly: false,
            isActionable: false,
            agentStatus: agentStatus);

        // Assert - Ring should be 4 pixels larger than node
        var ringRadius = ExtractRingRadius(svg);
        var nodeRadius = ExtractNodeRadius(svg);
        Assert.That(ringRadius, Is.EqualTo(nodeRadius + 4), "Ring should be 4 pixels larger than node");
    }

    [Test]
    public void GenerateTaskGraphCircleSvg_WithAgentStatusRing_HasCorrectOpacity()
    {
        // Arrange
        var agentStatus = new AgentStatusData
        {
            IsActive = true,
            Status = ClaudeSessionStatus.Running,
            SessionId = "test-session"
        };

        // Act
        var svg = TimelineSvgRenderer.GenerateTaskGraphCircleSvg(
            nodeLane: TestNodeLane,
            parentLane: null,
            isFirstChild: false,
            maxLanes: TestMaxLanes,
            nodeColor: TestNodeColor,
            isOutlineOnly: false,
            isActionable: false,
            agentStatus: agentStatus);

        // Assert
        AssertRingHasOpacity(svg, "0.6");
    }

    [Test]
    public void GenerateTaskGraphCircleSvg_WithAgentStatusRing_HasCorrectStrokeWidth()
    {
        // Arrange
        var agentStatus = new AgentStatusData
        {
            IsActive = true,
            Status = ClaudeSessionStatus.Running,
            SessionId = "test-session"
        };

        // Act
        var svg = TimelineSvgRenderer.GenerateTaskGraphCircleSvg(
            nodeLane: TestNodeLane,
            parentLane: null,
            isFirstChild: false,
            maxLanes: TestMaxLanes,
            nodeColor: TestNodeColor,
            isOutlineOnly: false,
            isActionable: false,
            agentStatus: agentStatus);

        // Assert
        AssertRingHasStrokeWidth(svg, "2");
    }

    #endregion

    #region Helper Methods

    private static int CountSvgElements(string svg, string elementName)
    {
        var pattern = $"<{elementName}\\s+[^>]*>";
        return Regex.Matches(svg, pattern).Count;
    }

    private static void AssertRingHasColor(string svg, string expectedColor)
    {
        // First circle should be the ring
        var firstCircleMatch = Regex.Match(svg, @"<circle[^>]+>");
        Assert.That(firstCircleMatch.Success, Is.True, "Should find a circle element");

        var strokeMatch = Regex.Match(firstCircleMatch.Value, @"stroke=""([^""]+)""");
        Assert.That(strokeMatch.Success, Is.True, "Ring should have stroke attribute");
        Assert.That(strokeMatch.Groups[1].Value, Is.EqualTo(expectedColor), $"Ring should have {expectedColor} stroke");
    }

    private static void AssertRingHasAnimation(string svg)
    {
        // Check for animate element within first circle
        Assert.That(svg, Does.Contain("<animate"), "Ring should have animation");
        Assert.That(svg, Does.Contain("attributeName=\"opacity\""), "Animation should animate opacity");
        Assert.That(svg, Does.Contain("values=\"0.6;1;0.6\""), "Animation should pulse between 0.6 and 1");
        Assert.That(svg, Does.Contain("dur=\"2s\""), "Animation should have 2s duration");
        Assert.That(svg, Does.Contain("repeatCount=\"indefinite\""), "Animation should repeat indefinitely");
    }

    private static void AssertRingHasOpacity(string svg, string expectedOpacity)
    {
        var firstCircleMatch = Regex.Match(svg, @"<circle[^>]+>");
        Assert.That(firstCircleMatch.Success, Is.True, "Should find a circle element");

        var opacityMatch = Regex.Match(firstCircleMatch.Value, @"opacity=""([^""]+)""");
        Assert.That(opacityMatch.Success, Is.True, "Ring should have opacity attribute");
        Assert.That(opacityMatch.Groups[1].Value, Is.EqualTo(expectedOpacity), $"Ring should have {expectedOpacity} opacity");
    }

    private static void AssertRingHasStrokeWidth(string svg, string expectedWidth)
    {
        var firstCircleMatch = Regex.Match(svg, @"<circle[^>]+>");
        Assert.That(firstCircleMatch.Success, Is.True, "Should find a circle element");

        var strokeWidthMatch = Regex.Match(firstCircleMatch.Value, @"stroke-width=""([^""]+)""");
        Assert.That(strokeWidthMatch.Success, Is.True, "Ring should have stroke-width attribute");
        Assert.That(strokeWidthMatch.Groups[1].Value, Is.EqualTo(expectedWidth), $"Ring should have {expectedWidth} stroke-width");
    }

    private static int ExtractRingRadius(string svg)
    {
        // First circle is the ring
        var firstCircleMatch = Regex.Match(svg, @"<circle[^>]+r=""(\d+)""");
        Assert.That(firstCircleMatch.Success, Is.True, "Should find ring circle with radius");
        return int.Parse(firstCircleMatch.Groups[1].Value);
    }

    private static int ExtractNodeRadius(string svg)
    {
        // Second circle is the node - find all circles and get the second one
        var circleMatches = Regex.Matches(svg, @"<circle[^>]+r=""(\d+)""");
        Assert.That(circleMatches.Count, Is.GreaterThan(1), "Should find at least two circles");
        return int.Parse(circleMatches[1].Groups[1].Value);
    }

    #endregion
}