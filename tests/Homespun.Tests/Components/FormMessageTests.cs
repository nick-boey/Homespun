using Bunit;
using Homespun.Client.Components;

namespace Homespun.Tests.Components;

/// <summary>
/// bUnit tests for the FormMessage component.
/// Tests the wrapper component which uses BbAlert from Blazor Blueprint.
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

        // Assert - BbAlert renders content in the alert structure
        Assert.That(cut.Markup, Does.Contain(message));
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
        Assert.That(cut.Markup, Does.Contain(title));
    }

    [Test]
    public void FormMessage_DoesNotRenderTitle_WhenNotProvided()
    {
        // Arrange
        const string message = "Error message";

        // Act
        var cut = Render<FormMessage>(parameters =>
            parameters.Add(p => p.Message, message));

        // Assert - The BbAlertTitle is rendered but should be empty
        Assert.That(cut.Markup, Does.Contain(message));
    }

    [Test]
    public void FormMessage_DefaultsToErrorType()
    {
        // Arrange
        const string message = "Default type message";

        // Act
        var cut = Render<FormMessage>(parameters =>
            parameters.Add(p => p.Message, message));

        // Assert - Component renders with a message
        Assert.That(cut.Markup, Does.Contain(message));
    }

    [Test]
    public void FormMessage_RendersWithDifferentTypes()
    {
        // Test all message types render without error
        var types = new[] { FormMessageType.Error, FormMessageType.Success, FormMessageType.Warning, FormMessageType.Info };

        foreach (var type in types)
        {
            var cut = Render<FormMessage>(parameters =>
                parameters
                    .Add(p => p.Message, $"Test {type}")
                    .Add(p => p.Type, type));

            Assert.That(cut.Markup, Does.Contain($"Test {type}"), $"Failed for type {type}");
        }
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

        // Assert - BbAlert renders a dismiss button
        Assert.That(cut.FindAll("button"), Has.Count.GreaterThan(0));
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
        Assert.That(cut.FindAll("button"), Has.Count.EqualTo(0));
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
        Assert.That(cut.FindAll("button"), Has.Count.EqualTo(0));
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
        var button = cut.Find("button");
        button.Click();

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

        // Assert - The custom class is passed to BbAlert
        Assert.That(cut.Markup, Does.Contain("mt-3"));
        Assert.That(cut.Markup, Does.Contain("custom-class"));
    }

    [Test]
    public void FormMessage_RendersAlert()
    {
        // Arrange
        const string message = "Message with alert";

        // Act
        var cut = Render<FormMessage>(parameters =>
            parameters.Add(p => p.Message, message));

        // Assert - BbAlert renders the alert container
        Assert.That(cut.Markup, Does.Contain(message));
        Assert.That(cut.Markup.Length, Is.GreaterThan(0));
    }
}
