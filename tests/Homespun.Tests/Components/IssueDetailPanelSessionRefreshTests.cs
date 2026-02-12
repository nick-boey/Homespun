using Homespun.Client.Components;

namespace Homespun.Tests.Components;

/// <summary>
/// Tests for IssueDetailPanel session refresh behavior.
/// Verifies that the component implements IAsyncDisposable for proper SignalR cleanup.
/// </summary>
[TestFixture]
public class IssueDetailPanelSessionRefreshTests
{
    [Test]
    public void IssueDetailPanel_ImplementsIAsyncDisposable()
    {
        // Assert - verify the component implements IAsyncDisposable for SignalR cleanup
        Assert.That(typeof(IAsyncDisposable).IsAssignableFrom(typeof(IssueDetailPanel)),
            "IssueDetailPanel should implement IAsyncDisposable for proper SignalR cleanup");
    }
}
