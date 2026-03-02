using Homespun.Client.Features.Issues.Components;

namespace Homespun.Tests.Components;

/// <summary>
/// Tests for InlineIssueDetailRow component.
/// Verifies the component implements IAsyncDisposable for proper SignalR cleanup.
/// </summary>
[TestFixture]
public class InlineIssueDetailRowTests
{
    [Test]
    public void InlineIssueDetailRow_ImplementsIAsyncDisposable()
    {
        // Assert - verify the component implements IAsyncDisposable for SignalR cleanup
        Assert.That(typeof(IAsyncDisposable).IsAssignableFrom(typeof(InlineIssueDetailRow)),
            "InlineIssueDetailRow should implement IAsyncDisposable for proper SignalR cleanup");
    }

    [Test]
    public void InlineIssueDetailRow_HasRequiredParameters()
    {
        // Verify the component has required parameters
        var properties = typeof(InlineIssueDetailRow).GetProperties();

        // Check for ProjectId parameter
        var projectIdProperty = properties.FirstOrDefault(p => p.Name == "ProjectId");
        Assert.That(projectIdProperty, Is.Not.Null, "InlineIssueDetailRow should have a ProjectId parameter");

        // Check for Issue parameter
        var issueProperty = properties.FirstOrDefault(p => p.Name == "Issue");
        Assert.That(issueProperty, Is.Not.Null, "InlineIssueDetailRow should have an Issue parameter");

        // Check for OnClose callback
        var onCloseProperty = properties.FirstOrDefault(p => p.Name == "OnClose");
        Assert.That(onCloseProperty, Is.Not.Null, "InlineIssueDetailRow should have an OnClose callback");

        // Check for OnActionCompleted callback
        var onActionCompletedProperty = properties.FirstOrDefault(p => p.Name == "OnActionCompleted");
        Assert.That(onActionCompletedProperty, Is.Not.Null, "InlineIssueDetailRow should have an OnActionCompleted callback");
    }
}
