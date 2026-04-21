using Homespun.Shared.Models.Sessions;
using Microsoft.Extensions.Caching.Memory;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Live implementation of <see cref="IModelCatalogService"/> backed by the
/// official Anthropic C# SDK (via the <see cref="IAnthropicModelSource"/> seam).
/// Successful responses are cached in process memory for 24 hours; failures
/// fall back to <see cref="ClaudeModelInfo.FallbackModels"/> and are not cached.
/// </summary>
public sealed class ModelCatalogService : IModelCatalogService
{
    internal const string CacheKey = "anthropic:models:v1";
    internal static readonly TimeSpan CacheDuration = TimeSpan.FromHours(24);

    private readonly IAnthropicModelSource _source;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ModelCatalogService> _logger;

    internal ModelCatalogService(
        IAnthropicModelSource source,
        IMemoryCache cache,
        ILogger<ModelCatalogService> logger)
    {
        _source = source;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IReadOnlyList<ClaudeModelInfo>> ListAsync(CancellationToken ct)
    {
        if (_cache.TryGetValue(CacheKey, out IReadOnlyList<ClaudeModelInfo>? cached) && cached is not null)
        {
            return cached;
        }

        try
        {
            var raw = await _source.ListAllAsync(ct);
            var catalog = raw
                .Select(m => new ClaudeModelInfo
                {
                    Id = m.Id,
                    DisplayName = m.DisplayName,
                    CreatedAt = m.CreatedAt,
                })
                .ToList();
            var withDefault = ModelCatalogDefaults.ApplyDefaultMarker(catalog);
            _cache.Set(CacheKey, withDefault, CacheDuration);
            return withDefault;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch Anthropic model catalog; returning FallbackModels");
            // Deliberately do not cache the fallback so the next call retries.
            return ModelCatalogDefaults.ApplyDefaultMarker(
                ClaudeModelInfo.FallbackModels
                    .Select(m => new ClaudeModelInfo
                    {
                        Id = m.Id,
                        DisplayName = m.DisplayName,
                        CreatedAt = m.CreatedAt,
                    })
                    .ToList());
        }
    }

    public async Task<string> ResolveModelIdAsync(string? requested, CancellationToken ct)
    {
        var models = await ListAsync(ct);
        return ModelCatalogDefaults.Resolve(requested, models);
    }
}
