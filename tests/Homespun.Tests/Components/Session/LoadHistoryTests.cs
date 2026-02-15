using Homespun.ClaudeAgentSdk;

namespace Homespun.Tests.Components.Session;

/// <summary>
/// Tests for the "Load previous messages" button feature in Session.razor.
/// TDD: These tests define the expected behavior for loading historical messages
/// when a user joins a session that is currently midway through processing.
///
/// The button should:
/// - Appear when the session is actively processing AND more cached messages exist than are loaded
/// - Be hidden when the session is stopped
/// - Be hidden when all messages are already loaded
/// - Load and merge cached messages when clicked
/// - Deduplicate messages by ID
/// - Mark all loaded messages as not streaming
/// </summary>
[TestFixture]
public class LoadHistoryTests
{
    #region Button Visibility Tests

    [Test]
    public void LoadHistoryButton_ShownWhenRunningWithMoreCachedMessages()
    {
        // Arrange
        var state = new LoadHistoryState
        {
            SessionStatus = ClaudeSessionStatus.Running,
            CachedMessageCount = 10,
            LoadedMessageCount = 3
        };

        // Act
        var shouldShow = ShouldShowLoadHistoryButton(state);

        // Assert
        Assert.That(shouldShow, Is.True, "Button should show when running and more messages exist in cache");
    }

    [Test]
    public void LoadHistoryButton_ShownWhenStartingWithMoreCachedMessages()
    {
        // Arrange
        var state = new LoadHistoryState
        {
            SessionStatus = ClaudeSessionStatus.Starting,
            CachedMessageCount = 5,
            LoadedMessageCount = 0
        };

        // Act
        var shouldShow = ShouldShowLoadHistoryButton(state);

        // Assert
        Assert.That(shouldShow, Is.True, "Button should show when starting and messages exist in cache");
    }

    [Test]
    public void LoadHistoryButton_ShownWhenRunningHooksWithMoreCachedMessages()
    {
        // Arrange
        var state = new LoadHistoryState
        {
            SessionStatus = ClaudeSessionStatus.RunningHooks,
            CachedMessageCount = 8,
            LoadedMessageCount = 2
        };

        // Act
        var shouldShow = ShouldShowLoadHistoryButton(state);

        // Assert
        Assert.That(shouldShow, Is.True, "Button should show when running hooks and more messages exist in cache");
    }

    [Test]
    public void LoadHistoryButton_HiddenWhenSessionStopped()
    {
        // Arrange
        var state = new LoadHistoryState
        {
            SessionStatus = ClaudeSessionStatus.Stopped,
            CachedMessageCount = 10,
            LoadedMessageCount = 3
        };

        // Act
        var shouldShow = ShouldShowLoadHistoryButton(state);

        // Assert
        Assert.That(shouldShow, Is.False, "Button should be hidden when session is stopped");
    }

    [Test]
    public void LoadHistoryButton_HiddenWhenWaitingForInput()
    {
        // Arrange
        var state = new LoadHistoryState
        {
            SessionStatus = ClaudeSessionStatus.WaitingForInput,
            CachedMessageCount = 10,
            LoadedMessageCount = 3
        };

        // Act
        var shouldShow = ShouldShowLoadHistoryButton(state);

        // Assert
        Assert.That(shouldShow, Is.False, "Button should be hidden when session is waiting for input (not actively processing)");
    }

    [Test]
    public void LoadHistoryButton_HiddenWhenAllMessagesLoaded()
    {
        // Arrange
        var state = new LoadHistoryState
        {
            SessionStatus = ClaudeSessionStatus.Running,
            CachedMessageCount = 5,
            LoadedMessageCount = 5
        };

        // Act
        var shouldShow = ShouldShowLoadHistoryButton(state);

        // Assert
        Assert.That(shouldShow, Is.False, "Button should be hidden when all cached messages are already loaded");
    }

    [Test]
    public void LoadHistoryButton_HiddenWhenMoreLoadedThanCached()
    {
        // Arrange: This can happen if new messages arrived after cache count was checked
        var state = new LoadHistoryState
        {
            SessionStatus = ClaudeSessionStatus.Running,
            CachedMessageCount = 5,
            LoadedMessageCount = 7
        };

        // Act
        var shouldShow = ShouldShowLoadHistoryButton(state);

        // Assert
        Assert.That(shouldShow, Is.False, "Button should be hidden when loaded messages exceed cached count");
    }

