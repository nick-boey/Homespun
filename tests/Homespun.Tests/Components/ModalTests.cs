using Bunit;
using Homespun.Client.Components;
using Homespun.Client.Features.Shared.Components;
using Microsoft.AspNetCore.Components;

namespace Homespun.Tests.Components;

/// <summary>
/// bUnit tests for the Modal component.
///
/// Note: The Modal component uses BbDialog from Blazor Blueprint which renders
/// content via a portal (outside the component's DOM tree). This means content
/// is not directly accessible in bUnit tests. These tests verify:
/// - Component parameters are handled correctly
/// - The component renders without errors
/// - Callbacks can be wired up
///
/// Visual and interactive verification should be done via E2E tests.
/// </summary>
[TestFixture]
public class ModalTests : BunitTestContext
{
    [Test]
    public void Modal_RendersEmpty_WhenNotOpen()
    {
        // Act
        var cut = Render<Modal>(parameters =>
            parameters.Add(p => p.IsOpen, false)
                      .Add(p => p.Title, "Test Modal"));

        // Assert - BbDialog renders nothing when closed
        Assert.That(cut.Markup, Is.Empty.Or.EqualTo("<!--!-->"));
    }

    [Test]
    public void Modal_RendersWithoutError_WhenOpen()
    {
        // Act - verify component renders without throwing
        var cut = Render<Modal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Test Modal"));

        // Assert - component renders (content is portal-rendered, not directly accessible)
        Assert.That(cut.Instance, Is.Not.Null);
        Assert.That(cut.Instance.IsOpen, Is.True);
    }

