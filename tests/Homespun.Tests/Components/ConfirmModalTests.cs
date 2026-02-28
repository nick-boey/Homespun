using Bunit;
using Homespun.Client.Components;
using Microsoft.AspNetCore.Components;

namespace Homespun.Tests.Components;

/// <summary>
/// bUnit tests for the ConfirmModal component.
///
/// Note: ConfirmModal uses Modal which uses BbDialog from Blazor Blueprint.
/// BbDialog renders content via a portal (outside the component's DOM tree),
/// so content is not directly accessible in bUnit tests. These tests verify:
/// - Component parameters are handled correctly
/// - The component renders without errors
/// - Callbacks can be wired up
///
/// Visual and interactive verification (button clicks, etc.) should be done via E2E tests.
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
        Assert.That(cut.Markup, Is.Empty.Or.EqualTo("<!--!-->"));
    }

    [Test]
    public void ConfirmModal_RendersWithoutError_WhenOpen()
    {
        // Act
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Confirm Action"));

        // Assert - component renders without error (content is portal-rendered)
        Assert.That(cut.Instance, Is.Not.Null);
        Assert.That(cut.Instance.IsOpen, Is.True);
        Assert.That(cut.Instance.Title, Is.EqualTo("Confirm Action"));
    }

    [Test]
    public void ConfirmModal_SetsMessageParameter()
    {
        // Act
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Confirm")
                      .Add(p => p.Message, "Are you sure?"));

        // Assert
        Assert.That(cut.Instance.Message, Is.EqualTo("Are you sure?"));
    }

    [Test]
    public void ConfirmModal_AcceptsChildContent()
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

        // Assert - component has ChildContent set
        Assert.That(cut.Instance.ChildContent, Is.Not.Null);
    }

    [Test]
    public void ConfirmModal_DefaultsButtonText()
    {
        // Act
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Confirm"));

        // Assert - verify default button text parameters
        Assert.That(cut.Instance.CancelText, Is.EqualTo("Cancel"));
        Assert.That(cut.Instance.ConfirmText, Is.EqualTo("Confirm"));
    }

    [Test]
    public void ConfirmModal_AcceptsCustomButtonText()
    {
        // Act
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Delete Item")
                      .Add(p => p.ConfirmText, "Delete")
                      .Add(p => p.CancelText, "Keep"));

        // Assert
        Assert.That(cut.Instance.CancelText, Is.EqualTo("Keep"));
        Assert.That(cut.Instance.ConfirmText, Is.EqualTo("Delete"));
    }

    [Test]
    public void ConfirmModal_AcceptsOnConfirmCallback()
    {
        // Arrange
        bool confirmCalled = false;

        // Act
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Confirm")
                      .Add(p => p.OnConfirm, EventCallback.Factory.Create(this, () => confirmCalled = true)));

        // Assert - callback is wired up (actual invocation tested via E2E)
        Assert.That(cut.Instance.OnConfirm.HasDelegate, Is.True);
    }

    [Test]
    public void ConfirmModal_AcceptsOnCancelCallback()
    {
        // Arrange
        bool cancelCalled = false;

        // Act
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Confirm")
                      .Add(p => p.OnCancel, EventCallback.Factory.Create(this, () => cancelCalled = true)));

        // Assert - callback is wired up (actual invocation tested via E2E)
        Assert.That(cut.Instance.OnCancel.HasDelegate, Is.True);
    }

    [Test]
    public void ConfirmModal_SetsIsProcessingParameter()
    {
        // Act
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Confirm")
                      .Add(p => p.IsProcessing, true));

        // Assert
        Assert.That(cut.Instance.IsProcessing, Is.True);
    }

    [Test]
    public void ConfirmModal_DefaultsIsProcessingToFalse()
    {
        // Act
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Confirm"));

        // Assert
        Assert.That(cut.Instance.IsProcessing, Is.False);
    }

    [Test]
    public void ConfirmModal_AcceptsAllVariants()
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

            Assert.That(cut.Instance.ConfirmVariant, Is.EqualTo(variant), $"Failed for variant {variant}");
        }
    }

    [Test]
    public void ConfirmModal_DefaultsVariantToPrimary()
    {
        // Act
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Confirm"));

        // Assert
        Assert.That(cut.Instance.ConfirmVariant, Is.EqualTo(ConfirmButtonVariant.Primary));
    }

    [Test]
    public void ConfirmModal_DefaultsShowCloseButtonToTrue()
    {
        // Act
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Confirm"));

        // Assert
        Assert.That(cut.Instance.ShowCloseButton, Is.True);
    }

    [Test]
    public void ConfirmModal_CanDisableCloseButton()
    {
        // Act
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Confirm")
                      .Add(p => p.ShowCloseButton, false));

        // Assert
        Assert.That(cut.Instance.ShowCloseButton, Is.False);
    }

    [Test]
    public void ConfirmModal_DefaultsCloseOnBackdropClickToTrue()
    {
        // Act
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Confirm"));

        // Assert
        Assert.That(cut.Instance.CloseOnBackdropClick, Is.True);
    }

    [Test]
    public void ConfirmModal_CanDisableBackdropClick()
    {
        // Act
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Confirm")
                      .Add(p => p.CloseOnBackdropClick, false));

        // Assert
        Assert.That(cut.Instance.CloseOnBackdropClick, Is.False);
    }

    [Test]
    public void ConfirmModal_CombinesMultipleParameters()
    {
        // Act
        var cut = Render<ConfirmModal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Delete Item")
                      .Add(p => p.Message, "Are you sure?")
                      .Add(p => p.ConfirmText, "Delete")
                      .Add(p => p.CancelText, "Keep")
                      .Add(p => p.ConfirmVariant, ConfirmButtonVariant.Danger)
                      .Add(p => p.IsProcessing, false)
                      .Add(p => p.ShowCloseButton, false)
                      .Add(p => p.CloseOnBackdropClick, false));

        // Assert
        Assert.That(cut.Instance.Title, Is.EqualTo("Delete Item"));
        Assert.That(cut.Instance.Message, Is.EqualTo("Are you sure?"));
        Assert.That(cut.Instance.ConfirmText, Is.EqualTo("Delete"));
        Assert.That(cut.Instance.CancelText, Is.EqualTo("Keep"));
        Assert.That(cut.Instance.ConfirmVariant, Is.EqualTo(ConfirmButtonVariant.Danger));
        Assert.That(cut.Instance.IsProcessing, Is.False);
        Assert.That(cut.Instance.ShowCloseButton, Is.False);
        Assert.That(cut.Instance.CloseOnBackdropClick, Is.False);
    }
}
