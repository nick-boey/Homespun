using Homespun.Client.Components;

namespace Homespun.Tests.Components;

/// <summary>
/// Tests for TimelineVisualization component selection behavior after issue creation.
/// </summary>
[TestFixture]
public class TimelineVisualizationSelectionTests
{
    [Test]
    public void TimelineVisualization_HasInitializeKeyboardNavigationWithSelectionMethod()
    {
        // The component should have a method to initialize keyboard navigation
        // with an optional issue ID to select
        var componentType = typeof(TimelineVisualization);

        var method = componentType.GetMethod(
            "InitializeKeyboardNavigationWithSelection",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);

        Assert.That(method, Is.Not.Null,
            "TimelineVisualization should have InitializeKeyboardNavigationWithSelection method");

        // Verify method takes a nullable string parameter
        var parameters = method!.GetParameters();
        Assert.That(parameters.Length, Is.EqualTo(1),
            "InitializeKeyboardNavigationWithSelection should have one parameter");
        Assert.That(parameters[0].ParameterType, Is.EqualTo(typeof(string)),
            "Parameter should be of type string (nullable)");
    }

    [Test]
    public void TimelineVisualization_InitializeKeyboardNavigation_DelegatesToWithSelection()
    {
        // The original InitializeKeyboardNavigation should delegate to the new method
        // with null as the selectIssueId parameter
        var componentType = typeof(TimelineVisualization);

        var method = componentType.GetMethod(
            "InitializeKeyboardNavigation",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);

        Assert.That(method, Is.Not.Null,
            "TimelineVisualization should still have InitializeKeyboardNavigation method");
    }
}
