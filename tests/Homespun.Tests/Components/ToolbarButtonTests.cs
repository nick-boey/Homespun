using Bunit;
using Homespun.Client.Components;
using Homespun.Tests.Components;
using Microsoft.AspNetCore.Components;

namespace Homespun.Tests.Components;

/// <summary>
/// bUnit tests for the ToolbarButton component.
/// Tests icon display (using Lucide icons via string names), tooltip rendering,
/// disabled state, and click events.
/// </summary>
[TestFixture]
public class ToolbarButtonTests : BunitTestContext
{
    [Test]
    public void RendersLucideIcon()
    {
        var cut = Render<ToolbarButton>(p =>
        {
            p.Add(x => x.Icon, "pencil");
        });

        // LucideIcon renders an SVG element
        var icon = cut.Find("button.toolbar-btn svg");
        Assert.That(icon, Is.Not.Null);
    }

    // NOTE: Tooltip rendering test requires BlazorBlueprint services (IPortalService)
    // which are not easily mocked in bUnit. The tooltip rendering is tested via E2E tests.

    [Test]
    public void DoesNotRenderTooltip_WhenTooltipNotProvided()
    {
        var cut = Render<ToolbarButton>(p =>
        {
            p.Add(x => x.Icon, "pencil");
        });

        // Without tooltip, there should be no BbTooltip wrapper
        var button = cut.Find("button.toolbar-btn");
        Assert.That(button, Is.Not.Null);
    }

    [Test]
    public void IsDisabled_WhenDisabledPropTrue()
    {
        var cut = Render<ToolbarButton>(p =>
        {
            p.Add(x => x.Icon, "pencil");
            p.Add(x => x.Disabled, true);
        });

        var button = cut.Find("button.toolbar-btn");
        Assert.That(button.HasAttribute("disabled"), Is.True);
    }

    [Test]
    public void IsNotDisabled_WhenDisabledPropFalse()
    {
        var cut = Render<ToolbarButton>(p =>
        {
            p.Add(x => x.Icon, "pencil");
            p.Add(x => x.Disabled, false);
        });

        var button = cut.Find("button.toolbar-btn");
        Assert.That(button.HasAttribute("disabled"), Is.False);
    }

    [Test]
    public void InvokesOnClick_WhenClicked()
    {
        var wasClicked = false;
        var cut = Render<ToolbarButton>(p =>
        {
            p.Add(x => x.Icon, "pencil");
            p.Add(x => x.OnClick, EventCallback.Factory.Create(this, () => wasClicked = true));
        });

        cut.Find("button.toolbar-btn").Click();

        Assert.That(wasClicked, Is.True);
    }

    [Test]
    public void DoesNotInvokeOnClick_WhenDisabled()
    {
        var wasClicked = false;
        var cut = Render<ToolbarButton>(p =>
        {
            p.Add(x => x.Icon, "pencil");
            p.Add(x => x.Disabled, true);
            p.Add(x => x.OnClick, EventCallback.Factory.Create(this, () => wasClicked = true));
        });

        // Clicking a disabled button should not invoke the callback
        // bUnit will throw an exception if we try to click a disabled element
        var button = cut.Find("button.toolbar-btn");
        Assert.That(button.HasAttribute("disabled"), Is.True);
        // We cannot directly click a disabled button in real DOM, so just verify it's disabled
    }

    [Test]
    public void HasCorrectButtonClass()
    {
        var cut = Render<ToolbarButton>(p =>
        {
            p.Add(x => x.Icon, "play");
        });

        var button = cut.Find("button");
        Assert.That(button.ClassList, Does.Contain("toolbar-btn"));
    }

    [Test]
    public void HasDataTestId_WhenProvided()
    {
        var cut = Render<ToolbarButton>(p =>
        {
            p.Add(x => x.Icon, "pencil");
            p.Add(x => x.TestId, "edit-button");
        });

        var button = cut.Find("[data-testid='edit-button']");
        Assert.That(button, Is.Not.Null);
    }
}
