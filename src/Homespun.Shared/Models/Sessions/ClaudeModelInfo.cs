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
    /// One entry per short-alias tier with plausible full ids and fixed past
    /// <c>CreatedAt</c> timestamps so ordering is deterministic in tests.
    /// </summary>
    public static readonly IReadOnlyList<ClaudeModelInfo> FallbackModels =
    [
        new ClaudeModelInfo
        {
            Id = "claude-opus-4-6-20250101",
            DisplayName = "Claude Opus 4.6",
            CreatedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
        },
        new ClaudeModelInfo
        {
            Id = "claude-sonnet-4-6-20250101",
            DisplayName = "Claude Sonnet 4.6",
            CreatedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
        },
        new ClaudeModelInfo
        {
            Id = "claude-haiku-4-5-20250101",
            DisplayName = "Claude Haiku 4.5",
            CreatedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
        }
    ];
}
