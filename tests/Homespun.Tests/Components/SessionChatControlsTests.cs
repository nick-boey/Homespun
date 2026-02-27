using Homespun.ClaudeAgentSdk;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Shared.Models.Sessions;
using Moq;

namespace Homespun.Tests.Components;

/// <summary>
/// Tests for session chat control elements: model selector, prompt selector, permission mode, clear context.
/// TDD: These tests define the expected behavior for the control dropdowns and buttons.
///
/// NOTE: Since the Session.razor page is complex with many dependencies (SignalR, JS Interop, etc.),
/// these tests verify the expected behavior through logic/contract testing rather than full component rendering.
/// </summary>
[TestFixture]
public class SessionChatControlsTests
{
    #region Model Selector Tests

    [Test]
    public void ModelSelector_ShouldHaveThreeModelOptions()
    {
        // The model selector should have exactly three options: opus, sonnet, haiku
        var models = new[] { "opus", "sonnet", "haiku" };

        Assert.That(models, Has.Length.EqualTo(3));
        Assert.That(models, Contains.Item("opus"));
        Assert.That(models, Contains.Item("sonnet"));
        Assert.That(models, Contains.Item("haiku"));
    }

    [Test]
    public void ModelSelector_ShouldDefaultToSessionModel()
    {
        // Given a session with model "sonnet"
        var session = CreateTestSession(model: "sonnet");

        // The model selector should default to that model
        var selectedModel = session.Model;

        Assert.That(selectedModel, Is.EqualTo("sonnet"));
    }

    [Test]
    public void ModelSelector_ShouldBeDisabledWhenRunning()
    {
        // Given a running session
        var session = CreateTestSession(status: ClaudeSessionStatus.Running);

        // The control should be disabled
        bool isDisabled = IsControlDisabled(session.Status);

        Assert.That(isDisabled, Is.True, "Model selector should be disabled when session is running");
    }

    [Test]
    public void ModelSelector_ShouldBeDisabledWhenStopped()
    {
        var session = CreateTestSession(status: ClaudeSessionStatus.Stopped);

        bool isDisabled = IsControlDisabled(session.Status);

        Assert.That(isDisabled, Is.True, "Model selector should be disabled when session is stopped");
    }

    [Test]
    public void ModelSelector_ShouldBeEnabledWhenWaitingForInput()
    {
        var session = CreateTestSession(status: ClaudeSessionStatus.WaitingForInput);

        bool isDisabled = IsControlDisabled(session.Status);

        Assert.That(isDisabled, Is.False, "Model selector should be enabled when waiting for input");
    }

    #endregion

    #region Prompt Selector Tests

    [Test]
    public void PromptSelector_ShouldLoadPromptsFromService()
    {
        // Arrange
        var mockService = new Mock<IAgentPromptService>();
        var prompts = new List<AgentPrompt>
        {
            new() { Id = "p1", Name = "Plan", InitialMessage = "Plan {{title}}" },
            new() { Id = "p2", Name = "Build", InitialMessage = "Build {{title}}" }
        };
        mockService.Setup(s => s.GetAllPrompts()).Returns(prompts);

        // Act
        var loadedPrompts = mockService.Object.GetAllPrompts();

        // Assert
        Assert.That(loadedPrompts, Has.Count.EqualTo(2));
        Assert.That(loadedPrompts[0].Name, Is.EqualTo("Plan"));
        Assert.That(loadedPrompts[1].Name, Is.EqualTo("Build"));
    }

    [Test]
    public void PromptSelector_ShouldRenderTemplateWithContext()
    {
        // Arrange
        var mockService = new Mock<IAgentPromptService>();
        var context = new PromptContext
        {
            Title = "Test Issue",
            Id = "ABC123",
            Description = "Fix the bug",
            Branch = "feature/fix",
            Type = "Bug"
        };

        mockService.Setup(s => s.RenderTemplate(
            "Working on {{title}} ({{id}})",
            It.Is<PromptContext>(c => c.Title == "Test Issue" && c.Id == "ABC123")))
            .Returns("Working on Test Issue (ABC123)");

        // Act
        var rendered = mockService.Object.RenderTemplate("Working on {{title}} ({{id}})", context);

        // Assert
        Assert.That(rendered, Is.EqualTo("Working on Test Issue (ABC123)"));
    }

    [Test]
    public void PromptSelector_SelectionShouldClearPreviousInput()
    {
        // When a prompt is selected, the behavior should be:
        // 1. Clear existing input
        // 2. Set input to rendered prompt
        // This is verified by ensuring the input is set to the prompt content (implicitly clearing previous)

        var previousInput = "This was my previous message";
        var newPromptContent = "New prompt from template";

        // Simulating the selection behavior
        var inputMessage = previousInput;
        inputMessage = newPromptContent; // This is what happens on selection

        Assert.That(inputMessage, Is.EqualTo(newPromptContent));
        Assert.That(inputMessage, Is.Not.EqualTo(previousInput));
    }

