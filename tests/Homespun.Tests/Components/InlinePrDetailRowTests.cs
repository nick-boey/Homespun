using Homespun.Client.Features.Issues.Components;

namespace Homespun.Tests.Components;

/// <summary>
/// Tests for InlinePrDetailRow component.
/// Verifies the component implements IAsyncDisposable for proper SignalR cleanup.
/// </summary>
[TestFixture]
public class InlinePrDetailRowTests
{
    [Test]
    public void InlinePrDetailRow_ImplementsIAsyncDisposable()
    {
        // Assert - verify the component implements IAsyncDisposable for SignalR cleanup
        Assert.That(typeof(IAsyncDisposable).IsAssignableFrom(typeof(InlinePrDetailRow)),
            "InlinePrDetailRow should implement IAsyncDisposable for proper SignalR cleanup");
    }

    [Test]
    public void InlinePrDetailRow_HasRequiredParameters()
    {
        // Verify the component has required parameters
        var properties = typeof(InlinePrDetailRow).GetProperties();

        // Check for ProjectId parameter
        var projectIdProperty = properties.FirstOrDefault(p => p.Name == "ProjectId");
        Assert.That(projectIdProperty, Is.Not.Null, "InlinePrDetailRow should have a ProjectId parameter");

        // Check for CurrentPr parameter
        var currentPrProperty = properties.FirstOrDefault(p => p.Name == "CurrentPr");
        Assert.That(currentPrProperty, Is.Not.Null, "InlinePrDetailRow should have a CurrentPr parameter");

        // Check for MergedPr parameter
        var mergedPrProperty = properties.FirstOrDefault(p => p.Name == "MergedPr");
        Assert.That(mergedPrProperty, Is.Not.Null, "InlinePrDetailRow should have a MergedPr parameter");

        // Check for OnClose callback
        var onCloseProperty = properties.FirstOrDefault(p => p.Name == "OnClose");
        Assert.That(onCloseProperty, Is.Not.Null, "InlinePrDetailRow should have an OnClose callback");

        // Check for OnActionCompleted callback
        var onActionCompletedProperty = properties.FirstOrDefault(p => p.Name == "OnActionCompleted");
        Assert.That(onActionCompletedProperty, Is.Not.Null, "InlinePrDetailRow should have an OnActionCompleted callback");
    }

    [Test]
    public void InlinePrDetailRow_HandlesBothCurrentAndMergedPrs()
    {
        // Verify the component can handle both current and merged PRs via separate parameters
        var properties = typeof(InlinePrDetailRow).GetProperties();

        var currentPrProperty = properties.FirstOrDefault(p => p.Name == "CurrentPr");
        Assert.That(currentPrProperty, Is.Not.Null, "InlinePrDetailRow should have a CurrentPr parameter");

        var mergedPrProperty = properties.FirstOrDefault(p => p.Name == "MergedPr");
        Assert.That(mergedPrProperty, Is.Not.Null, "InlinePrDetailRow should have a MergedPr parameter");
    }
}
