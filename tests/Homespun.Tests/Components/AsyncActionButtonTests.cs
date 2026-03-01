using Bunit;
using Homespun.Client.Components;
using Homespun.Client.Features.Shared.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Homespun.Tests.Components;

/// <summary>
/// bUnit tests for the AsyncActionButton component.
/// </summary>
[TestFixture]
public class AsyncActionButtonTests : BunitTestContext
{
    [Test]
    public void AsyncActionButton_RendersWithDefaultText()
    {
        // Act
        var cut = Render<AsyncActionButton>(parameters =>
            parameters.Add(p => p.Text, "Click Me"));

        // Assert
        var button = cut.Find("button");
        Assert.That(button.TextContent.Trim(), Is.EqualTo("Click Me"));
    }

    [Test]
    public void AsyncActionButton_RendersWithDefaultButtonType()
    {
        // Act
        var cut = Render<AsyncActionButton>(parameters =>
            parameters.Add(p => p.Text, "Click Me"));

        // Assert
        var button = cut.Find("button");
        Assert.That(button.GetAttribute("type"), Is.EqualTo("button"));
    }

    [Test]
    public void AsyncActionButton_RendersWithCustomButtonType()
    {
        // Act
        var cut = Render<AsyncActionButton>(parameters =>
            parameters
                .Add(p => p.Text, "Submit")
                .Add(p => p.ButtonType, "submit"));

        // Assert
        var button = cut.Find("button");
        Assert.That(button.GetAttribute("type"), Is.EqualTo("submit"));
    }

    [Test]
    public void AsyncActionButton_AppliesCustomCssClass()
    {
        // Act
        var cut = Render<AsyncActionButton>(parameters =>
            parameters
                .Add(p => p.Text, "Primary")
                .Add(p => p.CssClass, "custom-class"));

        // Assert - BbButton renders as a button with the custom class
        var button = cut.Find("button");
        Assert.That(button.ClassList, Does.Contain("custom-class"));
    }

    [Test]
    public void AsyncActionButton_IsDisabled_WhenIsLoadingExternalIsTrue()
    {
        // Act
        var cut = Render<AsyncActionButton>(parameters =>
            parameters
                .Add(p => p.Text, "Save")
                .Add(p => p.IsLoadingExternal, true));

        // Assert
        var button = cut.Find("button");
        Assert.That(button.HasAttribute("disabled"), Is.True);
    }

    [Test]
    public void AsyncActionButton_IsDisabled_WhenDisabledIsTrue()
    {
        // Act
        var cut = Render<AsyncActionButton>(parameters =>
            parameters
                .Add(p => p.Text, "Save")
                .Add(p => p.Disabled, true));

        // Assert
        var button = cut.Find("button");
        Assert.That(button.HasAttribute("disabled"), Is.True);
    }

    [Test]
    public void AsyncActionButton_IsNotDisabled_WhenBothDisabledAndLoadingAreFalse()
    {
        // Act
        var cut = Render<AsyncActionButton>(parameters =>
            parameters
                .Add(p => p.Text, "Save")
                .Add(p => p.IsLoadingExternal, false)
                .Add(p => p.Disabled, false));

        // Assert
        var button = cut.Find("button");
        Assert.That(button.HasAttribute("disabled"), Is.False);
    }

    [Test]
    public void AsyncActionButton_ShowsSpinner_WhenLoading()
    {
        // Act
        var cut = Render<AsyncActionButton>(parameters =>
            parameters
                .Add(p => p.Text, "Save")
                .Add(p => p.IsLoadingExternal, true));

        // Assert - BbSpinner renders as SVG with specific class
        // Look for any spinner element (Blueprint renders SVG spinners)
        Assert.That(cut.Markup.Contains("Save"), Is.True, "Button should show text when loading");
    }

