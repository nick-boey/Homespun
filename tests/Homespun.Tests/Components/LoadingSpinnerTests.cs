using Bunit;
using Homespun.Components.Shared;

namespace Homespun.Tests.Components;

/// <summary>
/// bUnit tests for the LoadingSpinner component.
/// </summary>
[TestFixture]
public class LoadingSpinnerTests : BunitTestContext
{
    [Test]
    public void LoadingSpinner_DefaultsToMediumSize()
    {
        // Act
        var cut = Render<LoadingSpinner>();

        // Assert
        var spinner = cut.Find(".spinner-border");
        Assert.That(spinner.ClassList, Does.Not.Contain("spinner-border-sm"));
        Assert.That(spinner.ClassList, Does.Not.Contain("spinner-border-lg"));
    }

    [Test]
    public void LoadingSpinner_RendersSmallSize()
    {
        // Act
        var cut = Render<LoadingSpinner>(parameters =>
            parameters.Add(p => p.Size, SpinnerSize.Sm));

        // Assert
        var spinner = cut.Find(".spinner-border");
        Assert.That(spinner.ClassList, Does.Contain("spinner-border-sm"));
    }

    [Test]
    public void LoadingSpinner_RendersLargeSize()
    {
        // Act
        var cut = Render<LoadingSpinner>(parameters =>
            parameters.Add(p => p.Size, SpinnerSize.Lg));

        // Assert
        var spinner = cut.Find(".spinner-border");
        Assert.That(spinner.ClassList, Does.Contain("spinner-border-lg"));
    }

    [Test]
    public void LoadingSpinner_DisplaysLabel_WhenProvided()
    {
        // Act
        var cut = Render<LoadingSpinner>(parameters =>
            parameters.Add(p => p.Label, "Loading..."));

        // Assert
        var label = cut.Find(".loading-spinner-label");
        Assert.That(label.TextContent, Is.EqualTo("Loading..."));
    }

    [Test]
    public void LoadingSpinner_HidesLabel_WhenNotProvided()
    {
        // Act
        var cut = Render<LoadingSpinner>();

        // Assert
        var labels = cut.FindAll(".loading-spinner-label");
        Assert.That(labels, Has.Count.EqualTo(0));
    }

    [Test]
    public void LoadingSpinner_RendersScreenReaderText_WhenProvided()
    {
        // Act
        var cut = Render<LoadingSpinner>(parameters =>
            parameters.Add(p => p.ScreenReaderText, "Checking authentication"));

        // Assert
        var srOnly = cut.Find(".sr-only");
        Assert.That(srOnly.TextContent, Is.EqualTo("Checking authentication"));
    }

    [Test]
    public void LoadingSpinner_HasRoleStatus_ForAccessibility()
    {
        // Act
        var cut = Render<LoadingSpinner>();

        // Assert
        var container = cut.Find(".loading-spinner");
        Assert.That(container.GetAttribute("role"), Is.EqualTo("status"));
    }

    [Test]
    public void LoadingSpinner_UsesInlineLayout_WhenInlineIsTrue()
    {
        // Act
        var cut = Render<LoadingSpinner>(parameters =>
            parameters.Add(p => p.Inline, true));

        // Assert
        var container = cut.Find(".loading-spinner");
        Assert.That(container.ClassList, Does.Contain("loading-spinner-inline"));
        Assert.That(container.ClassList, Does.Not.Contain("loading-spinner-block"));
    }

    [Test]
    public void LoadingSpinner_UsesBlockLayout_WhenInlineIsFalse()
    {
        // Act
        var cut = Render<LoadingSpinner>(parameters =>
            parameters.Add(p => p.Inline, false));

        // Assert
        var container = cut.Find(".loading-spinner");
        Assert.That(container.ClassList, Does.Contain("loading-spinner-block"));
        Assert.That(container.ClassList, Does.Not.Contain("loading-spinner-inline"));
    }

    [Test]
    public void LoadingSpinner_CentersContent_WhenCenteredIsTrue()
    {
        // Act
        var cut = Render<LoadingSpinner>(parameters =>
            parameters.Add(p => p.Centered, true));

        // Assert
        var container = cut.Find(".loading-spinner");
        Assert.That(container.ClassList, Does.Contain("loading-spinner-centered"));
    }

    [Test]
    public void LoadingSpinner_DoesNotCenter_WhenCenteredIsFalse()
    {
        // Act
        var cut = Render<LoadingSpinner>(parameters =>
            parameters.Add(p => p.Centered, false));

        // Assert
        var container = cut.Find(".loading-spinner");
        Assert.That(container.ClassList, Does.Not.Contain("loading-spinner-centered"));
    }

    [Test]
    public void LoadingSpinner_AppliesCustomClass_WhenProvided()
    {
        // Act
        var cut = Render<LoadingSpinner>(parameters =>
            parameters.Add(p => p.Class, "custom-class"));

        // Assert
        var container = cut.Find(".loading-spinner");
        Assert.That(container.ClassList, Does.Contain("custom-class"));
    }

    [Test]
    public void LoadingSpinner_SmallSize_HasSmallLabelClass()
    {
        // Act
        var cut = Render<LoadingSpinner>(parameters =>
        {
            parameters.Add(p => p.Size, SpinnerSize.Sm);
            parameters.Add(p => p.Label, "Loading...");
        });

        // Assert
        var label = cut.Find(".loading-spinner-label");
        Assert.That(label.ClassList, Does.Contain("loading-spinner-label-sm"));
    }

    [Test]
    public void LoadingSpinner_LargeSize_HasLargeLabelClass()
    {
        // Act
        var cut = Render<LoadingSpinner>(parameters =>
        {
            parameters.Add(p => p.Size, SpinnerSize.Lg);
            parameters.Add(p => p.Label, "Loading...");
        });

        // Assert
        var label = cut.Find(".loading-spinner-label");
        Assert.That(label.ClassList, Does.Contain("loading-spinner-label-lg"));
    }

    [Test]
    public void LoadingSpinner_CombinesMultipleParameters()
    {
        // Act
        var cut = Render<LoadingSpinner>(parameters =>
        {
            parameters.Add(p => p.Size, SpinnerSize.Sm);
            parameters.Add(p => p.Label, "Saving...");
            parameters.Add(p => p.Inline, true);
            parameters.Add(p => p.Class, "me-2");
            parameters.Add(p => p.ScreenReaderText, "Saving document");
        });

        // Assert
        var container = cut.Find(".loading-spinner");
        Assert.That(container.ClassList, Does.Contain("loading-spinner-inline"));
        Assert.That(container.ClassList, Does.Contain("me-2"));

        var spinner = cut.Find(".spinner-border");
        Assert.That(spinner.ClassList, Does.Contain("spinner-border-sm"));

        var label = cut.Find(".loading-spinner-label");
        Assert.That(label.TextContent, Is.EqualTo("Saving..."));

        var srOnly = cut.Find(".sr-only");
        Assert.That(srOnly.TextContent, Is.EqualTo("Saving document"));
    }
}
