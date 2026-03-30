using Homespun.Features.ClaudeCode.Services;
using NUnit.Framework;

namespace Homespun.Tests.Features.ClaudeCode;

[TestFixture]
public class SessionStateManagerTests
{
    private SessionStateManager _manager = null!;

    [SetUp]
    public void SetUp()
    {
        _manager = new SessionStateManager();
    }

    // --- CTS ---

    [Test]
    public void GetOrCreateCts_ReturnsNewCts()
    {
        var cts = _manager.GetOrCreateCts("s1");
        Assert.That(cts, Is.Not.Null);
        Assert.That(cts.IsCancellationRequested, Is.False);
    }

    [Test]
    public void GetOrCreateCts_ReturnsSameInstanceOnSecondCall()
    {
        var cts1 = _manager.GetOrCreateCts("s1");
        var cts2 = _manager.GetOrCreateCts("s1");
        Assert.That(cts2, Is.SameAs(cts1));
    }

    [Test]
    public void GetCts_ReturnsNullWhenNotSet()
    {
        Assert.That(_manager.GetCts("missing"), Is.Null);
    }

    [Test]
    public void TryRemoveCts_RemovesAndReturns()
    {
        _manager.GetOrCreateCts("s1");
        var removed = _manager.TryRemoveCts("s1", out var cts);
        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.True);
            Assert.That(cts, Is.Not.Null);
            Assert.That(_manager.GetCts("s1"), Is.Null);
        });
    }

    [Test]
    public async Task CancelAndRemoveCtsAsync_CancelsAndDisposes()
    {
        var cts = _manager.GetOrCreateCts("s1");
        await _manager.CancelAndRemoveCtsAsync("s1");
        Assert.Multiple(() =>
        {
            Assert.That(cts.IsCancellationRequested, Is.True);
            Assert.That(_manager.GetCts("s1"), Is.Null);
        });
    }

    [Test]
    public async Task CancelAndRemoveCtsAsync_NoOpForMissing()
    {
        await _manager.CancelAndRemoveCtsAsync("missing");
        Assert.That(_manager.GetCts("missing"), Is.Null);
    }

    // --- Run IDs ---

    [Test]
    public void GetRunId_ReturnsNullWhenNotSet()
    {
        Assert.That(_manager.GetRunId("s1"), Is.Null);
    }

    [Test]
    public void SetAndGetRunId_Works()
    {
        _manager.SetRunId("s1", "run-1");
        Assert.That(_manager.GetRunId("s1"), Is.EqualTo("run-1"));
    }

    [Test]
    public void RemoveRunId_Clears()
    {
        _manager.SetRunId("s1", "run-1");
        _manager.RemoveRunId("s1");
        Assert.That(_manager.GetRunId("s1"), Is.Null);
    }

    // --- Turn IDs ---

    [Test]
    public void GetCurrentTurnId_ReturnsNullWhenNotSet()
    {
        Assert.That(_manager.GetCurrentTurnId("s1"), Is.Null);
    }

    [Test]
    public void SetAndGetCurrentTurnId_Works()
    {
        var turnId = Guid.NewGuid();
        _manager.SetCurrentTurnId("s1", turnId);
        Assert.That(_manager.GetCurrentTurnId("s1"), Is.EqualTo(turnId));
    }

    [Test]
    public void IsTurnActive_ReturnsTrueForMatchingTurn()
    {
        var turnId = Guid.NewGuid();
        _manager.SetCurrentTurnId("s1", turnId);
        Assert.That(_manager.IsTurnActive("s1", turnId), Is.True);
    }

    [Test]
    public void IsTurnActive_ReturnsFalseForDifferentTurn()
    {
        _manager.SetCurrentTurnId("s1", Guid.NewGuid());
        Assert.That(_manager.IsTurnActive("s1", Guid.NewGuid()), Is.False);
    }

    [Test]
    public void IsTurnActive_ReturnsFalseWhenNotSet()
    {
        Assert.That(_manager.IsTurnActive("s1", Guid.NewGuid()), Is.False);
    }

    [Test]
    public void RemoveTurnId_Clears()
    {
        _manager.SetCurrentTurnId("s1", Guid.NewGuid());
        _manager.RemoveTurnId("s1");
        Assert.That(_manager.GetCurrentTurnId("s1"), Is.Null);
    }

    // --- Agent session IDs ---

    [Test]
    public void GetAgentSessionId_ReturnsNullWhenNotSet()
    {
        Assert.That(_manager.GetAgentSessionId("s1"), Is.Null);
    }

    [Test]
    public void SetAndGetAgentSessionId_Works()
    {
        _manager.SetAgentSessionId("s1", "agent-1");
        Assert.That(_manager.GetAgentSessionId("s1"), Is.EqualTo("agent-1"));
    }

    [Test]
    public void TryRemoveAgentSessionId_RemovesAndReturns()
    {
        _manager.SetAgentSessionId("s1", "agent-1");
        var removed = _manager.TryRemoveAgentSessionId("s1", out var agentId);
        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.True);
            Assert.That(agentId, Is.EqualTo("agent-1"));
            Assert.That(_manager.GetAgentSessionId("s1"), Is.Null);
        });
    }

    // --- Tool uses ---

    [Test]
    public void GetOrCreateSessionToolUses_CreatesNewDictionary()
    {
        var toolUses = _manager.GetOrCreateSessionToolUses("s1");
        Assert.That(toolUses, Is.Not.Null);
        Assert.That(toolUses, Is.Empty);
    }

    [Test]
    public void GetOrCreateSessionToolUses_ReturnsSameInstanceOnSecondCall()
    {
        var first = _manager.GetOrCreateSessionToolUses("s1");
        first["tool-1"] = "Write";
        var second = _manager.GetOrCreateSessionToolUses("s1");
        Assert.That(second, Is.SameAs(first));
        Assert.That(second["tool-1"], Is.EqualTo("Write"));
    }

    [Test]
    public void RemoveSessionToolUses_Clears()
    {
        _manager.GetOrCreateSessionToolUses("s1");
        _manager.RemoveSessionToolUses("s1");
        // After removal, GetOrCreate should return a new empty dictionary
        var toolUses = _manager.GetOrCreateSessionToolUses("s1");
        Assert.That(toolUses, Is.Empty);
    }

    // --- Question answer sources ---

    [Test]
    public void TryGetQuestionAnswerSource_ReturnsFalseWhenNotSet()
    {
        var found = _manager.TryGetQuestionAnswerSource("s1", out _);
        Assert.That(found, Is.False);
    }

    [Test]
    public void SetAndTryGetQuestionAnswerSource_Works()
    {
        var tcs = new TaskCompletionSource<Dictionary<string, string>>();
        _manager.SetQuestionAnswerSource("s1", tcs);
        var found = _manager.TryGetQuestionAnswerSource("s1", out var result);
        Assert.Multiple(() =>
        {
            Assert.That(found, Is.True);
            Assert.That(result, Is.SameAs(tcs));
        });
    }

    [Test]
    public void TryRemoveQuestionAnswerSource_RemovesEntry()
    {
        var tcs = new TaskCompletionSource<Dictionary<string, string>>();
        _manager.SetQuestionAnswerSource("s1", tcs);
        var removed = _manager.TryRemoveQuestionAnswerSource("s1");
        Assert.Multiple(() =>
        {
            Assert.That(removed, Is.True);
            Assert.That(_manager.TryGetQuestionAnswerSource("s1", out _), Is.False);
        });
    }

    // --- Bulk cleanup ---

    [Test]
    public void CleanupSession_RemovesAllStateForSession()
    {
        _manager.GetOrCreateCts("s1");
        _manager.SetRunId("s1", "run-1");
        _manager.SetCurrentTurnId("s1", Guid.NewGuid());
        _manager.SetAgentSessionId("s1", "agent-1");
        _manager.GetOrCreateSessionToolUses("s1");
        _manager.SetQuestionAnswerSource("s1", new TaskCompletionSource<Dictionary<string, string>>());

        _manager.CleanupSession("s1");

        Assert.Multiple(() =>
        {
            Assert.That(_manager.GetCts("s1"), Is.Null);
            Assert.That(_manager.GetRunId("s1"), Is.Null);
            Assert.That(_manager.GetCurrentTurnId("s1"), Is.Null);
            Assert.That(_manager.GetAgentSessionId("s1"), Is.Null);
            Assert.That(_manager.TryGetQuestionAnswerSource("s1", out _), Is.False);
        });
    }

    [Test]
    public void CleanupSession_DoesNotAffectOtherSessions()
    {
        _manager.SetRunId("s1", "run-1");
        _manager.SetRunId("s2", "run-2");

        _manager.CleanupSession("s1");

        Assert.Multiple(() =>
        {
            Assert.That(_manager.GetRunId("s1"), Is.Null);
            Assert.That(_manager.GetRunId("s2"), Is.EqualTo("run-2"));
        });
    }

    [Test]
    public async Task CleanupAllAsync_ClearsAllState()
    {
        _manager.GetOrCreateCts("s1");
        _manager.GetOrCreateCts("s2");
        _manager.SetRunId("s1", "run-1");
        _manager.SetAgentSessionId("s2", "agent-2");

        await _manager.CleanupAllAsync();

        Assert.Multiple(() =>
        {
            Assert.That(_manager.GetCts("s1"), Is.Null);
            Assert.That(_manager.GetCts("s2"), Is.Null);
            Assert.That(_manager.GetRunId("s1"), Is.Null);
            Assert.That(_manager.GetAgentSessionId("s2"), Is.Null);
        });
    }
}
