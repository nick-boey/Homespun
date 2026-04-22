namespace Homespun.Shared.Models.Sessions;

/// <summary>
/// Information about a Claude model available for use.
/// </summary>
public class ClaudeModelInfo
{
    /// <summary>
    /// The full model id returned by the Anthropic API (e.g., "claude-opus-4-7-20251101").
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Display name for the model.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// When the model was published.
    /// </summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Whether this model is the current default selection. Exactly one entry
    /// in a catalog response should carry <c>IsDefault = true</c>; the value is
    /// computed by the server via the preference-ordered newest-in-tier rule.
    /// </summary>
    public bool IsDefault { get; init; }

    /// <summary>
    /// Fallback catalog used when the Anthropic API is unavailable and in mock mode.
    /// One entry per short-alias tier; the ids are the tier aliases themselves
    /// (<c>opus</c>/<c>sonnet</c>/<c>haiku</c>) which the Claude Agent SDK accepts
    /// directly, avoiding drift against a hardcoded dated snapshot.
    /// </summary>
    public static readonly IReadOnlyList<ClaudeModelInfo> FallbackModels =
    [
        new ClaudeModelInfo
        {
            Id = "opus",
            DisplayName = "Opus",
            CreatedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
        },
        new ClaudeModelInfo
        {
            Id = "sonnet",
            DisplayName = "Sonnet",
            CreatedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
        },
        new ClaudeModelInfo
        {
            Id = "haiku",
            DisplayName = "Haiku",
            CreatedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
        }
    ];
}
