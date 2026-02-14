using Homespun.Client.Components;

namespace Homespun.Tests.Components;

/// <summary>
/// Tests for TimelineVisualization component parameter change handling.
/// Verifies that the component properly handles ProjectId parameter changes
/// when navigating between projects via the sidebar.
/// </summary>
[TestFixture]
public class TimelineVisualizationParameterTests
{
    [Test]
    public void TimelineVisualization_ImplementsIAsyncDisposable()
    {
        // Assert - verify the component implements IAsyncDisposable for proper SignalR cleanup
        Assert.That(typeof(IAsyncDisposable).IsAssignableFrom(typeof(TimelineVisualization)),
            "TimelineVisualization should implement IAsyncDisposable for proper SignalR cleanup");
    }

    [Test]
    public void TimelineVisualization_OverridesOnParametersSetAsync_ForProjectIdChanges()
    {
        // The component must override OnParametersSetAsync to handle ProjectId changes
        // when navigating between projects via the sidebar
        var componentType = typeof(TimelineVisualization);

        // Check for OnParametersSetAsync override - it should be declared on TimelineVisualization itself
        var method = componentType.GetMethod(
            "OnParametersSetAsync",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.DeclaredOnly);

        Assert.That(method, Is.Not.Null,
            "TimelineVisualization must override OnParametersSetAsync to handle ProjectId parameter changes when switching projects via sidebar");
    }

    [Test]
    public void TimelineVisualization_HasProjectIdTrackingField()
    {
        // The component should have a field to track the previous ProjectId
        // to detect when the parameter changes
        var componentType = typeof(TimelineVisualization);

        var field = componentType.GetField(
            "_previousProjectId",
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic);

        Assert.That(field, Is.Not.Null,
            "TimelineVisualization should have a _previousProjectId field to track parameter changes");
    }
}
