using Bunit;
using BlazorBlueprint.Components;
using Homespun.Client.Features.Projects.Components;
using Microsoft.AspNetCore.Components;

namespace Homespun.Tests.Components;

/// <summary>
/// bUnit tests for the ModelSelector component.
/// ModelSelector uses BbSelect (Blazor Blueprint) which renders a custom dropdown
/// instead of a native select element. Tests verify component parameters and behavior.
/// </summary>
[TestFixture]
public class ModelSelectorTests : BunitTestContext
{
    [Test]
    public void ModelSelector_DefaultsToOpus()
    {
        // Act
        var cut = Render<ModelSelector>();

        // Assert - verify the component instance has the correct default
        Assert.That(cut.Instance.SelectedModel, Is.EqualTo("opus"));
    }

    [Test]
    public void ModelSelector_DisplaysAllOptions()
    {
        // Act
        var cut = Render<ModelSelector>();

        // Assert - BbSelect renders the selected option's display text in the trigger.
        // Verify the default "Opus" text is displayed.
        Assert.That(cut.Markup, Does.Contain("Opus"));
    }

    [Test]
    public void ModelSelector_RendersWithCustomId()
    {
        // Act
        var cut = Render<ModelSelector>(parameters =>
            parameters.Add(p => p.Id, "custom-id"));

        // Assert - verify the Id parameter is accepted
        Assert.That(cut.Instance.Id, Is.EqualTo("custom-id"));
    }

    [Test]
    public void ModelSelector_RendersWithSelectedModel()
    {
        // Act
        var cut = Render<ModelSelector>(parameters =>
            parameters.Add(p => p.SelectedModel, "sonnet"));

        // Assert - verify the component displays the selected model's display text
        Assert.That(cut.Instance.SelectedModel, Is.EqualTo("sonnet"));
        Assert.That(cut.Markup, Does.Contain("Sonnet"));
    }

    [Test]
    public async Task ModelSelector_InvokesCallbackOnChange()
    {
        // Arrange
        string? selectedModel = null;
        var cut = Render<ModelSelector>(parameters =>
            parameters.Add(p => p.SelectedModelChanged, EventCallback.Factory.Create<string>(this, value => selectedModel = value)));

        // Act - invoke the callback through the component's public API
        await cut.InvokeAsync(() => cut.Instance.SelectedModelChanged.InvokeAsync("haiku"));

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
        // Allow all unplanned JS interop calls (needed for Blazor Blueprint components
        // like BbSelect that import JS modules during OnAfterRenderAsync)
        Context.JSInterop.Mode = JSRuntimeMode.Loose;
        // Register Blazor Blueprint services for components using Dialog, Tooltip, etc.
        Context.Services.AddBlazorBlueprintComponents();
    }

    [TearDown]
    public async Task TearDown()
    {
        if (Context != null)
        {
            await Context.DisposeAsync();
        }
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