    [Test]
    public void Modal_SetsParametersCorrectly()
    {
        // Act
        var cut = Render<Modal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "My Test Title")
                      .Add(p => p.Size, ModalSize.Lg)
                      .Add(p => p.Centered, true)
                      .Add(p => p.ShowCloseButton, false)
                      .Add(p => p.CloseOnBackdropClick, false)
                      .Add(p => p.DialogClass, "custom-class"));

        // Assert - verify parameters are set on instance
        Assert.That(cut.Instance.Title, Is.EqualTo("My Test Title"));
        Assert.That(cut.Instance.Size, Is.EqualTo(ModalSize.Lg));
        Assert.That(cut.Instance.Centered, Is.True);
        Assert.That(cut.Instance.ShowCloseButton, Is.False);
        Assert.That(cut.Instance.CloseOnBackdropClick, Is.False);
        Assert.That(cut.Instance.DialogClass, Is.EqualTo("custom-class"));
    }

    [Test]
    public void Modal_AcceptsHeaderRenderFragment()
    {
        // Act - verify component accepts Header without error
        var cut = Render<Modal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Should be ignored")
                      .Add(p => p.Header, (RenderFragment)(builder =>
                      {
                          builder.OpenElement(0, "span");
                          builder.AddAttribute(1, "class", "custom-header");
                          builder.AddContent(2, "Custom Header Content");
                          builder.CloseElement();
                      })));

        // Assert - component has Header set
        Assert.That(cut.Instance.Header, Is.Not.Null);
    }

    [Test]
    public void Modal_AcceptsBodyRenderFragment()
    {
        // Act
        var cut = Render<Modal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Test")
                      .Add(p => p.Body, (RenderFragment)(builder =>
                      {
                          builder.OpenElement(0, "p");
                          builder.AddContent(1, "Body content here");
                          builder.CloseElement();
                      })));

        // Assert - component has Body set
        Assert.That(cut.Instance.Body, Is.Not.Null);
    }

    [Test]
    public void Modal_AcceptsChildContent()
    {
        // Act
        var cut = Render<Modal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Test")
                      .Add(p => p.ChildContent, (RenderFragment)(builder =>
                      {
                          builder.OpenElement(0, "span");
                          builder.AddContent(1, "Child content");
                          builder.CloseElement();
                      })));

        // Assert - component has ChildContent set
        Assert.That(cut.Instance.ChildContent, Is.Not.Null);
    }

    [Test]
    public void Modal_AcceptsFooterRenderFragment()
    {
        // Act
        var cut = Render<Modal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Test")
                      .Add(p => p.Footer, (RenderFragment)(builder =>
                      {
                          builder.OpenElement(0, "button");
                          builder.AddAttribute(1, "class", "test-button");
                          builder.AddContent(2, "OK");
                          builder.CloseElement();
                      })));

        // Assert - component has Footer set
        Assert.That(cut.Instance.Footer, Is.Not.Null);
    }

    [Test]
    public void Modal_DefaultsShowCloseButton_ToTrue()
    {
        // Act
        var cut = Render<Modal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Test"));

        // Assert
        Assert.That(cut.Instance.ShowCloseButton, Is.True);
    }

    [Test]
    public void Modal_CanDisableCloseButton()
    {
        // Act
        var cut = Render<Modal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Test")
                      .Add(p => p.ShowCloseButton, false));

        // Assert
        Assert.That(cut.Instance.ShowCloseButton, Is.False);
    }

    [Test]
    public void Modal_AcceptsOnCloseCallback()
    {
        // Arrange
        bool closeCalled = false;

        // Act
        var cut = Render<Modal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Test")
                      .Add(p => p.OnClose, EventCallback.Factory.Create(this, () => closeCalled = true)));

        // Assert - callback is wired up (actual invocation tested via E2E)
        Assert.That(cut.Instance.OnClose.HasDelegate, Is.True);
    }

    [Test]
    public void Modal_DefaultsCloseOnBackdropClick_ToTrue()
    {
        // Act
        var cut = Render<Modal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Test"));

        // Assert
        Assert.That(cut.Instance.CloseOnBackdropClick, Is.True);
    }

    [Test]
    public void Modal_CanDisableBackdropClick()
    {
        // Act
        var cut = Render<Modal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Test")
                      .Add(p => p.CloseOnBackdropClick, false));

        // Assert
        Assert.That(cut.Instance.CloseOnBackdropClick, Is.False);
    }

    [Test]
    public void Modal_DefaultsToDefaultSize()
    {
        // Act
        var cut = Render<Modal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Test"));

        // Assert
        Assert.That(cut.Instance.Size, Is.EqualTo(ModalSize.Default));
    }

    [Test]
    public void Modal_AcceptsSmSize()
    {
        // Act
        var cut = Render<Modal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Test")
                      .Add(p => p.Size, ModalSize.Sm));

        // Assert
        Assert.That(cut.Instance.Size, Is.EqualTo(ModalSize.Sm));
    }

    [Test]
    public void Modal_AcceptsLgSize()
    {
        // Act
        var cut = Render<Modal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Test")
                      .Add(p => p.Size, ModalSize.Lg));

        // Assert
        Assert.That(cut.Instance.Size, Is.EqualTo(ModalSize.Lg));
    }

    [Test]
    public void Modal_AcceptsXlSize()
    {
        // Act
        var cut = Render<Modal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Test")
                      .Add(p => p.Size, ModalSize.Xl));

        // Assert
        Assert.That(cut.Instance.Size, Is.EqualTo(ModalSize.Xl));
    }

    [Test]
    public void Modal_DefaultsCenteredToFalse()
    {
        // Act
        var cut = Render<Modal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Test"));

        // Assert
        Assert.That(cut.Instance.Centered, Is.False);
    }

    [Test]
    public void Modal_CanEnableCentered()
    {
        // Act
        var cut = Render<Modal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Test")
                      .Add(p => p.Centered, true));

        // Assert
        Assert.That(cut.Instance.Centered, Is.True);
    }

    [Test]
    public void Modal_AcceptsDialogClass()
    {
        // Act
        var cut = Render<Modal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Test")
                      .Add(p => p.DialogClass, "my-custom-class"));

        // Assert
        Assert.That(cut.Instance.DialogClass, Is.EqualTo("my-custom-class"));
    }

    [Test]
    public void Modal_CombinesMultipleParameters()
    {
        // Act
        var cut = Render<Modal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Test")
                      .Add(p => p.Size, ModalSize.Lg)
                      .Add(p => p.Centered, true)
                      .Add(p => p.DialogClass, "extra-class"));

        // Assert
        Assert.That(cut.Instance.Size, Is.EqualTo(ModalSize.Lg));
        Assert.That(cut.Instance.Centered, Is.True);
        Assert.That(cut.Instance.DialogClass, Is.EqualTo("extra-class"));
    }
}