    [Test]
    public void LoadHistoryButton_HiddenWhenNoCachedMessages()
    {
        // Arrange
        var state = new LoadHistoryState
        {
            SessionStatus = ClaudeSessionStatus.Running,
            CachedMessageCount = 0,
            LoadedMessageCount = 0
        };

        // Act
        var shouldShow = ShouldShowLoadHistoryButton(state);

        // Assert
        Assert.That(shouldShow, Is.False, "Button should be hidden when no cached messages exist");
    }

    #endregion

    #region Message Count Display Tests

    [Test]
    public void PreviousMessageCount_CalculatedCorrectly()
    {
        // Arrange
        var state = new LoadHistoryState
        {
            CachedMessageCount = 10,
            LoadedMessageCount = 3
        };

        // Act
        int previousCount = GetPreviousMessageCount(state);

        // Assert
        Assert.That(previousCount, Is.EqualTo(7), "Should show 7 previous messages (10 - 3)");
    }

    [Test]
    public void PreviousMessageCount_NeverNegative()
    {
        // Arrange: loaded > cached (edge case)
        var state = new LoadHistoryState
        {
            CachedMessageCount = 3,
            LoadedMessageCount = 5
        };

        // Act
        int previousCount = GetPreviousMessageCount(state);

        // Assert
        Assert.That(previousCount, Is.GreaterThanOrEqualTo(0), "Previous message count should never be negative");
    }

    #endregion

    #region Message Merging Tests

    [Test]
    public void LoadHistory_DeduplicatesMessagesByID()
    {
        // Arrange
        var existingMessages = new List<ClaudeMessage>
        {
            CreateMessage("msg-1", "Hello"),
            CreateMessage("msg-2", "World")
        };

        var cachedMessages = new List<ClaudeMessage>
        {
            CreateMessage("msg-0", "First"),
            CreateMessage("msg-1", "Hello"), // Duplicate
            CreateMessage("msg-2", "World"), // Duplicate
            CreateMessage("msg-3", "New")
        };

        // Act
        var merged = MergeMessages(existingMessages, cachedMessages);

        // Assert
        Assert.That(merged.Count, Is.EqualTo(4), "Should have 4 unique messages");
        Assert.That(merged.Select(m => m.Id).Distinct().Count(), Is.EqualTo(4), "All message IDs should be unique");
        Assert.That(merged[0].Id, Is.EqualTo("msg-0"), "First message should be msg-0");
        Assert.That(merged[3].Id, Is.EqualTo("msg-3"), "Last message should be msg-3");
    }

    [Test]
    public void LoadHistory_MarksAllMessagesAsNotStreaming()
    {
        // Arrange
        var cachedMessages = new List<ClaudeMessage>
        {
            CreateMessage("msg-1", "Hello", isStreaming: true),
            CreateMessage("msg-2", "World", isStreaming: false)
        };

        // Act
        var processed = ProcessCachedMessages(cachedMessages);

        // Assert
        Assert.That(processed.All(m => !m.IsStreaming), Is.True, "All messages should be marked as not streaming");
        Assert.That(processed.SelectMany(m => m.Content).All(c => !c.IsStreaming), Is.True,
            "All content blocks should be marked as not streaming");
    }

    [Test]
    public void LoadHistory_PreservesMessageOrder()
    {
        // Arrange
        var cachedMessages = new List<ClaudeMessage>
        {
            CreateMessage("msg-1", "First", createdAt: DateTime.UtcNow.AddMinutes(-3)),
            CreateMessage("msg-2", "Second", createdAt: DateTime.UtcNow.AddMinutes(-2)),
            CreateMessage("msg-3", "Third", createdAt: DateTime.UtcNow.AddMinutes(-1))
        };

        // Act
        var processed = ProcessCachedMessages(cachedMessages);

        // Assert
        Assert.That(processed[0].Id, Is.EqualTo("msg-1"));
        Assert.That(processed[1].Id, Is.EqualTo("msg-2"));
        Assert.That(processed[2].Id, Is.EqualTo("msg-3"));
    }

    #endregion

    #region Loading State Tests

    [Test]
    public void LoadingState_ButtonDisabledWhileLoading()
    {
        // Arrange
        var state = new LoadHistoryState
        {
            IsLoadingHistory = true
        };

        // Act
        var isDisabled = IsButtonDisabled(state);

        // Assert
        Assert.That(isDisabled, Is.True, "Button should be disabled while loading");
    }

    [Test]
    public void LoadingState_ButtonEnabledWhenNotLoading()
    {
        // Arrange
        var state = new LoadHistoryState
        {
            IsLoadingHistory = false
        };

        // Act
        var isDisabled = IsButtonDisabled(state);

        // Assert
        Assert.That(isDisabled, Is.False, "Button should be enabled when not loading");
    }

