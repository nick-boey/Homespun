namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Internal seam over the Anthropic <c>GET /v1/models</c> REST endpoint.
/// Exists so that <see cref="ModelCatalogService"/> can be unit-tested without
/// hitting the network.
/// </summary>
/// <remarks>
/// Production DI binds <see cref="AnthropicModelSource"/>, which calls the REST
/// endpoint directly via <see cref="HttpClient"/>. The SDK's models-list path
/// is deliberately avoided because it rejects Claude Code OAuth tokens with
/// <c>"OAuth authentication is currently not supported"</c>. Mock mode
/// (<c>UseLiveClaudeSessions=false</c>) does not register this interface
/// because <see cref="MockModelCatalogService"/> doesn't depend on it.
/// </remarks>
internal interface IAnthropicModelSource
{
    Task<IReadOnlyList<RawModelInfo>> ListAllAsync(CancellationToken ct);
}

/// <summary>
/// Minimal projection of the fields consumed from the Anthropic
/// <c>GET /v1/models</c> response (<c>id</c>, <c>display_name</c>, <c>created_at</c>).
/// </summary>
internal sealed record RawModelInfo(string Id, string DisplayName, DateTimeOffset CreatedAt);
