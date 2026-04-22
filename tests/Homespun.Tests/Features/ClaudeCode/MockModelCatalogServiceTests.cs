using Homespun.Features.ClaudeCode.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Homespun.Tests.Features.ClaudeCode;

[TestFixture]
public class MockModelCatalogServiceTests
{
    [Test]
    public async Task ListAsync_never_constructs_live_model_source()
    {
        var services = new ServiceCollection();

        // If anything in the mock path resolves IAnthropicModelSource, this
        // factory throws — proving the mock never pulls on the live REST source.
        services.AddSingleton<IAnthropicModelSource>(_ =>
            throw new InvalidOperationException(
                "MockModelCatalogService must not construct IAnthropicModelSource."));

        services.AddSingleton<IModelCatalogService, MockModelCatalogService>();

        await using var sp = services.BuildServiceProvider();
        var catalog = sp.GetRequiredService<IModelCatalogService>();

        var models = await catalog.ListAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(models, Has.Count.EqualTo(ClaudeModelInfo.FallbackModels.Count));
            Assert.That(models.Count(m => m.IsDefault), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ListAsync_returns_fallback_catalog_with_default_marked()
    {
        var svc = new MockModelCatalogService();

        var models = await svc.ListAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(models.Select(m => m.Id),
                Is.EquivalentTo(ClaudeModelInfo.FallbackModels.Select(m => m.Id)));
            var def = models.Single(m => m.IsDefault);
            // Default tier is "opus" first; FallbackModels ids all contain their tier name.
            Assert.That(def.Id, Does.Contain("opus"));
        });
    }

    [Test]
    public async Task ResolveModelIdAsync_resolves_short_alias_against_fallback_catalog()
    {
        var svc = new MockModelCatalogService();

        var sonnetId = await svc.ResolveModelIdAsync("sonnet", CancellationToken.None);

        Assert.That(sonnetId, Does.Contain("sonnet"));
    }
}