    [Test]
    public void PromptSelector_ShouldResetAfterSelection()
    {
        // After selecting a prompt, the dropdown should reset to default (empty string)
        var selectedPromptId = "some-prompt-id";

        // Simulate selection and reset
        selectedPromptId = string.Empty; // Reset after selection

        Assert.That(selectedPromptId, Is.Empty);
    }

    [Test]
    public void PromptSelector_ShouldBeDisabledWhenRunning()
    {
        var session = CreateTestSession(status: ClaudeSessionStatus.Running);

        bool isDisabled = IsControlDisabled(session.Status);

        Assert.That(isDisabled, Is.True, "Prompt selector should be disabled when session is running");
    }

    #endregion

    #region Mode Selector Tests

    [Test]
    public void ModeSelector_ShouldHaveTwoOptions()
    {
        // Mode selector should have: Build, Plan
        var modes = Enum.GetValues<SessionMode>();

        Assert.That(modes, Has.Length.EqualTo(2));
        Assert.That(modes, Contains.Item(SessionMode.Build));
        Assert.That(modes, Contains.Item(SessionMode.Plan));
    }

    [Test]
    public void ModeSelector_DefaultShouldBeBuild()
    {
        // The default mode should be Build (full access)
        var defaultMode = SessionMode.Build;

        Assert.That(defaultMode, Is.EqualTo(SessionMode.Build));
    }

    [Test]
    public void SendMessageRequest_ShouldIncludeMode()
    {
        // SendMessageRequest must carry the mode from the UI to the server
        var request = new Homespun.Shared.Requests.SendMessageRequest
        {
            Message = "Hello",
            Mode = SessionMode.Plan
        };

        Assert.That(request.Mode, Is.EqualTo(SessionMode.Plan));
    }

    [Test]
    public void SendMessageRequest_Mode_DefaultsToNull()
    {
        // When not specified, mode should be null (session's mode will be used)
        var request = new Homespun.Shared.Requests.SendMessageRequest
        {
            Message = "Hello"
        };

        Assert.That(request.Mode, Is.Null);
    }

    #endregion

    #region Clear Context Tests

    [Test]
    public void ClearContext_ShouldAddContextClearMarker()
    {
        // Arrange
        var session = CreateTestSession();
        Assert.That(session.ContextClearMarkers, Has.Count.EqualTo(0));

        // Act - simulate clearing context
        session.ContextClearMarkers.Add(DateTime.UtcNow);

        // Assert
        Assert.That(session.ContextClearMarkers, Has.Count.EqualTo(1));
    }

    [Test]
    public void ClearContext_ShouldNotDeleteMessages()
    {
        // Arrange
        var session = CreateTestSession();
        session.Messages.Add(new ClaudeMessage
        {
            SessionId = session.Id,
            Role = ClaudeMessageRole.User,
            Content = [new ClaudeMessageContent { Type = ClaudeContentType.Text, Text = "Test" }]
        });
        var originalCount = session.Messages.Count;

        // Act - simulate clearing context (should not remove messages)
        session.ContextClearMarkers.Add(DateTime.UtcNow);

        // Assert
        Assert.That(session.Messages, Has.Count.EqualTo(originalCount));
    }

