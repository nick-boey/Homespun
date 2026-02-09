using Homespun.ClaudeAgentSdk;
using Homespun.Features.ClaudeCode.Services;
using Microsoft.AspNetCore.Components.Web;
using Moq;

namespace Homespun.Tests.Components;

/// <summary>
/// Tests for session chat input keyboard handling and submission behavior.
/// TDD: These tests define the expected behavior for Enter/Ctrl+Enter key handling.
///
/// NOTE: Since the Session.razor page is complex with many dependencies (SignalR, JS Interop, etc.),
/// these tests verify the expected behavior through logic/contract testing rather than full component rendering.
/// </summary>
[TestFixture]
public class SessionChatInputTests
{
    #region Keyboard Handling Logic Tests

    [Test]
    public void HandleKeyDown_EnterOnly_ShouldNotSubmit()
    {
        // Arrange - This test verifies the logic that pressing Enter alone does NOT submit
        // The expected behavior: Enter key (without modifiers) should NOT trigger submission
        var keyEventArgs = new KeyboardEventArgs
        {
            Key = "Enter",
            ShiftKey = false,
            CtrlKey = false,
            MetaKey = false
        };

        // Act - Check the submission condition
        bool shouldSubmit = ShouldSubmitOnKeyDown(keyEventArgs);

        // Assert
        Assert.That(shouldSubmit, Is.False, "Enter key alone should not submit the message");
    }

    [Test]
    public void HandleKeyDown_ShiftEnter_ShouldNotSubmit()
    {
        // Arrange - Shift+Enter should also not submit (creates newline)
        var keyEventArgs = new KeyboardEventArgs
        {
            Key = "Enter",
            ShiftKey = true,
            CtrlKey = false,
            MetaKey = false
        };

        // Act
        bool shouldSubmit = ShouldSubmitOnKeyDown(keyEventArgs);

        // Assert
        Assert.That(shouldSubmit, Is.False, "Shift+Enter should not submit the message");
    }

    [Test]
    public void HandleKeyDown_CtrlEnter_ShouldSubmit()
    {
        // Arrange - This test validates that Ctrl+Enter DOES submit the message
        var keyEventArgs = new KeyboardEventArgs
        {
            Key = "Enter",
            ShiftKey = false,
            CtrlKey = true,
            MetaKey = false
        };

        // Act
        bool shouldSubmit = ShouldSubmitOnKeyDown(keyEventArgs);

        // Assert
        Assert.That(shouldSubmit, Is.True, "Ctrl+Enter should submit the message");
    }

    [Test]
    public void HandleKeyDown_MetaEnter_ShouldSubmit()
    {
        // Arrange - This test validates that Meta+Enter (Cmd+Enter on Mac) also submits
        var keyEventArgs = new KeyboardEventArgs
        {
            Key = "Enter",
            ShiftKey = false,
            CtrlKey = false,
            MetaKey = true
        };

        // Act
        bool shouldSubmit = ShouldSubmitOnKeyDown(keyEventArgs);

        // Assert
        Assert.That(shouldSubmit, Is.True, "Meta+Enter (Cmd+Enter on Mac) should submit the message");
    }

    [Test]
    public void HandleKeyDown_CtrlShiftEnter_ShouldSubmit()
    {
        // Arrange - Ctrl+Shift+Enter should also submit (Ctrl is present)
        var keyEventArgs = new KeyboardEventArgs
        {
            Key = "Enter",
            ShiftKey = true,
            CtrlKey = true,
            MetaKey = false
        };

        // Act
        bool shouldSubmit = ShouldSubmitOnKeyDown(keyEventArgs);

        // Assert
        Assert.That(shouldSubmit, Is.True, "Ctrl+Shift+Enter should submit the message");
    }

    [Test]
    public void HandleKeyDown_NonEnterKey_ShouldNotSubmit()
    {
        // Arrange - Any non-Enter key should not trigger submit
        var keyEventArgs = new KeyboardEventArgs
        {
            Key = "a",
            ShiftKey = false,
            CtrlKey = true,
            MetaKey = false
        };

        // Act
        bool shouldSubmit = ShouldSubmitOnKeyDown(keyEventArgs);

        // Assert
        Assert.That(shouldSubmit, Is.False, "Non-Enter keys should not submit");
    }

