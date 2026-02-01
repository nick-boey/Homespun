using Bunit;
using Homespun.Components.Shared;
using Microsoft.AspNetCore.Components;

namespace Homespun.Tests.Components;

/// <summary>
/// bUnit tests for the ConfirmModal component.
/// </summary>
[TestFixture]
public class ConfirmModalTests : BunitTestContext
{
    [Test]
    public void ConfirmModal_RendersEmpty_WhenNotOpen()
    {
        // Act
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, false)
                      .Add(p => p.Title, "Confirm"));

        // Assert
        Assert.That(cut.Markup, Is.Empty.Or.EqualTo("<!--!-->"));
    }

    [Test]
    public void ConfirmModal_RendersContent_WhenOpen()
    {
        // Act
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Confirm Action"));

        // Assert
        Assert.That(cut.Find(".modal").ClassList, Does.Contain("show"));
        Assert.That(cut.Find(".modal-title").TextContent, Is.EqualTo("Confirm Action"));
    }

    [Test]
    public void ConfirmModal_DisplaysMessage_WhenProvided()
    {
        // Act
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Confirm")
                      .Add(p => p.Message, "Are you sure?"));

        // Assert
        var body = cut.Find(".modal-body");
        Assert.That(body.TextContent, Does.Contain("Are you sure?"));
    }

    [Test]
    public void ConfirmModal_DisplaysChildContent_WhenProvided()
    {
        // Act
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Confirm")
                      .Add(p => p.Message, "Should be ignored")
                      .Add(p => p.ChildContent, (RenderFragment)(builder =>
                      {
                          builder.OpenElement(0, "span");
                          builder.AddAttribute(1, "class", "custom-content");
                          builder.AddContent(2, "Custom warning content");
                          builder.CloseElement();
                      })));

        // Assert
        var customContent = cut.Find(".custom-content");
        Assert.That(customContent.TextContent, Is.EqualTo("Custom warning content"));
    }

    [Test]
    public void ConfirmModal_DisplaysDefaultButtonText()
    {
        // Act
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Confirm"));

        // Assert
        var buttons = cut.FindAll(".modal-footer button");
        Assert.That(buttons[0].TextContent.Trim(), Is.EqualTo("Cancel"));
        Assert.That(buttons[1].TextContent.Trim(), Is.EqualTo("Confirm"));
    }

    [Test]
    public void ConfirmModal_DisplaysCustomButtonText()
    {
        // Act
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Delete Item")
                      .Add(p => p.ConfirmText, "Delete")
                      .Add(p => p.CancelText, "Keep"));

        // Assert
        var buttons = cut.FindAll(".modal-footer button");
        Assert.That(buttons[0].TextContent.Trim(), Is.EqualTo("Keep"));
        Assert.That(buttons[1].TextContent.Trim(), Is.EqualTo("Delete"));
    }

    [Test]
    public void ConfirmModal_InvokesOnConfirm_WhenConfirmClicked()
    {
        // Arrange
        bool confirmCalled = false;
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Confirm")
                      .Add(p => p.OnConfirm, EventCallback.Factory.Create(this, () => confirmCalled = true)));

        // Act
        var confirmButton = cut.FindAll(".modal-footer button")[1];
        confirmButton.Click();

        // Assert
        Assert.That(confirmCalled, Is.True);
    }

    [Test]
    public void ConfirmModal_InvokesOnCancel_WhenCancelClicked()
    {
        // Arrange
        bool cancelCalled = false;
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Confirm")
                      .Add(p => p.OnCancel, EventCallback.Factory.Create(this, () => cancelCalled = true)));

        // Act
        var cancelButton = cut.FindAll(".modal-footer button")[0];
        cancelButton.Click();

        // Assert
        Assert.That(cancelCalled, Is.True);
    }

    [Test]
    public void ConfirmModal_InvokesOnCancel_WhenCloseButtonClicked()
    {
        // Arrange
        bool cancelCalled = false;
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Confirm")
                      .Add(p => p.OnCancel, EventCallback.Factory.Create(this, () => cancelCalled = true)));

        // Act
        cut.Find(".btn-close").Click();

        // Assert
        Assert.That(cancelCalled, Is.True);
    }

    [Test]
    public void ConfirmModal_AppliesPrimaryVariant_ByDefault()
    {
        // Act
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Confirm"));

        // Assert
        var confirmButton = cut.FindAll(".modal-footer button")[1];
        Assert.That(confirmButton.ClassList, Does.Contain("btn-primary"));
    }

    [Test]
    public void ConfirmModal_AppliesDangerVariant()
    {
        // Act
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Delete")
                      .Add(p => p.ConfirmVariant, ConfirmButtonVariant.Danger));

        // Assert
        var confirmButton = cut.FindAll(".modal-footer button")[1];
        Assert.That(confirmButton.ClassList, Does.Contain("btn-danger"));
    }

    [Test]
    public void ConfirmModal_AppliesWarningVariant()
    {
        // Act
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Warning")
                      .Add(p => p.ConfirmVariant, ConfirmButtonVariant.Warning));

        // Assert
        var confirmButton = cut.FindAll(".modal-footer button")[1];
        Assert.That(confirmButton.ClassList, Does.Contain("btn-warning"));
    }

    [Test]
    public void ConfirmModal_AppliesSuccessVariant()
    {
        // Act
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Success")
                      .Add(p => p.ConfirmVariant, ConfirmButtonVariant.Success));

        // Assert
        var confirmButton = cut.FindAll(".modal-footer button")[1];
        Assert.That(confirmButton.ClassList, Does.Contain("btn-success"));
    }

    [Test]
    public void ConfirmModal_ShowsSpinner_WhenProcessing()
    {
        // Act
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Confirm")
                      .Add(p => p.IsProcessing, true));

        // Assert
        var spinner = cut.FindAll(".spinner-border");
        Assert.That(spinner, Has.Count.EqualTo(1));
    }

    [Test]
    public void ConfirmModal_DisablesButtons_WhenProcessing()
    {
        // Act
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Confirm")
                      .Add(p => p.IsProcessing, true));

        // Assert
        var buttons = cut.FindAll(".modal-footer button");
        Assert.That(buttons[0].HasAttribute("disabled"), Is.True, "Cancel button should be disabled");
        Assert.That(buttons[1].HasAttribute("disabled"), Is.True, "Confirm button should be disabled");
    }

    [Test]
    public void ConfirmModal_ButtonsEnabled_WhenNotProcessing()
    {
        // Act
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Confirm")
                      .Add(p => p.IsProcessing, false));

        // Assert
        var buttons = cut.FindAll(".modal-footer button");
        Assert.That(buttons[0].HasAttribute("disabled"), Is.False, "Cancel button should not be disabled");
        Assert.That(buttons[1].HasAttribute("disabled"), Is.False, "Confirm button should not be disabled");
    }

    [Test]
    public void ConfirmModal_IsCentered_ByDefault()
    {
        // Act
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Confirm"));

        // Assert
        var dialog = cut.Find(".modal-dialog");
        Assert.That(dialog.ClassList, Does.Contain("modal-dialog-centered"));
    }

    [Test]
    public void ConfirmModal_ShowsCloseButton_ByDefault()
    {
        // Act
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Confirm"));

        // Assert
        var closeButtons = cut.FindAll(".btn-close");
        Assert.That(closeButtons, Has.Count.EqualTo(1));
    }

    [Test]
    public void ConfirmModal_HidesCloseButton_WhenDisabled()
    {
        // Act
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Confirm")
                      .Add(p => p.ShowCloseButton, false));

        // Assert
        var closeButtons = cut.FindAll(".btn-close");
        Assert.That(closeButtons, Has.Count.EqualTo(0));
    }
}
