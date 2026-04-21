using Homespun.Features.ClaudeCode.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Homespun.Tests.Features.ClaudeCode;

[TestFixture]
public class ModelCatalogServiceTests
{
    private static readonly DateTimeOffset BaseDate = new(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private Mock<IAnthropicModelSource> _source = null!;
    private MemoryCache _cache = null!;

    [SetUp]
    public void SetUp()
    {
        _source = new Mock<IAnthropicModelSource>();
        _cache = new MemoryCache(new MemoryCacheOptions());
    }

    [TearDown]
    public void TearDown()
    {
        _cache.Dispose();
    }

    private ModelCatalogService CreateService()
        => new(_source.Object, _cache, NullLogger<ModelCatalogService>.Instance);

    private void SetupList(params (string Id, string DisplayName, DateTimeOffset CreatedAt)[] models)
    {
        var infos = models.Select(m => new RawModelInfo(m.Id, m.DisplayName, m.CreatedAt)).ToList();
        _source
            .Setup(s => s.ListAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(infos);
    }

    [Test]
    public async Task ResolveDefault_picks_newest_opus_when_multiple_opus_versions_exist()
    {
        SetupList(
            ("claude-opus-4-6-20250101", "Opus 4.6", BaseDate),
            ("claude-opus-4-7-20251101", "Opus 4.7", BaseDate.AddYears(1)),
            ("claude-sonnet-4-6-20250601", "Sonnet 4.6", BaseDate.AddMonths(6)));

        var svc = CreateService();
        var models = await svc.ListAsync(CancellationToken.None);

        var def = models.Single(m => m.IsDefault);
        Assert.That(def.Id, Is.EqualTo("claude-opus-4-7-20251101"));
    }

    [Test]
    public async Task ResolveDefault_falls_to_sonnet_when_no_opus_present()
    {
        SetupList(
            ("claude-sonnet-4-6-20250101", "Sonnet 4.6", BaseDate),
            ("claude-sonnet-4-7-20251101", "Sonnet 4.7", BaseDate.AddYears(1)),
            ("claude-haiku-4-5-20240101", "Haiku 4.5", BaseDate.AddYears(-1)));

        var svc = CreateService();
        var models = await svc.ListAsync(CancellationToken.None);

        var def = models.Single(m => m.IsDefault);
        Assert.That(def.Id, Is.EqualTo("claude-sonnet-4-7-20251101"));
    }

    [Test]
    public async Task ResolveDefault_falls_to_haiku_when_no_opus_or_sonnet_present()
    {
        SetupList(
            ("claude-haiku-4-5-20240101", "Haiku 4.5", BaseDate.AddYears(-1)),
            ("claude-haiku-4-6-20250101", "Haiku 4.6", BaseDate));

        var svc = CreateService();
        var models = await svc.ListAsync(CancellationToken.None);

        var def = models.Single(m => m.IsDefault);
        Assert.That(def.Id, Is.EqualTo("claude-haiku-4-6-20250101"));
    }

    [Test]
    public async Task ResolveDefault_returns_first_model_when_no_tier_matches()
    {
        SetupList(
            ("custom-model-x", "Custom X", BaseDate),
            ("custom-model-y", "Custom Y", BaseDate.AddDays(1)));

        var svc = CreateService();
        var models = await svc.ListAsync(CancellationToken.None);

        var def = models.Single(m => m.IsDefault);
        Assert.That(def.Id, Is.EqualTo("custom-model-x"));
    }

    [Test]
    public async Task ListAsync_caches_successful_response_for_24h()
    {
        SetupList(("claude-opus-4-7-20251101", "Opus 4.7", BaseDate));

        var svc = CreateService();
        await svc.ListAsync(CancellationToken.None);
        await svc.ListAsync(CancellationToken.None);

        _source.Verify(s => s.ListAllAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task ListAsync_returns_FallbackModels_when_sdk_throws_and_does_not_cache_failure()
    {
        _source
            .Setup(s => s.ListAllAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("transient"));

        var svc = CreateService();
        var first = await svc.ListAsync(CancellationToken.None);
        var second = await svc.ListAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first, Has.Count.EqualTo(ClaudeModelInfo.FallbackModels.Count));
            Assert.That(second, Has.Count.EqualTo(ClaudeModelInfo.FallbackModels.Count));
            Assert.That(first.Count(m => m.IsDefault), Is.EqualTo(1));
        });

        _source.Verify(
            s => s.ListAllAsync(It.IsAny<CancellationToken>()),
            Times.Exactly(2),
            "Failure path must not cache; subsequent calls must retry the SDK.");
    }

    [Test]
    public async Task ResolveModelIdAsync_exact_id_match_returns_input_unchanged()
    {
        SetupList(
            ("claude-opus-4-7-20251101", "Opus 4.7", BaseDate.AddYears(1)),
            ("claude-sonnet-4-6-20250601", "Sonnet 4.6", BaseDate.AddMonths(6)));

        var svc = CreateService();
        var resolved = await svc.ResolveModelIdAsync("claude-sonnet-4-6-20250601", CancellationToken.None);

        Assert.That(resolved, Is.EqualTo("claude-sonnet-4-6-20250601"));
    }

    [Test]
    [TestCase("opus", "claude-opus-4-7-20251101")]
    [TestCase("Sonnet", "claude-sonnet-4-6-20250601")]
    [TestCase("HAIKU", "claude-haiku-4-5-20240101")]
    public async Task ResolveModelIdAsync_short_alias_resolves_to_newest_in_tier(string alias, string expected)
    {
        SetupList(
            ("claude-opus-4-6-20250101", "Opus 4.6", BaseDate),
            ("claude-opus-4-7-20251101", "Opus 4.7", BaseDate.AddYears(1)),
            ("claude-sonnet-4-5-20230101", "Sonnet 4.5", BaseDate.AddYears(-2)),
            ("claude-sonnet-4-6-20250601", "Sonnet 4.6", BaseDate.AddMonths(6)),
            ("claude-haiku-4-5-20240101", "Haiku 4.5", BaseDate.AddYears(-1)));

        var svc = CreateService();
        var resolved = await svc.ResolveModelIdAsync(alias, CancellationToken.None);

        Assert.That(resolved, Is.EqualTo(expected));
    }

    [Test]
    public async Task ResolveModelIdAsync_null_returns_current_default()
    {
        SetupList(
            ("claude-opus-4-7-20251101", "Opus 4.7", BaseDate.AddYears(1)),
            ("claude-sonnet-4-6-20250601", "Sonnet 4.6", BaseDate.AddMonths(6)));

        var svc = CreateService();
        var resolved = await svc.ResolveModelIdAsync(null, CancellationToken.None);

        Assert.That(resolved, Is.EqualTo("claude-opus-4-7-20251101"));
    }

    [Test]
    public async Task ResolveModelIdAsync_unknown_value_passes_through_unchanged()
    {
        SetupList(
            ("claude-opus-4-7-20251101", "Opus 4.7", BaseDate.AddYears(1)));

        var svc = CreateService();
        var resolved = await svc.ResolveModelIdAsync("claude-something-retired-19990101", CancellationToken.None);

        Assert.That(resolved, Is.EqualTo("claude-something-retired-19990101"));
    }
}
