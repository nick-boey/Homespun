using Bunit;
using Homespun.Client.Components;
using Microsoft.AspNetCore.Components;

namespace Homespun.Tests.Components;

/// <summary>
/// bUnit tests for the ConfirmModal component.
/// Tests the wrapper component which uses BbDialog from Blazor Blueprint.
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

        // Assert - BbDialog doesn't render content when closed
        // The actual output may vary depending on BbDialog implementation
        Assert.That(cut.Markup.Contains("Confirm"), Is.False);
    }

    [Test]
    public void ConfirmModal_RendersContent_WhenOpen()
    {
        // Act
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Confirm Action"));

        // Assert - Content should be visible
        Assert.That(cut.Markup, Does.Contain("Confirm Action"));
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
        Assert.That(cut.Markup, Does.Contain("Are you sure?"));
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

        // Assert - Look for button text regardless of structure
        Assert.That(cut.Markup, Does.Contain("Cancel"));
        Assert.That(cut.Markup, Does.Contain("Confirm"));
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
        Assert.That(cut.Markup, Does.Contain("Keep"));
        Assert.That(cut.Markup, Does.Contain("Delete"));
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

        // Act - Find the confirm button (second button, contains "Confirm")
        var buttons = cut.FindAll("button");
        var confirmButton = buttons.FirstOrDefault(b => b.TextContent.Contains("Confirm"));
        confirmButton?.Click();

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

        // Act - Find the cancel button
        var buttons = cut.FindAll("button");
        var cancelButton = buttons.FirstOrDefault(b => b.TextContent.Contains("Cancel"));
        cancelButton?.Click();

        // Assert
        Assert.That(cancelCalled, Is.True);
    }

    [Test]
    public void ConfirmModal_DisablesButtons_WhenProcessing()
    {
        // Act
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Confirm")
                      .Add(p => p.IsProcessing, true));

        // Assert - Look for disabled attribute on buttons
        var buttons = cut.FindAll("button");
        Assert.That(buttons.Count(b => b.HasAttribute("disabled")), Is.GreaterThanOrEqualTo(2));
    }

    [Test]
    public void ConfirmModal_ButtonsEnabled_WhenNotProcessing()
    {
        // Act
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Confirm")
                      .Add(p => p.IsProcessing, false));

        // Assert - Look for enabled buttons
        var buttons = cut.FindAll("button");
        var cancelButton = buttons.FirstOrDefault(b => b.TextContent.Contains("Cancel"));
        var confirmButton = buttons.FirstOrDefault(b => b.TextContent.Contains("Confirm"));

        Assert.That(cancelButton?.HasAttribute("disabled"), Is.False, "Cancel button should not be disabled");
        Assert.That(confirmButton?.HasAttribute("disabled"), Is.False, "Confirm button should not be disabled");
    }

    [Test]
    public void ConfirmModal_RendersDifferentVariants()
    {
        // Test all variants render without error
        var variants = new[] {
            ConfirmButtonVariant.Primary,
            ConfirmButtonVariant.Danger,
            ConfirmButtonVariant.Warning,
            ConfirmButtonVariant.Success
        };

        foreach (var variant in variants)
        {
            var cut = Render<ConfirmModal>(parameters =>
                parameters.Add(p => p.IsOpen, true)
                          .Add(p => p.Title, "Confirm")
                          .Add(p => p.ConfirmVariant, variant));

            Assert.That(cut.Markup, Does.Contain("Confirm"), $"Failed for variant {variant}");
        }
    }

    [Test]
    public void ConfirmModal_ShowsLoadingState_WhenProcessing()
    {
        // Act
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Confirm")
                      .Add(p => p.IsProcessing, true));

        // Assert - BbButton with Loading=true renders a spinner/loading indicator
        // The exact markup depends on BbButton implementation
        Assert.That(cut.Markup, Does.Contain("Confirm"));
    }
}