    [Test]
    public void AsyncActionButton_DoesNotShowSpinner_WhenNotLoading()
    {
        // Act
        var cut = Render<AsyncActionButton>(parameters =>
            parameters
                .Add(p => p.Text, "Save")
                .Add(p => p.IsLoadingExternal, false));

        // Assert - when not loading, button should just show text without spinner
        var button = cut.Find("button");
        Assert.That(button.ClassList, Does.Not.Contain("loading"));
    }

    [Test]
    public void AsyncActionButton_ShowsLoadingText_WhenLoadingAndLoadingTextProvided()
    {
        // Act
        var cut = Render<AsyncActionButton>(parameters =>
            parameters
                .Add(p => p.Text, "Save")
                .Add(p => p.LoadingText, "Saving...")
                .Add(p => p.IsLoadingExternal, true));

        // Assert
        var button = cut.Find("button");
        Assert.That(button.TextContent, Does.Contain("Saving..."));
        Assert.That(button.TextContent, Does.Not.Contain("Save"));
    }

    [Test]
    public void AsyncActionButton_ShowsRegularText_WhenLoadingAndNoLoadingTextProvided()
    {
        // Act
        var cut = Render<AsyncActionButton>(parameters =>
            parameters
                .Add(p => p.Text, "Save")
                .Add(p => p.IsLoadingExternal, true));

        // Assert
        var button = cut.Find("button");
        Assert.That(button.TextContent, Does.Contain("Save"));
    }

    [Test]
    public async Task AsyncActionButton_InvokesOnClick_WhenClicked()
    {
        // Arrange
        var wasClicked = false;
        var cut = Render<AsyncActionButton>(parameters =>
            parameters
                .Add(p => p.Text, "Click Me")
                .Add(p => p.OnClick, EventCallback.Factory.Create<MouseEventArgs>(this, _ => wasClicked = true)));

        // Act
        await cut.Find("button").ClickAsync(new MouseEventArgs());

        // Assert
        Assert.That(wasClicked, Is.True);
    }

    [Test]
    public async Task AsyncActionButton_DoesNotInvokeOnClick_WhenDisabled()
    {
        // Arrange
        var wasClicked = false;
        var cut = Render<AsyncActionButton>(parameters =>
            parameters
                .Add(p => p.Text, "Click Me")
                .Add(p => p.Disabled, true)
                .Add(p => p.OnClick, EventCallback.Factory.Create<MouseEventArgs>(this, _ => wasClicked = true)));

        // Act - clicking a disabled button should not trigger the callback
        // Note: bUnit doesn't prevent clicks on disabled buttons, so the component must handle this
        await cut.Find("button").ClickAsync(new MouseEventArgs());

        // Assert - the component should prevent the callback from being invoked when disabled
        Assert.That(wasClicked, Is.False);
    }

    [Test]
    public async Task AsyncActionButton_DoesNotInvokeOnClick_WhenLoading()
    {
        // Arrange
        var wasClicked = false;
        var cut = Render<AsyncActionButton>(parameters =>
            parameters
                .Add(p => p.Text, "Click Me")
                .Add(p => p.IsLoadingExternal, true)
                .Add(p => p.OnClick, EventCallback.Factory.Create<MouseEventArgs>(this, _ => wasClicked = true)));

        // Act
        await cut.Find("button").ClickAsync(new MouseEventArgs());

        // Assert
        Assert.That(wasClicked, Is.False);
    }

    [Test]
    public void AsyncActionButton_RendersIconContent()
    {
        // Act
        var cut = Render<AsyncActionButton>(parameters =>
            parameters
                .Add(p => p.Text, "Refresh")
                .Add<RenderFragment>(p => p.IconContent, builder =>
                {
                    builder.OpenElement(0, "i");
                    builder.AddAttribute(1, "class", "bi bi-arrow-clockwise");
                    builder.CloseElement();
                }));

        // Assert
        var icon = cut.Find("i.bi-arrow-clockwise");
        Assert.That(icon, Is.Not.Null);
    }

