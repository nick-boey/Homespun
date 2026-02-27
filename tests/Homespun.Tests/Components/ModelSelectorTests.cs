using Bunit;
using BlazorBlueprint.Components;
using Homespun.Client.Components;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Homespun.Tests.Components;

/// <summary>
/// bUnit tests for the ModelSelector component.
/// </summary>
[TestFixture]
public class ModelSelectorTests : BunitTestContext
{
    [Test]
    public void ModelSelector_DefaultsToOpus()
    {
        // Act
        var cut = Render<ModelSelector>();

        // Assert
        var select = cut.Find("select");
        Assert.That(select.GetAttribute("value"), Is.EqualTo("opus"));
    }

    [Test]
    public void ModelSelector_DisplaysAllOptions()
    {
        // Act
        var cut = Render<ModelSelector>();

        // Assert
        var options = cut.FindAll("option");
        Assert.That(options, Has.Count.EqualTo(3));
        Assert.That(options[0].TextContent, Is.EqualTo("Opus"));
        Assert.That(options[1].TextContent, Is.EqualTo("Sonnet"));
        Assert.That(options[2].TextContent, Is.EqualTo("Haiku"));
    }

    [Test]
    public void ModelSelector_RendersWithCustomId()
    {
        // Act
        var cut = Render<ModelSelector>(parameters =>
            parameters.Add(p => p.Id, "custom-id"));

        // Assert
        var select = cut.Find("select");
        Assert.That(select.Id, Is.EqualTo("model-selector-custom-id"));
    }

    [Test]
    public void ModelSelector_RendersWithSelectedModel()
    {
        // Act
        var cut = Render<ModelSelector>(parameters =>
            parameters.Add(p => p.SelectedModel, "sonnet"));

        // Assert
        var select = cut.Find("select");
        Assert.That(select.GetAttribute("value"), Is.EqualTo("sonnet"));
    }

    [Test]
    public void ModelSelector_InvokesCallbackOnChange()
    {
        // Arrange
        string? selectedModel = null;
        var cut = Render<ModelSelector>(parameters =>
            parameters.Add(p => p.SelectedModelChanged, EventCallback.Factory.Create<string>(this, value => selectedModel = value)));

        // Act
        cut.Find("select").Change("haiku");

        // Assert
        Assert.That(selectedModel, Is.EqualTo("haiku"));
    }
}

/// <summary>
/// Base test context for bUnit tests.
/// Registers Blazor Blueprint services for components using BbDialog, BbTooltip, etc.
/// </summary>
public class BunitTestContext : TestContextWrapper
{
    [SetUp]
    public void Setup()
    {
        Context = new Bunit.BunitContext();
        // Register Blazor Blueprint services for components using Dialog, Tooltip, etc.
        Context.Services.AddBlazorBlueprintComponents();
    }

    [TearDown]
    public void TearDown()
    {
        Context?.Dispose();
    }
}

/// <summary>
/// Wrapper for bUnit BunitContext to work with NUnit.
/// </summary>
public abstract class TestContextWrapper
{
    protected Bunit.BunitContext? Context { get; set; }

    protected IRenderedComponent<TComponent> Render<TComponent>(Action<ComponentParameterCollectionBuilder<TComponent>>? parameterBuilder = null)
        where TComponent : Microsoft.AspNetCore.Components.IComponent
    {
        if (Context == null)
            throw new InvalidOperationException("Context not initialized");

        return Context.Render(parameterBuilder);
    }

    protected Microsoft.Extensions.DependencyInjection.IServiceCollection Services => Context!.Services;

    protected BunitJSInterop JSInterop => Context!.JSInterop;
}
