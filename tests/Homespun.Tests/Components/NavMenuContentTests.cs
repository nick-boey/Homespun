using Bunit;
using Homespun.Client.Features.Navigation.Components;
using Homespun.Client.Features.Navigation.Models;
using Homespun.Shared.Models.Projects;

namespace Homespun.Tests.Components;

/// <summary>
/// bUnit tests for the NavMenuContent component.
/// NavMenuContent renders navigation items for both sidebar and responsive nav contexts.
/// </summary>
[TestFixture]
public class NavMenuContentTests : BunitTestContext
{
    [Test]
    public void ResponsiveMode_RendersProjectsGroupLabel()
    {
        // Arrange & Act
        var cut = Render<NavMenuContent>(p =>
        {
            p.Add(x => x.Projects, new List<Project>());
            p.Add(x => x.RenderMode, NavRenderMode.Responsive);
        });

        // Assert - should have a Projects group label
        Assert.That(cut.Markup, Does.Contain("Projects"));
    }

    [Test]
    public void ResponsiveMode_RendersAllProjectsLink()
    {
        // Arrange & Act
        var cut = Render<NavMenuContent>(p =>
        {
            p.Add(x => x.Projects, new List<Project>());
            p.Add(x => x.RenderMode, NavRenderMode.Responsive);
        });

        // Assert - should have an "All" link to /projects
        var allLink = cut.Find("a[href='projects']");
        Assert.That(allLink, Is.Not.Null);
        Assert.That(allLink.TextContent, Does.Contain("All"));
    }

    [Test]
    public void ResponsiveMode_RendersDynamicProjectLinks()
    {
        // Arrange
        var projects = new List<Project>
        {
            new() { Name = "Project Alpha", LocalPath = "/path", DefaultBranch = "main" },
            new() { Name = "Project Beta", LocalPath = "/path2", DefaultBranch = "main" }
        };

        // Act
        var cut = Render<NavMenuContent>(p =>
        {
            p.Add(x => x.Projects, projects);
            p.Add(x => x.RenderMode, NavRenderMode.Responsive);
        });

        // Assert - should have links for each project
        Assert.That(cut.Markup, Does.Contain("Project Alpha"));
        Assert.That(cut.Markup, Does.Contain("Project Beta"));

        var projectLinks = cut.FindAll("a[href^='projects/']");
        Assert.That(projectLinks, Has.Count.EqualTo(2));
    }

    [Test]
    public void ResponsiveMode_RendersAgentsLink()
    {
        // Arrange & Act
        var cut = Render<NavMenuContent>(p =>
        {
            p.Add(x => x.Projects, new List<Project>());
            p.Add(x => x.RenderMode, NavRenderMode.Responsive);
        });

        // Assert - should have Agents link
        var agentsLink = cut.Find("a[href='agents']");
        Assert.That(agentsLink, Is.Not.Null);
        Assert.That(agentsLink.TextContent, Does.Contain("Agents"));
    }

    [Test]
    public void ResponsiveMode_RendersPromptsLink()
    {
        // Arrange & Act
        var cut = Render<NavMenuContent>(p =>
        {
            p.Add(x => x.Projects, new List<Project>());
            p.Add(x => x.RenderMode, NavRenderMode.Responsive);
        });

        // Assert - should have Prompts link
        var promptsLink = cut.Find("a[href='prompts']");
        Assert.That(promptsLink, Is.Not.Null);
        Assert.That(promptsLink.TextContent, Does.Contain("Prompts"));
    }

    [Test]
    public void ResponsiveMode_RendersSettingsLink()
    {
        // Arrange & Act
        var cut = Render<NavMenuContent>(p =>
        {
            p.Add(x => x.Projects, new List<Project>());
            p.Add(x => x.RenderMode, NavRenderMode.Responsive);
        });

        // Assert - should have Settings link
        var settingsLink = cut.Find("a[href='settings']");
        Assert.That(settingsLink, Is.Not.Null);
        Assert.That(settingsLink.TextContent, Does.Contain("Settings"));
    }

    [Test]
    public void ResponsiveMode_RendersIconsWithLinks()
    {
        // Arrange & Act
        var cut = Render<NavMenuContent>(p =>
        {
            p.Add(x => x.Projects, new List<Project>());
            p.Add(x => x.RenderMode, NavRenderMode.Responsive);
        });

        // Assert - icons should be present (LucideIcon renders SVG elements)
        var allLink = cut.Find("a[href='projects']");
        var agentsLink = cut.Find("a[href='agents']");
        var promptsLink = cut.Find("a[href='prompts']");
        var settingsLink = cut.Find("a[href='settings']");

        Assert.That(allLink.QuerySelector("svg"), Is.Not.Null, "All projects link should have an icon");
        Assert.That(agentsLink.QuerySelector("svg"), Is.Not.Null, "Agents link should have an icon");
        Assert.That(promptsLink.QuerySelector("svg"), Is.Not.Null, "Prompts link should have an icon");
        Assert.That(settingsLink.QuerySelector("svg"), Is.Not.Null, "Settings link should have an icon");
    }

    [Test]
    public void SidebarMode_RendersBbSidebarComponents()
    {
        // Arrange & Act
        var cut = Render<NavMenuContent>(p =>
        {
            p.Add(x => x.Projects, new List<Project>());
            p.Add(x => x.RenderMode, NavRenderMode.Sidebar);
        });

        // Assert - should contain BbSidebar component markup
        // BbSidebarMenu renders a <ul> element
        var menuList = cut.Find("ul");
        Assert.That(menuList, Is.Not.Null);
    }

    [Test]
    public void SidebarMode_RendersProjectsGroup()
    {
        // Arrange & Act
        var cut = Render<NavMenuContent>(p =>
        {
            p.Add(x => x.Projects, new List<Project>());
            p.Add(x => x.RenderMode, NavRenderMode.Sidebar);
        });

        // Assert - should have Projects group label
        Assert.That(cut.Markup, Does.Contain("Projects"));
    }

    [Test]
    public void SidebarMode_RendersDynamicProjectLinks()
    {
        // Arrange
        var projects = new List<Project>
        {
            new() { Name = "Test Project", LocalPath = "/path", DefaultBranch = "main" }
        };

        // Act
        var cut = Render<NavMenuContent>(p =>
        {
            p.Add(x => x.Projects, projects);
            p.Add(x => x.RenderMode, NavRenderMode.Sidebar);
        });

        // Assert - should have link for the project
        Assert.That(cut.Markup, Does.Contain("Test Project"));
    }

    [Test]
    public void SidebarMode_RendersAllNavigationItems()
    {
        // Arrange & Act
        var cut = Render<NavMenuContent>(p =>
        {
            p.Add(x => x.Projects, new List<Project>());
            p.Add(x => x.RenderMode, NavRenderMode.Sidebar);
        });

        // Assert - should have all navigation items
        Assert.That(cut.Markup, Does.Contain("All"));
        Assert.That(cut.Markup, Does.Contain("Agents"));
        Assert.That(cut.Markup, Does.Contain("Prompts"));
        Assert.That(cut.Markup, Does.Contain("Settings"));
    }
}
