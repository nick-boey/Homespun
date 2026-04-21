using Homespun.Shared.Models.Sessions;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Catalog of Claude models available for agent sessions. Single source of
/// truth for the `GET /api/models` endpoint and for server-side resolution of
/// legacy short-alias model values.
/// </summary>
public interface IModelCatalogService
{
    /// <summary>
    /// Returns the current catalog with <c>IsDefault</c> precomputed on exactly
    /// one entry via the preference-ordered newest-in-tier rule.
    /// </summary>
    Task<IReadOnlyList<ClaudeModelInfo>> ListAsync(CancellationToken ct);

    /// <summary>
    /// Resolves a possibly-legacy model identifier against the current catalog.
    /// <list type="bullet">
    /// <item>null or empty → the current default id</item>
    /// <item>exact id match → input unchanged</item>
    /// <item>short-alias (<c>opus</c>, <c>sonnet</c>, <c>haiku</c>) → newest id in tier</item>
    /// <item>unknown value → input unchanged (defers error to the SDK)</item>
    /// </list>
    /// </summary>
    Task<string> ResolveModelIdAsync(string? requested, CancellationToken ct);
}
