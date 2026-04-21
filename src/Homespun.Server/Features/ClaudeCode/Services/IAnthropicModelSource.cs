namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Internal seam over the Anthropic SDK's models-list endpoint. Exists so that
/// <see cref="ModelCatalogService"/> can be unit-tested without constructing
/// the SDK's sealed response types (which have many required members).
/// </summary>
/// <remarks>
/// Production DI binds <see cref="AnthropicModelSource"/>, which wraps
/// <c>IAnthropicClient</c>. Mock mode never registers this interface because
/// <see cref="MockModelCatalogService"/> doesn't depend on it.
/// </remarks>
internal interface IAnthropicModelSource
{
    Task<IReadOnlyList<RawModelInfo>> ListAllAsync(CancellationToken ct);
}

/// <summary>
/// Minimal projection of the fields we consume from <c>Anthropic.Models.Models.ModelInfo</c>.
/// </summary>
internal sealed record RawModelInfo(string Id, string DisplayName, DateTimeOffset CreatedAt);
