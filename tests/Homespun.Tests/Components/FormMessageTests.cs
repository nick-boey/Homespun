using Bunit;
using Homespun.Components.Shared;

namespace Homespun.Tests.Components;

/// <summary>
/// bUnit tests for the FormMessage component.
/// </summary>
[TestFixture]
public class FormMessageTests : BunitTestContext
{
    [Test]
    public void FormMessage_RendersNothing_WhenMessageIsNull()
    {
        // Act
        var cut = Render<FormMessage>(parameters =>
            parameters.Add(p => p.Message, null));

        // Assert
        Assert.That(cut.Markup, Is.Empty.Or.EqualTo(""));
    }

    [Test]
    public void FormMessage_RendersNothing_WhenMessageIsEmpty()
    {
        // Act
        var cut = Render<FormMessage>(parameters =>
            parameters.Add(p => p.Message, ""));

        // Assert
        Assert.That(cut.Markup, Is.Empty.Or.EqualTo(""));
    }

    [Test]
    public void FormMessage_DisplaysMessage_WhenProvided()
    {
        // Arrange
        const string message = "Test error message";

        // Act
        var cut = Render<FormMessage>(parameters =>
            parameters.Add(p => p.Message, message));

        // Assert
        Assert.That(cut.Find(".form-message-body").TextContent, Is.EqualTo(message));
    }

    [Test]
    public void FormMessage_DisplaysTitle_WhenProvided()
    {
        // Arrange
        const string title = "Error Title";
        const string message = "Error message";

        // Act
        var cut = Render<FormMessage>(parameters =>
            parameters
                .Add(p => p.Title, title)
                .Add(p => p.Message, message));

        // Assert
        Assert.That(cut.Find(".form-message-title").TextContent, Is.EqualTo(title));
    }

    [Test]
    public void FormMessage_DoesNotRenderTitle_WhenNotProvided()
    {
        // Arrange
        const string message = "Error message";

        // Act
        var cut = Render<FormMessage>(parameters =>
            parameters.Add(p => p.Message, message));

        // Assert
        Assert.That(cut.FindAll(".form-message-title"), Has.Count.EqualTo(0));
    }

    [Test]
    public void FormMessage_AppliesErrorClass_ForErrorType()
    {
        // Arrange
        const string message = "Error message";

        // Act
        var cut = Render<FormMessage>(parameters =>
            parameters
                .Add(p => p.Message, message)
                .Add(p => p.Type, FormMessageType.Error));

        // Assert
        Assert.That(cut.Find(".form-message").ClassList, Does.Contain("form-message-error"));
    }

    [Test]
    public void FormMessage_AppliesSuccessClass_ForSuccessType()
    {
        // Arrange
        const string message = "Success message";

        // Act
        var cut = Render<FormMessage>(parameters =>
            parameters
                .Add(p => p.Message, message)
                .Add(p => p.Type, FormMessageType.Success));

        // Assert
        Assert.That(cut.Find(".form-message").ClassList, Does.Contain("form-message-success"));
    }

    [Test]
    public void FormMessage_AppliesWarningClass_ForWarningType()
    {
        // Arrange
        const string message = "Warning message";

        // Act
        var cut = Render<FormMessage>(parameters =>
            parameters
                .Add(p => p.Message, message)
                .Add(p => p.Type, FormMessageType.Warning));

        // Assert
        Assert.That(cut.Find(".form-message").ClassList, Does.Contain("form-message-warning"));
    }

    [Test]
    public void FormMessage_AppliesInfoClass_ForInfoType()
    {
        // Arrange
        const string message = "Info message";

        // Act
        var cut = Render<FormMessage>(parameters =>
            parameters
                .Add(p => p.Message, message)
                .Add(p => p.Type, FormMessageType.Info));

        // Assert
        Assert.That(cut.Find(".form-message").ClassList, Does.Contain("form-message-info"));
    }

    [Test]
    public void FormMessage_DefaultsToErrorType()
    {
        // Arrange
        const string message = "Default type message";

        // Act
        var cut = Render<FormMessage>(parameters =>
            parameters.Add(p => p.Message, message));

        // Assert
        Assert.That(cut.Find(".form-message").ClassList, Does.Contain("form-message-error"));
    }