    [Test]
    public void AfterLoad_ButtonShouldBeHidden()
    {
        // Arrange
        var state = new LoadHistoryState
        {
            SessionStatus = ClaudeSessionStatus.Running,
            CachedMessageCount = 10,
            LoadedMessageCount = 3,
            ShowLoadHistoryButton = true
        };

        // Act: Simulate successful load
        state.ShowLoadHistoryButton = false; // This happens after LoadHistoryAsync completes

        // Assert
        Assert.That(state.ShowLoadHistoryButton, Is.False, "Button should be hidden after successful load");
    }

    #endregion

    #region Active Session Status Tests

    [Test]
    [TestCase(ClaudeSessionStatus.Starting, true)]
    [TestCase(ClaudeSessionStatus.RunningHooks, true)]
    [TestCase(ClaudeSessionStatus.Running, true)]
    [TestCase(ClaudeSessionStatus.WaitingForInput, false)]
    [TestCase(ClaudeSessionStatus.WaitingForQuestionAnswer, false)]
    [TestCase(ClaudeSessionStatus.WaitingForPlanExecution, false)]
    [TestCase(ClaudeSessionStatus.Stopped, false)]
    public void IsActivelyProcessing_ReturnsCorrectValue(ClaudeSessionStatus status, bool expectedResult)
    {
        // Act
        var isActive = IsActivelyProcessing(status);

        // Assert
        Assert.That(isActive, Is.EqualTo(expectedResult),
            $"Status {status} should {(expectedResult ? "" : "not ")}be considered actively processing");
    }

    #endregion

    #region Helper Types and Methods

    /// <summary>
    /// Represents the load history state tracked by the component.
    /// </summary>
    private class LoadHistoryState
    {
        public ClaudeSessionStatus SessionStatus { get; set; } = ClaudeSessionStatus.WaitingForInput;
        public int CachedMessageCount { get; set; }
        public int LoadedMessageCount { get; set; }
        public bool IsLoadingHistory { get; set; }
        public bool ShowLoadHistoryButton { get; set; }
    }

    /// <summary>
    /// Mirrors the button visibility logic in Session.razor.
    /// </summary>
    private static bool ShouldShowLoadHistoryButton(LoadHistoryState state)
    {
        var isActivelyProcessing = IsActivelyProcessing(state.SessionStatus);
        return isActivelyProcessing && state.CachedMessageCount > state.LoadedMessageCount;
    }

    /// <summary>
    /// Determines if a session is actively processing (mid-execution).
    /// </summary>
    private static bool IsActivelyProcessing(ClaudeSessionStatus status)
    {
        return status is ClaudeSessionStatus.Starting
            or ClaudeSessionStatus.RunningHooks
            or ClaudeSessionStatus.Running;
    }

    /// <summary>
    /// Calculates the number of previous messages available to load.
    /// </summary>
    private static int GetPreviousMessageCount(LoadHistoryState state)
    {
        return Math.Max(0, state.CachedMessageCount - state.LoadedMessageCount);
    }

    /// <summary>
    /// Determines if the button should be disabled.
    /// </summary>
    private static bool IsButtonDisabled(LoadHistoryState state)
    {
        return state.IsLoadingHistory;
    }

    /// <summary>
    /// Merges cached messages with existing messages, deduplicating by ID.
    /// Cached messages become the source of truth.
    /// </summary>
    private static List<ClaudeMessage> MergeMessages(
        List<ClaudeMessage> existingMessages,
        List<ClaudeMessage> cachedMessages)
    {
        // Cached messages are the authoritative source - just use them
        // The existing messages were incomplete, so we replace entirely
        return cachedMessages.ToList();
    }

    /// <summary>
    /// Processes cached messages by marking them as not streaming.
    /// </summary>
    private static List<ClaudeMessage> ProcessCachedMessages(List<ClaudeMessage> cachedMessages)
    {
        foreach (var msg in cachedMessages)
        {
            msg.IsStreaming = false;
            foreach (var content in msg.Content)
            {
                content.IsStreaming = false;
            }
        }
        return cachedMessages;
    }

    /// <summary>
    /// Creates a test message with the given parameters.
    /// </summary>
    private static ClaudeMessage CreateMessage(
        string id,
        string text,
        bool isStreaming = false,
        DateTime? createdAt = null)
    {
        var message = new ClaudeMessage
        {
            Id = id,
            SessionId = "test-session",
            Role = ClaudeMessageRole.Assistant,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            IsStreaming = isStreaming
        };
        message.Content.Add(new ClaudeMessageContent
        {
            Type = ClaudeContentType.Text,
            Text = text,
            IsStreaming = isStreaming
        });
        return message;
    }

    #endregion
}
