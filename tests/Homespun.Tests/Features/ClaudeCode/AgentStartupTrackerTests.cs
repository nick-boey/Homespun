using Homespun.Features.ClaudeCode.Services;

namespace Homespun.Tests.Features.ClaudeCode;

[TestFixture]
public class AgentStartupTrackerTests
{
    private AgentStartupTracker _tracker = null!;

    [SetUp]
    public void SetUp()
    {
        _tracker = new AgentStartupTracker();
    }

    [Test]
    public void TryMarkAsStarting_FirstCall_ReturnsTrue()
    {
        // Act
        var result = _tracker.TryMarkAsStarting("entity-1");

        // Assert
        Assert.That(result, Is.True);
        Assert.That(_tracker.IsStarting("entity-1"), Is.True);
    }

    [Test]
    public void TryMarkAsStarting_SecondCallSameEntity_ReturnsFalse()
    {
        // Arrange
        _tracker.TryMarkAsStarting("entity-1");

        // Act
        var result = _tracker.TryMarkAsStarting("entity-1");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void TryMarkAsStarting_DifferentEntities_BothReturnTrue()
    {
        // Act
        var result1 = _tracker.TryMarkAsStarting("entity-1");
        var result2 = _tracker.TryMarkAsStarting("entity-2");

        // Assert
        Assert.That(result1, Is.True);
        Assert.That(result2, Is.True);
        Assert.That(_tracker.IsStarting("entity-1"), Is.True);
        Assert.That(_tracker.IsStarting("entity-2"), Is.True);
    }

    [Test]
    public void TryMarkAsStarting_AfterClear_CanMarkAgain()
    {
        // Arrange
        _tracker.TryMarkAsStarting("entity-1");
        _tracker.Clear("entity-1");

        // Act
        var result = _tracker.TryMarkAsStarting("entity-1");

        // Assert
        Assert.That(result, Is.True);
    }

    [Test]
    public void TryMarkAsStarting_AfterMarkAsStarted_StillBlocked()
    {
        // Arrange
        _tracker.TryMarkAsStarting("entity-1");
        _tracker.MarkAsStarted("entity-1");

        // Act - entity is still tracked (as Started), so TryAdd should fail
        var result = _tracker.TryMarkAsStarting("entity-1");

        // Assert
        Assert.That(result, Is.False);
    }

    [Test]
    public void TryMarkAsStarting_FiresOnStateChanged()
    {
        // Arrange
        string? firedEntityId = null;
        AgentStartupState? firedState = null;
        _tracker.OnStateChanged += (entityId, state) =>
        {
            firedEntityId = entityId;
            firedState = state;
        };

        // Act
        _tracker.TryMarkAsStarting("entity-1");

        // Assert
        Assert.That(firedEntityId, Is.EqualTo("entity-1"));
        Assert.That(firedState, Is.Not.Null);
        Assert.That(firedState!.Status, Is.EqualTo(AgentStartupStatus.Starting));
    }

    [Test]
    public void TryMarkAsStarting_WhenBlocked_DoesNotFireEvent()
    {
        // Arrange
        _tracker.TryMarkAsStarting("entity-1");
        var eventFired = false;
        _tracker.OnStateChanged += (_, _) => eventFired = true;

        // Act
        _tracker.TryMarkAsStarting("entity-1");

        // Assert
        Assert.That(eventFired, Is.False);
    }

    [Test]
    public void MarkAsFailed_AllowsRetryAfterClear()
    {
        // Arrange
        _tracker.TryMarkAsStarting("entity-1");
        _tracker.MarkAsFailed("entity-1", "some error");

        // Verify it's still tracked (blocked)
        Assert.That(_tracker.TryMarkAsStarting("entity-1"), Is.False);

        // Act - clear to allow retry
        _tracker.Clear("entity-1");
        var result = _tracker.TryMarkAsStarting("entity-1");

        // Assert
        Assert.That(result, Is.True);
        Assert.That(_tracker.IsStarting("entity-1"), Is.True);
    }
}