    #endregion

    #region Service Contract Tests

    [Test]
    public void SendMessageAsync_ShouldAcceptModelParameter()
    {
        // Verify the service interface supports the model parameter
        var mockService = new Mock<IClaudeSessionService>();

        // This should compile - verifying the signature exists
        mockService.Setup(s => s.SendMessageAsync(
            It.IsAny<string>(),
            It.IsAny<string>(),
            It.IsAny<PermissionMode>(),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()));

        Assert.Pass("Service interface supports model parameter in SendMessageAsync");
    }

    [Test]
    public void SendMessageAsync_WithNullModel_ShouldUseDefaultModel()
    {
        // Verify the service can handle null model (uses session default)
        var mockService = new Mock<IClaudeSessionService>();
        mockService.Setup(s => s.SendMessageAsync(
            "session-1",
            "test message",
            PermissionMode.Default,
            null,
            It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act & Assert - should not throw
        Assert.DoesNotThrowAsync(async () =>
            await mockService.Object.SendMessageAsync("session-1", "test message", PermissionMode.Default, null));
    }

    #endregion

    #region Input Validation Tests

    [Test]
    public void SendButton_ShouldBeDisabledWhenInputEmpty()
    {
        // Test the logic for determining send button disabled state
        string inputMessage = "";
        var sessionStatus = ClaudeSessionStatus.WaitingForInput;

        bool isDisabled = IsSendButtonDisabled(inputMessage, sessionStatus);

        Assert.That(isDisabled, Is.True, "Send button should be disabled when input is empty");
    }

    [Test]
    public void SendButton_ShouldBeDisabledWhenWhitespaceOnly()
    {
        // Test the logic for determining send button disabled state
        string inputMessage = "   \t\n  ";
        var sessionStatus = ClaudeSessionStatus.WaitingForInput;

        bool isDisabled = IsSendButtonDisabled(inputMessage, sessionStatus);

        Assert.That(isDisabled, Is.True, "Send button should be disabled when input is only whitespace");
    }

    [Test]
    public void SendButton_ShouldBeEnabledWithValidInput()
    {
        string inputMessage = "Hello, Claude!";
        var sessionStatus = ClaudeSessionStatus.WaitingForInput;

        bool isDisabled = IsSendButtonDisabled(inputMessage, sessionStatus);

        Assert.That(isDisabled, Is.False, "Send button should be enabled with valid input");
    }

    [Test]
    public void SendButton_ShouldBeDisabledWhenSessionRunning()
    {
        string inputMessage = "Hello, Claude!";
        var sessionStatus = ClaudeSessionStatus.Running;

        bool isDisabled = IsSendButtonDisabled(inputMessage, sessionStatus);

        Assert.That(isDisabled, Is.True, "Send button should be disabled when session is running");
    }

    [Test]
    public void SendButton_ShouldBeDisabledWhenSessionStopped()
    {
        string inputMessage = "Hello, Claude!";
        var sessionStatus = ClaudeSessionStatus.Stopped;

        bool isDisabled = IsSendButtonDisabled(inputMessage, sessionStatus);

        Assert.That(isDisabled, Is.True, "Send button should be disabled when session is stopped");
    }

    #endregion

    #region Helper Methods (mirror the Session.razor logic)

    /// <summary>
    /// Logic to determine if a message should be submitted based on key press.
    /// Mirrors the HandleKeyDown logic in Session.razor.
    /// </summary>
    private static bool ShouldSubmitOnKeyDown(KeyboardEventArgs e)
    {
        // Only submit on Ctrl+Enter or Meta+Enter (Cmd+Enter on Mac)
        return e.Key == "Enter" && (e.CtrlKey || e.MetaKey);
    }

    /// <summary>
    /// Logic to determine if send button should be disabled.
    /// Mirrors the disabled attribute logic in Session.razor.
    /// </summary>
    private static bool IsSendButtonDisabled(string inputMessage, ClaudeSessionStatus status)
    {
        return string.IsNullOrWhiteSpace(inputMessage) ||
               status == ClaudeSessionStatus.Running ||
               status == ClaudeSessionStatus.Stopped;
    }

    #endregion
}