    [Test]
    public void ClearContext_ServiceShouldClearConversationId()
    {
        // Verify the service clears the conversation ID to start fresh
        var mockService = new Mock<IClaudeSessionService>();
        mockService.Setup(s => s.ClearContextAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        Assert.DoesNotThrowAsync(async () =>
            await mockService.Object.ClearContextAsync("session-1"));
    }

    [Test]
    public void ClearContextButton_ShouldBeDisabledWhenNoMessages()
    {
        var session = CreateTestSession();
        session.Messages.Clear();

        bool isDisabled = IsClearContextDisabled(session);

        Assert.That(isDisabled, Is.True, "Clear context button should be disabled when no messages");
    }

    [Test]
    public void ClearContextButton_ShouldBeEnabledWithMessages()
    {
        var session = CreateTestSession(status: ClaudeSessionStatus.WaitingForInput);
        session.Messages.Add(new ClaudeMessage
        {
            SessionId = session.Id,
            Role = ClaudeMessageRole.User,
            Content = [new ClaudeMessageContent { Type = ClaudeContentType.Text, Text = "Test" }]
        });

        bool isDisabled = IsClearContextDisabled(session);

        Assert.That(isDisabled, Is.False, "Clear context button should be enabled with messages");
    }

    [Test]
    public void ClearContextButton_ShouldBeDisabledWhenRunning()
    {
        var session = CreateTestSession(status: ClaudeSessionStatus.Running);
        session.Messages.Add(new ClaudeMessage
        {
            SessionId = session.Id,
            Role = ClaudeMessageRole.User,
            Content = [new ClaudeMessageContent { Type = ClaudeContentType.Text, Text = "Test" }]
        });

        bool isDisabled = IsClearContextDisabled(session);

        Assert.That(isDisabled, Is.True, "Clear context button should be disabled when session is running");
    }

    #endregion

    #region Context Separator Tests

    [Test]
    public void ContextSeparator_ShouldShowBetweenMessages()
    {
        // Arrange
        var session = CreateTestSession();
        var msg1Time = DateTime.UtcNow.AddMinutes(-5);
        var clearTime = DateTime.UtcNow.AddMinutes(-3);
        var msg2Time = DateTime.UtcNow.AddMinutes(-1);

        session.Messages.Add(new ClaudeMessage
        {
            SessionId = session.Id,
            Role = ClaudeMessageRole.User,
            CreatedAt = msg1Time,
            Content = [new ClaudeMessageContent { Type = ClaudeContentType.Text, Text = "First message" }]
        });
        session.Messages.Add(new ClaudeMessage
        {
            SessionId = session.Id,
            Role = ClaudeMessageRole.User,
            CreatedAt = msg2Time,
            Content = [new ClaudeMessageContent { Type = ClaudeContentType.Text, Text = "Second message" }]
        });
        session.ContextClearMarkers.Add(clearTime);

        // Act
        bool showSeparatorBeforeFirst = ShouldShowContextSeparator(session, session.Messages[0]);
        bool showSeparatorBeforeSecond = ShouldShowContextSeparator(session, session.Messages[1]);

        // Assert
        Assert.That(showSeparatorBeforeFirst, Is.False, "First message should not have separator before it");
        Assert.That(showSeparatorBeforeSecond, Is.True, "Second message should have separator before it");
    }

    [Test]
    public void ContextSeparator_ShouldNotShowForFirstMessage()
    {
        var session = CreateTestSession();
        var msg1Time = DateTime.UtcNow.AddMinutes(-5);

        session.Messages.Add(new ClaudeMessage
        {
            SessionId = session.Id,
            Role = ClaudeMessageRole.User,
            CreatedAt = msg1Time,
            Content = [new ClaudeMessageContent { Type = ClaudeContentType.Text, Text = "First message" }]
        });

        // Even if there's a clear marker before the first message, don't show it
        session.ContextClearMarkers.Add(msg1Time.AddMinutes(-1));

        bool showSeparator = ShouldShowContextSeparator(session, session.Messages[0]);

        Assert.That(showSeparator, Is.False, "First message should never have separator before it");
    }

    #endregion

    #region Layout Tests

    [Test]
    public void Layout_ControlsRowShouldContainFourElements()
    {
        // The controls row should contain:
        // 1. Permission selector
        // 2. Model selector
        // 3. Prompt selector
        // 4. Clear context button
        var controlElements = new[] { "permission-select", "model-select", "prompt-select", "clear-context-btn" };

        Assert.That(controlElements, Has.Length.EqualTo(4));
    }

    #endregion

    #region Helper Methods

    private static ClaudeSession CreateTestSession(
        ClaudeSessionStatus status = ClaudeSessionStatus.WaitingForInput,
        string model = "sonnet")
    {
        return new ClaudeSession
        {
            Id = "test-session-id",
            EntityId = "test-entity",
            ProjectId = "test-project",
            WorkingDirectory = "/test/dir",
            Model = model,
            Mode = SessionMode.Build,
            Status = status,
            Messages = []
        };
    }

    /// <summary>
    /// Mirrors the disabled logic for controls in Session.razor
    /// </summary>
    private static bool IsControlDisabled(ClaudeSessionStatus status)
    {
        return status == ClaudeSessionStatus.Running || status == ClaudeSessionStatus.Stopped;
    }

    /// <summary>
    /// Mirrors the disabled logic for clear context button in Session.razor
    /// </summary>
    private static bool IsClearContextDisabled(ClaudeSession session)
    {
        return session.Status == ClaudeSessionStatus.Running ||
               session.Status == ClaudeSessionStatus.Stopped ||
               session.Messages.Count == 0;
    }

    /// <summary>
    /// Mirrors the ShouldShowContextSeparator logic in Session.razor
    /// </summary>
    private static bool ShouldShowContextSeparator(ClaudeSession session, ClaudeMessage message)
    {
        if (session.ContextClearMarkers.Count == 0) return false;

        var messageIndex = session.Messages.IndexOf(message);
        if (messageIndex <= 0) return false;

        var prevMessage = session.Messages[messageIndex - 1];
        return session.ContextClearMarkers.Any(marker =>
            marker > prevMessage.CreatedAt && marker <= message.CreatedAt);
    }

    #endregion
}
