using Bunit;
using Homespun.Components.Shared;
using Microsoft.AspNetCore.Components;

namespace Homespun.Tests.Components;

/// <summary>
/// bUnit tests for the EmptyState component.
/// </summary>
[TestFixture]
public class EmptyStateTests : BunitTestContext
{
    [Test]
    public void EmptyState_RendersTitle()
    {
        // Act
        var cut = Render<EmptyState>(parameters =>
            parameters.Add(p => p.Title, "No Items"));

        // Assert
        var title = cut.Find(".empty-state-title");
        Assert.That(title.TextContent, Is.EqualTo("No Items"));
    }

    [Test]
    public void EmptyState_RendersDescription()
    {
        // Act
        var cut = Render<EmptyState>(parameters =>
            parameters.Add(p => p.Description, "Create one to get started."));

        // Assert
        var description = cut.Find(".empty-state-description");
        Assert.That(description.TextContent, Is.EqualTo("Create one to get started."));
    }

    [Test]
    public void EmptyState_RendersIcon()
    {
        // Act
        var cut = Render<EmptyState>(parameters =>
            parameters.Add(p => p.Icon, builder =>
            {
                builder.OpenElement(0, "svg");
                builder.AddAttribute(1, "data-testid", "test-icon");
                builder.CloseElement();
            }));

        // Assert
        var iconContainer = cut.Find(".empty-state-icon");
        var svg = iconContainer.QuerySelector("svg[data-testid='test-icon']");
        Assert.That(svg, Is.Not.Null);
    }

    [Test]
    public void EmptyState_RendersLinkActionButton()
    {
        // Act
        var cut = Render<EmptyState>(parameters =>
            parameters
                .Add(p => p.ActionLabel, "Create New")
                .Add(p => p.ActionHref, "/create"));

        // Assert
        var link = cut.Find(".empty-state-action a");
        Assert.That(link.TextContent, Is.EqualTo("Create New"));
        Assert.That(link.GetAttribute("href"), Is.EqualTo("/create"));
        Assert.That(link.ClassList, Does.Contain("btn"));
        Assert.That(link.ClassList, Does.Contain("btn-primary"));
    }

    [Test]
    public void EmptyState_RendersClickableActionButton()
    {
        // Arrange
        bool wasClicked = false;
        var cut = Render<EmptyState>(parameters =>
            parameters
                .Add(p => p.ActionLabel, "Add Item")
                .Add(p => p.ActionClick, EventCallback.Factory.Create(this, () => wasClicked = true)));

        // Act
        cut.Find(".empty-state-action button").Click();

        // Assert
        Assert.That(wasClicked, Is.True);
    }

    [Test]
    public void EmptyState_RendersCustomActionContent()
    {
        // Act
        var cut = Render<EmptyState>(parameters =>
            parameters.Add(p => p.ActionContent, builder =>
            {
                builder.OpenElement(0, "div");
                builder.AddAttribute(1, "data-testid", "custom-action");
                builder.AddContent(2, "Custom Action");
                builder.CloseElement();
            }));

        // Assert
        var customAction = cut.Find("[data-testid='custom-action']");
        Assert.That(customAction.TextContent, Is.EqualTo("Custom Action"));
    }

    [Test]
    public void EmptyState_CustomActionContentOverridesDefaultButton()
    {
        // Act
        var cut = Render<EmptyState>(parameters =>
            parameters
                .Add(p => p.ActionLabel, "Default Button")
                .Add(p => p.ActionHref, "/default")
                .Add(p => p.ActionContent, builder =>
                {
                    builder.OpenElement(0, "span");
                    builder.AddAttribute(1, "data-testid", "custom");
                    builder.CloseElement();
                }));

        // Assert
        // Should have custom action, not the default link
        var customAction = cut.Find("[data-testid='custom']");
        Assert.That(customAction, Is.Not.Null);
        Assert.Throws<Bunit.ElementNotFoundException>(() => cut.Find(".empty-state-action a"));
    }

    [Test]
    public void EmptyState_DoesNotRenderTitleWhenNull()
    {
        // Act
        var cut = Render<EmptyState>(parameters =>
            parameters.Add(p => p.Description, "Some description"));

        // Assert
        Assert.Throws<Bunit.ElementNotFoundException>(() => cut.Find(".empty-state-title"));
    }

    [Test]
    public void EmptyState_DoesNotRenderDescriptionWhenNull()
    {
        // Act
        var cut = Render<EmptyState>(parameters =>
            parameters.Add(p => p.Title, "Some title"));

        // Assert
        Assert.Throws<Bunit.ElementNotFoundException>(() => cut.Find(".empty-state-description"));
    }

    [Test]
    public void EmptyState_DoesNotRenderIconWhenNull()
    {
        // Act
        var cut = Render<EmptyState>(parameters =>
            parameters.Add(p => p.Title, "Some title"));

        // Assert
        Assert.Throws<Bunit.ElementNotFoundException>(() => cut.Find(".empty-state-icon"));
    }

    [Test]
    public void EmptyState_DoesNotRenderActionWhenNoActionProvided()
    {
        // Act
        var cut = Render<EmptyState>(parameters =>
            parameters.Add(p => p.Title, "Some title"));

        // Assert
        Assert.Throws<Bunit.ElementNotFoundException>(() => cut.Find(".empty-state-action"));
    }

    [Test]
    public void EmptyState_RendersAllComponentsTogether()
    {
        // Act
        var cut = Render<EmptyState>(parameters =>
            parameters
                .Add(p => p.Title, "No Projects")
                .Add(p => p.Description, "Create your first project.")
                .Add(p => p.ActionLabel, "New Project")
                .Add(p => p.ActionHref, "/projects/create")
                .Add(p => p.Icon, builder =>
                {
                    builder.OpenElement(0, "svg");
                    builder.CloseElement();
                }));

        // Assert
        Assert.That(cut.Find(".empty-state"), Is.Not.Null);
        Assert.That(cut.Find(".empty-state-icon"), Is.Not.Null);
        Assert.That(cut.Find(".empty-state-title").TextContent, Is.EqualTo("No Projects"));
        Assert.That(cut.Find(".empty-state-description").TextContent, Is.EqualTo("Create your first project."));
        Assert.That(cut.Find(".empty-state-action a").TextContent, Is.EqualTo("New Project"));
    }

    [Test]
    public void EmptyState_HrefActionTakesPrecedenceOverClickAction()
    {
        // Act
        bool wasClicked = false;
        var cut = Render<EmptyState>(parameters =>
            parameters
                .Add(p => p.ActionLabel, "Action")
                .Add(p => p.ActionHref, "/path")
                .Add(p => p.ActionClick, EventCallback.Factory.Create(this, () => wasClicked = true)));

        // Assert - Should render link, not button
        var link = cut.Find(".empty-state-action a");
        Assert.That(link, Is.Not.Null);
        Assert.Throws<Bunit.ElementNotFoundException>(() => cut.Find(".empty-state-action button"));
    }
}
