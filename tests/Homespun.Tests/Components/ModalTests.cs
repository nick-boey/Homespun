using Bunit;
using Homespun.Client.Components;
using Microsoft.AspNetCore.Components;

namespace Homespun.Tests.Components;

/// <summary>
/// bUnit tests for the Modal component.
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

        // Assert
        Assert.That(cut.Markup, Is.Empty.Or.EqualTo("<!--!-->"));
    }

    [Test]
    public void Modal_RendersContent_WhenOpen()
    {
        // Act
        var cut = Render<Modal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Test Modal"));

        // Assert
        Assert.That(cut.Find(".modal").ClassList, Does.Contain("show"));
        Assert.That(cut.Find(".modal-backdrop").ClassList, Does.Contain("show"));
    }

    [Test]
    public void Modal_DisplaysTitle_WhenProvided()
    {
        // Act
        var cut = Render<Modal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "My Test Title"));

        // Assert
        var title = cut.Find(".modal-title");
        Assert.That(title.TextContent, Is.EqualTo("My Test Title"));
    }

    [Test]
    public void Modal_DisplaysCustomHeader_WhenProvided()
    {
        // Act
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

        // Assert
        var customHeader = cut.Find(".custom-header");
        Assert.That(customHeader.TextContent, Is.EqualTo("Custom Header Content"));
    }

    [Test]
    public void Modal_DisplaysBodyContent()
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

        // Assert
        var body = cut.Find(".modal-body");
        Assert.That(body.TextContent, Does.Contain("Body content here"));
    }

    [Test]
    public void Modal_DisplaysChildContent_WhenBodyNotProvided()
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

        // Assert
        var body = cut.Find(".modal-body");
        Assert.That(body.TextContent, Does.Contain("Child content"));
    }

    [Test]
    public void Modal_DisplaysFooter_WhenProvided()
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

        // Assert
        var footer = cut.Find(".modal-footer");
        var button = footer.QuerySelector(".test-button");
        Assert.That(button, Is.Not.Null);
        Assert.That(button!.TextContent, Is.EqualTo("OK"));
    }

    [Test]
    public void Modal_ShowsCloseButton_ByDefault()
    {
        // Act
        var cut = Render<Modal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Test"));

        // Assert
        var closeButton = cut.Find(".btn-close");
        Assert.That(closeButton, Is.Not.Null);
    }

    [Test]
    public void Modal_HidesCloseButton_WhenDisabled()
    {
        // Act
        var cut = Render<Modal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Test")
                      .Add(p => p.ShowCloseButton, false));

        // Assert
        var closeButtons = cut.FindAll(".btn-close");
        Assert.That(closeButtons, Has.Count.EqualTo(0));
    }

    [Test]
    public void Modal_InvokesOnClose_WhenCloseButtonClicked()
    {
        // Arrange
        bool closeCalled = false;
        var cut = Render<Modal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Test")
                      .Add(p => p.OnClose, EventCallback.Factory.Create(this, () => closeCalled = true)));

        // Act
        cut.Find(".btn-close").Click();

        // Assert
        Assert.That(closeCalled, Is.True);
    }

    [Test]
    public void Modal_InvokesOnClose_WhenBackdropClicked()
    {
        // Arrange
        bool closeCalled = false;
        var cut = Render<Modal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Test")
                      .Add(p => p.OnClose, EventCallback.Factory.Create(this, () => closeCalled = true)));

        // Act
        cut.Find(".modal-backdrop").Click();

        // Assert
        Assert.That(closeCalled, Is.True);
    }

    [Test]
    public void Modal_DoesNotClose_WhenBackdropClickDisabled()
    {
        // Arrange
        bool closeCalled = false;
        var cut = Render<Modal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Test")
                      .Add(p => p.CloseOnBackdropClick, false)
                      .Add(p => p.OnClose, EventCallback.Factory.Create(this, () => closeCalled = true)));

        // Act
        cut.Find(".modal-backdrop").Click();

        // Assert
        Assert.That(closeCalled, Is.False);
    }

    [Test]
    public void Modal_AppliesSmSize()
    {
        // Act
        var cut = Render<Modal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Test")
                      .Add(p => p.Size, ModalSize.Sm));

        // Assert
        var dialog = cut.Find(".modal-dialog");
        Assert.That(dialog.ClassList, Does.Contain("modal-sm"));
    }

    [Test]
    public void Modal_AppliesLgSize()
    {
        // Act
        var cut = Render<Modal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Test")
                      .Add(p => p.Size, ModalSize.Lg));

        // Assert
        var dialog = cut.Find(".modal-dialog");
        Assert.That(dialog.ClassList, Does.Contain("modal-lg"));
    }

    [Test]
    public void Modal_AppliesXlSize()
    {
        // Act
        var cut = Render<Modal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Test")
                      .Add(p => p.Size, ModalSize.Xl));

        // Assert
        var dialog = cut.Find(".modal-dialog");
        Assert.That(dialog.ClassList, Does.Contain("modal-xl"));
    }

    [Test]
    public void Modal_AppliesCenteredClass_WhenEnabled()
    {
        // Act
        var cut = Render<Modal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Test")
                      .Add(p => p.Centered, true));

        // Assert
        var dialog = cut.Find(".modal-dialog");
        Assert.That(dialog.ClassList, Does.Contain("modal-dialog-centered"));
    }

    [Test]
    public void Modal_DoesNotApplyCentered_ByDefault()
    {
        // Act
        var cut = Render<Modal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Test"));

        // Assert
        var dialog = cut.Find(".modal-dialog");
        Assert.That(dialog.ClassList, Does.Not.Contain("modal-dialog-centered"));
    }

    [Test]
    public void Modal_AppliesCustomDialogClass()
    {
        // Act
        var cut = Render<Modal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Test")
                      .Add(p => p.DialogClass, "my-custom-class"));

        // Assert
        var dialog = cut.Find(".modal-dialog");
        Assert.That(dialog.ClassList, Does.Contain("my-custom-class"));
    }

    [Test]
    public void Modal_CombinesMultipleDialogClasses()
    {
        // Act
        var cut = Render<Modal>(parameters =>
            parameters.Add(p => p.IsOpen, true)
                      .Add(p => p.Title, "Test")
                      .Add(p => p.Size, ModalSize.Lg)
                      .Add(p => p.Centered, true)
                      .Add(p => p.DialogClass, "extra-class"));

        // Assert
        var dialog = cut.Find(".modal-dialog");
        Assert.That(dialog.ClassList, Does.Contain("modal-lg"));
        Assert.That(dialog.ClassList, Does.Contain("modal-dialog-centered"));
        Assert.That(dialog.ClassList, Does.Contain("extra-class"));
    }
}
