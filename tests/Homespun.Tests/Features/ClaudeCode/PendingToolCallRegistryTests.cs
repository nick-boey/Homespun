using Homespun.Features.ClaudeCode.Services;

namespace Homespun.Tests.Features.ClaudeCode;

/// <summary>
/// Tests for <see cref="PendingToolCallRegistry"/>: the per-session slot that
/// bridges the translator's <c>TOOL_CALL_START</c> emission and the hub's
/// <c>TOOL_CALL_RESULT</c> synthesis.
/// </summary>
[TestFixture]
public class PendingToolCallRegistryTests
{
    private PendingToolCallRegistry _registry = null!;

    [SetUp]
    public void SetUp()
    {
        _registry = new PendingToolCallRegistry();
    }

    [Test]
    public void Register_ThenDequeue_ReturnsTheRegisteredId()
    {
        _registry.Register("session-1", "tool-42");

        var id = _registry.Dequeue("session-1");

        Assert.That(id, Is.EqualTo("tool-42"));
    }

    [Test]
    public void Dequeue_ClearsTheSlot()
    {
        _registry.Register("session-1", "tool-42");
        _registry.Dequeue("session-1");

        Assert.That(_registry.Dequeue("session-1"), Is.Null);
    }

    [Test]
    public void Dequeue_OnEmptySession_ReturnsNull()
    {
        Assert.That(_registry.Dequeue("unknown-session"), Is.Null);
    }

    [Test]
    public void DoubleRegister_Overwrites_LastWriteWins()
    {
        // Documented semantics: if a second input-required arrives before the first
        // resolves, the newer tool-call id replaces the older one. The old id is
        // orphaned — acceptable because the user's UI has moved on to the new prompt.
        _registry.Register("session-1", "tool-first");
        _registry.Register("session-1", "tool-second");

        Assert.That(_registry.Dequeue("session-1"), Is.EqualTo("tool-second"));
    }

    [Test]
    public void Register_DifferentSessions_KeepsSlotsIndependent()
    {
        _registry.Register("session-a", "tool-a");
        _registry.Register("session-b", "tool-b");

        Assert.Multiple(() =>
        {
            Assert.That(_registry.Dequeue("session-a"), Is.EqualTo("tool-a"));
            Assert.That(_registry.Dequeue("session-b"), Is.EqualTo("tool-b"));
        });
    }
}
