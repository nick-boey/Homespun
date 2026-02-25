using Homespun.Client.Components;
using NUnit.Framework;

namespace Homespun.Tests.Components;

/// <summary>
/// Tests for SessionsPanel component behavior.
/// Verifies that the component implements IAsyncDisposable for proper SignalR cleanup.
/// Note: Logic tests for helper methods have been moved to SessionsPanelTests.cs.
/// </summary>
[TestFixture]
public class SessionsPanelSignalRTests
{
    [Test]
    public void SessionsPanel_ImplementsIAsyncDisposable()
    {
        // Assert - verify the component implements IAsyncDisposable for SignalR cleanup
        Assert.That(typeof(IAsyncDisposable).IsAssignableFrom(typeof(SessionsPanel)),
            "SessionsPanel should implement IAsyncDisposable for proper SignalR cleanup");
    }
}