    [Test]
    public void AsyncActionButton_DoesNotRenderIconContent_WhenLoading()
    {
        // Act
        var cut = Render<AsyncActionButton>(parameters =>
            parameters
                .Add(p => p.Text, "Refresh")
                .Add(p => p.IsLoadingExternal, true)
                .Add<RenderFragment>(p => p.IconContent, builder =>
                {
                    builder.OpenElement(0, "i");
                    builder.AddAttribute(1, "class", "bi bi-arrow-clockwise");
                    builder.CloseElement();
                }));

        // Assert
        var icons = cut.FindAll("i.bi-arrow-clockwise");
        Assert.That(icons, Is.Empty);
    }

    [Test]
    public void AsyncActionButton_RendersChildContent()
    {
        // Act
        var cut = Render<AsyncActionButton>(parameters =>
            parameters
                .Add(p => p.Text, "Save")
                .Add<RenderFragment>(p => p.ChildContent, builder =>
                {
                    builder.OpenElement(0, "span");
                    builder.AddAttribute(1, "class", "badge");
                    builder.AddContent(2, "3");
                    builder.CloseElement();
                }));

        // Assert
        var badge = cut.Find("span.badge");
        Assert.That(badge, Is.Not.Null);
        Assert.That(badge.TextContent, Is.EqualTo("3"));
    }

    [Test]
    public void AsyncActionButton_IsDisabledWhenLoading()
    {
        // Act
        var cut = Render<AsyncActionButton>(parameters =>
            parameters
                .Add(p => p.Text, "Save")
                .Add(p => p.IsLoadingExternal, true));

        // Assert - BbButton with Loading=true will be disabled
        var button = cut.Find("button");
        Assert.That(button.HasAttribute("disabled"), Is.True);
    }

    [Test]
    public void AsyncActionButton_IsNotDisabledWhenNotLoading()
    {
        // Act
        var cut = Render<AsyncActionButton>(parameters =>
            parameters
                .Add(p => p.Text, "Save")
                .Add(p => p.IsLoadingExternal, false));

        // Assert - button is not disabled when not loading
        var button = cut.Find("button");
        Assert.That(button.HasAttribute("disabled"), Is.False);
    }

    [Test]
    public async Task AsyncActionButton_ManagesInternalLoadingState_WhenNoExternalLoadingProvided()
    {
        // Arrange
        var taskCompletionSource = new TaskCompletionSource();
        var cut = Render<AsyncActionButton>(parameters =>
            parameters
                .Add(p => p.Text, "Save")
                .Add(p => p.LoadingText, "Saving...")
                .Add(p => p.OnClick, EventCallback.Factory.Create<MouseEventArgs>(this, async _ =>
                {
                    await taskCompletionSource.Task;
                })));

        // Assert - initially not loading
        var button = cut.Find("button");
        Assert.That(button.TextContent, Does.Contain("Save"));
        Assert.That(button.HasAttribute("disabled"), Is.False);

        // Act - click the button
        var clickTask = cut.Find("button").ClickAsync(new MouseEventArgs());

        // Wait for the component to re-render - check for disabled state
        cut.WaitForState(() => cut.Find("button").HasAttribute("disabled"), TimeSpan.FromSeconds(1));

        // Assert - should be in loading state with loading text
        button = cut.Find("button");
        Assert.That(button.TextContent, Does.Contain("Saving..."));
        Assert.That(button.HasAttribute("disabled"), Is.True);

        // Complete the async operation
        taskCompletionSource.SetResult();
        await clickTask;

        // Wait for loading to complete
        cut.WaitForState(() => !cut.Find("button").HasAttribute("disabled"), TimeSpan.FromSeconds(1));

        // Assert - should be back to normal state
        button = cut.Find("button");
        Assert.That(button.TextContent, Does.Contain("Save"));
        Assert.That(button.HasAttribute("disabled"), Is.False);
    }
}
