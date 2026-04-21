using Homespun.Shared.Models.Sessions;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Shared helpers for picking the default model and resolving legacy short-alias
/// identifiers against a catalog. Used by both the live and mock catalog services
/// so the two implementations agree on the selection rule.
/// </summary>
internal static class ModelCatalogDefaults
{
    internal static readonly string[] TierPreference = ["opus", "sonnet", "haiku"];

    /// <summary>
    /// Returns a copy of the input list with <c>IsDefault = true</c> on the
    /// single entry chosen by the preference-ordered newest-in-tier rule; if
    /// no tier matches, the first entry is marked.
    /// </summary>
    public static IReadOnlyList<ClaudeModelInfo> ApplyDefaultMarker(IReadOnlyList<ClaudeModelInfo> models)
    {
        if (models.Count == 0)
        {
            return models;
        }

        var defaultModel = SelectDefault(models);

        return models.Select(m => new ClaudeModelInfo
        {
            Id = m.Id,
            DisplayName = m.DisplayName,
            CreatedAt = m.CreatedAt,
            IsDefault = ReferenceEquals(m, defaultModel),
        }).ToList();
    }

    /// <summary>
    /// Picks the default model using the preference-ordered newest-in-tier rule.
    /// Falls back to the first entry if no tier matches.
    /// </summary>
    public static ClaudeModelInfo SelectDefault(IReadOnlyList<ClaudeModelInfo> models)
    {
        foreach (var tier in TierPreference)
        {
            var match = models
                .Where(m => m.Id.Contains(tier, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(m => m.CreatedAt)
                .FirstOrDefault();
            if (match is not null)
            {
                return match;
            }
        }

        return models[0];
    }

    /// <summary>
    /// Resolves a possibly-legacy model identifier against the given catalog.
    /// </summary>
    public static string Resolve(string? requested, IReadOnlyList<ClaudeModelInfo> models)
    {
        if (string.IsNullOrEmpty(requested))
        {
            var def = models.FirstOrDefault(m => m.IsDefault);
            return def?.Id ?? (models.Count > 0 ? models[0].Id : string.Empty);
        }

        if (models.Any(m => string.Equals(m.Id, requested, StringComparison.Ordinal)))
        {
            return requested;
        }

        foreach (var tier in TierPreference)
        {
            if (requested.Equals(tier, StringComparison.OrdinalIgnoreCase))
            {
                var match = models
                    .Where(m => m.Id.Contains(tier, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(m => m.CreatedAt)
                    .FirstOrDefault();
                if (match is not null)
                {
                    return match.Id;
                }
            }
        }

        return requested;
    }
}
