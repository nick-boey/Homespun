using Homespun.Client.Components;

namespace Homespun.Tests.Components;

/// <summary>
/// Tests for CurrentPullRequestDetailPanel session refresh behavior.
/// Verifies that the component implements IAsyncDisposable for proper SignalR cleanup.
/// </summary>
[TestFixture]
public class CurrentPullRequestDetailPanelSessionRefreshTests
{
    [Test]
    public void CurrentPullRequestDetailPanel_ImplementsIAsyncDisposable()
    {
        // Assert - verify the component implements IAsyncDisposable for SignalR cleanup
        Assert.That(typeof(IAsyncDisposable).IsAssignableFrom(typeof(CurrentPullRequestDetailPanel)),
            "CurrentPullRequestDetailPanel should implement IAsyncDisposable for proper SignalR cleanup");
    }
}
