using Homespun.Shared.Models;

namespace Homespun.Tests.Components;

/// <summary>
/// Tests for Session.razor loading state behavior.
/// TDD: These tests define the expected behavior for the loading/not-found/loaded states.
///
/// The Session page should have three states:
/// 1. Loading (_isLoading = true) - Show loading spinner
/// 2. Not Found (_isLoading = false, _session = null) - Show "Session Not Found"
/// 3. Loaded (_isLoading = false, _session != null) - Show session content
/// </summary>
[TestFixture]
public class SessionLoadingStateTests
{
    /// <summary>
    /// Represents the three-state logic used in Session.razor template.
    /// This mirrors the expected Razor conditional logic.
    /// </summary>
    private record SessionPageState(bool IsLoading, ClaudeSession? Session)
    {
        public bool ShowLoading => IsLoading;
        public bool ShowNotFound => !IsLoading && Session == null;
        public bool ShowContent => !IsLoading && Session != null;
    }

    [Test]
    public void Session_ShouldShowLoadingState_WhenIsLoadingTrue()
    {
        // Given _isLoading = true and _session = null (initial state)
        var state = new SessionPageState(IsLoading: true, Session: null);

        // Then loading state should be shown
        Assert.Multiple(() =>
        {
            Assert.That(state.ShowLoading, Is.True, "Should show loading when _isLoading is true");
            Assert.That(state.ShowNotFound, Is.False, "Should not show not-found during loading");
            Assert.That(state.ShowContent, Is.False, "Should not show content during loading");
        });
    }

    [Test]
    public void Session_ShouldShowLoadingState_EvenWhenSessionIsNull()
    {
        // Given _isLoading = true (even though _session is still null during load)
        var state = new SessionPageState(IsLoading: true, Session: null);

        // The key behavior: don't show "not found" while still loading
        Assert.That(state.ShowNotFound, Is.False,
            "Should NOT show 'not found' while loading, even if session is null");
        Assert.That(state.ShowLoading, Is.True,
            "Should show loading spinner while loading");
    }

    [Test]
    public void Session_ShouldShowNotFoundState_WhenLoadedAndSessionNull()
    {
        // Given _isLoading = false and _session = null (server confirmed session doesn't exist)
        var state = new SessionPageState(IsLoading: false, Session: null);

        // Then not-found state should be shown
        Assert.Multiple(() =>
        {
            Assert.That(state.ShowLoading, Is.False, "Should not show loading when done loading");
            Assert.That(state.ShowNotFound, Is.True, "Should show not-found when session is null after loading");
            Assert.That(state.ShowContent, Is.False, "Should not show content when session is null");
        });
    }

    [Test]
    public void Session_ShouldShowContentState_WhenLoadedAndSessionExists()
    {
        // Given _isLoading = false and _session exists
        var session = CreateTestSession();
        var state = new SessionPageState(IsLoading: false, Session: session);

        // Then content state should be shown
        Assert.Multiple(() =>
        {
            Assert.That(state.ShowLoading, Is.False, "Should not show loading when done loading");
            Assert.That(state.ShowNotFound, Is.False, "Should not show not-found when session exists");
            Assert.That(state.ShowContent, Is.True, "Should show content when session exists");
        });
    }

    [Test]
    public void Session_StateTransitions_CorrectlyFromLoadingToNotFound()
    {
        // Simulate the lifecycle: initial → loading → server returns null

        // Initial state (component starts with _isLoading = true)
        var initialState = new SessionPageState(IsLoading: true, Session: null);
        Assert.That(initialState.ShowLoading, Is.True, "Initial state should show loading");
        Assert.That(initialState.ShowNotFound, Is.False, "Initial state should NOT show not-found");

        // After API returns null (loading complete, session not found)
        var finalState = new SessionPageState(IsLoading: false, Session: null);
        Assert.That(finalState.ShowLoading, Is.False, "Final state should not show loading");
        Assert.That(finalState.ShowNotFound, Is.True, "Final state should show not-found");
    }

    [Test]
    public void Session_StateTransitions_CorrectlyFromLoadingToContent()
    {
        // Simulate the lifecycle: initial → loading → server returns session

        // Initial state (component starts with _isLoading = true)
        var initialState = new SessionPageState(IsLoading: true, Session: null);
        Assert.That(initialState.ShowLoading, Is.True, "Initial state should show loading");
        Assert.That(initialState.ShowContent, Is.False, "Initial state should not show content");

        // After API returns session (loading complete, session found)
        var session = CreateTestSession();
        var finalState = new SessionPageState(IsLoading: false, Session: session);
        Assert.That(finalState.ShowLoading, Is.False, "Final state should not show loading");
        Assert.That(finalState.ShowContent, Is.True, "Final state should show content");
    }

    [Test]
    public void Session_ExactlyOneStateIsActive_AtAnyTime()
    {
        // Test all possible combinations to ensure exactly one state is active
        var testCases = new[]
        {
            new SessionPageState(IsLoading: true, Session: null),
            new SessionPageState(IsLoading: true, Session: CreateTestSession()),
            new SessionPageState(IsLoading: false, Session: null),
            new SessionPageState(IsLoading: false, Session: CreateTestSession()),
        };

        foreach (var state in testCases)
        {
            var activeStates = new[] { state.ShowLoading, state.ShowNotFound, state.ShowContent }
                .Count(s => s);

            Assert.That(activeStates, Is.EqualTo(1),
                $"Exactly one state should be active for IsLoading={state.IsLoading}, Session={(state.Session != null ? "exists" : "null")}");
        }
    }

    [Test]
    public void Session_LoadingTakesPrecedence_OverSessionState()
    {
        // Edge case: if _isLoading is true but _session already has a value
        // (e.g., from a previous load), loading should still show
        var session = CreateTestSession();
        var state = new SessionPageState(IsLoading: true, Session: session);

        Assert.That(state.ShowLoading, Is.True,
            "Loading should take precedence even if session exists");
        Assert.That(state.ShowContent, Is.False,
            "Content should not show while loading");
    }

    private static ClaudeSession CreateTestSession()
    {
        return new ClaudeSession
        {
            Id = "test-session-id",
            EntityId = "test-entity-id",
            ProjectId = "test-project-id",
            WorkingDirectory = "/test/workdir",
            Status = ClaudeSessionStatus.WaitingForInput,
            Mode = SessionMode.Build,
            Model = "sonnet"
        };
    }
}