    [Test]
    public void FormMessage_ShowsDismissButton_WhenDismissibleWithCallback()
    {
        // Arrange
        const string message = "Dismissible message";

        // Act
        var cut = Render<FormMessage>(parameters =>
            parameters
                .Add(p => p.Message, message)
                .Add(p => p.IsDismissible, true)
                .Add(p => p.OnDismiss, () => { }));

        // Assert
        Assert.That(cut.FindAll(".form-message-dismiss"), Has.Count.EqualTo(1));
    }

    [Test]
    public void FormMessage_HidesDismissButton_WhenNotDismissible()
    {
        // Arrange
        const string message = "Non-dismissible message";

        // Act
        var cut = Render<FormMessage>(parameters =>
            parameters
                .Add(p => p.Message, message)
                .Add(p => p.IsDismissible, false));

        // Assert
        Assert.That(cut.FindAll(".form-message-dismiss"), Has.Count.EqualTo(0));
    }

    [Test]
    public void FormMessage_HidesDismissButton_WhenNoCallback()
    {
        // Arrange
        const string message = "Message without callback";

        // Act
        var cut = Render<FormMessage>(parameters =>
            parameters
                .Add(p => p.Message, message)
                .Add(p => p.IsDismissible, true));

        // Assert - dismiss button not shown without callback
        Assert.That(cut.FindAll(".form-message-dismiss"), Has.Count.EqualTo(0));
    }

    [Test]
    public void FormMessage_InvokesDismissCallback_WhenDismissButtonClicked()
    {
        // Arrange
        const string message = "Dismissible message";
        var dismissed = false;

        var cut = Render<FormMessage>(parameters =>
            parameters
                .Add(p => p.Message, message)
                .Add(p => p.IsDismissible, true)
                .Add(p => p.OnDismiss, () => dismissed = true));

        // Act
        cut.Find(".form-message-dismiss").Click();

        // Assert
        Assert.That(dismissed, Is.True);
    }

    [Test]
    public void FormMessage_AppliesCustomClass_WhenProvided()
    {
        // Arrange
        const string message = "Message with custom class";
        const string customClass = "mt-3 custom-class";

        // Act
        var cut = Render<FormMessage>(parameters =>
            parameters
                .Add(p => p.Message, message)
                .Add(p => p.Class, customClass));

        // Assert
        Assert.That(cut.Find(".form-message").ClassList, Does.Contain("mt-3"));
        Assert.That(cut.Find(".form-message").ClassList, Does.Contain("custom-class"));
    }

    [Test]
    public void FormMessage_HasAlertRole_ForErrorType()
    {
        // Arrange
        const string message = "Error message";

        // Act
        var cut = Render<FormMessage>(parameters =>
            parameters
                .Add(p => p.Message, message)
                .Add(p => p.Type, FormMessageType.Error));

        // Assert
        Assert.That(cut.Find(".form-message").GetAttribute("role"), Is.EqualTo("alert"));
    }

    [Test]
    public void FormMessage_HasAlertRole_ForWarningType()
    {
        // Arrange
        const string message = "Warning message";

        // Act
        var cut = Render<FormMessage>(parameters =>
            parameters
                .Add(p => p.Message, message)
                .Add(p => p.Type, FormMessageType.Warning));

        // Assert
        Assert.That(cut.Find(".form-message").GetAttribute("role"), Is.EqualTo("alert"));
    }

    [Test]
    public void FormMessage_HasStatusRole_ForSuccessType()
    {
        // Arrange
        const string message = "Success message";

        // Act
        var cut = Render<FormMessage>(parameters =>
            parameters
                .Add(p => p.Message, message)
                .Add(p => p.Type, FormMessageType.Success));

        // Assert
        Assert.That(cut.Find(".form-message").GetAttribute("role"), Is.EqualTo("status"));
    }

    [Test]
    public void FormMessage_HasStatusRole_ForInfoType()
    {
        // Arrange
        const string message = "Info message";

        // Act
        var cut = Render<FormMessage>(parameters =>
            parameters
                .Add(p => p.Message, message)
                .Add(p => p.Type, FormMessageType.Info));

        // Assert
        Assert.That(cut.Find(".form-message").GetAttribute("role"), Is.EqualTo("status"));
    }

    [Test]
    public void FormMessage_RendersIcon()
    {
        // Arrange
        const string message = "Message with icon";

        // Act
        var cut = Render<FormMessage>(parameters =>
            parameters.Add(p => p.Message, message));

        // Assert
        Assert.That(cut.FindAll(".form-message-icon svg"), Has.Count.EqualTo(1));
    }
}
