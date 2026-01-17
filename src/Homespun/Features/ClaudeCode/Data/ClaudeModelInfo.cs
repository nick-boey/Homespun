namespace Homespun.Features.ClaudeCode.Data;

/// <summary>
/// Information about a Claude model available for use.
/// </summary>
public class ClaudeModelInfo
{
    /// <summary>
    /// The model ID (e.g., "claude-sonnet-4-20250514").
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Display name for the model.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Provider ID (always "anthropic" for Claude models).
    /// </summary>
    public string ProviderId => "anthropic";

    /// <summary>
    /// Full ID including provider (e.g., "anthropic/claude-sonnet-4-20250514").
    /// </summary>
    public string FullId => $"{ProviderId}/{Id}";

    /// <summary>
    /// Whether this model supports extended thinking.
    /// </summary>
    public bool SupportsThinking { get; init; }

    /// <summary>
    /// Whether this model supports tool use.
    /// </summary>
    public bool SupportsToolUse { get; init; } = true;

    /// <summary>
    /// Whether this model supports vision/images.
    /// </summary>
    public bool SupportsVision { get; init; } = true;

    /// <summary>
    /// Pre-defined list of available Claude models.
    /// </summary>
    public static readonly IReadOnlyList<ClaudeModelInfo> AvailableModels =
    [
        new ClaudeModelInfo
        {
            Id = "claude-opus-4-20250514",
            Name = "Claude Opus 4",
            SupportsThinking = true
        },
        new ClaudeModelInfo
        {
            Id = "claude-sonnet-4-20250514",
            Name = "Claude Sonnet 4",
            SupportsThinking = true
        },
        new ClaudeModelInfo
        {
            Id = "claude-3-7-sonnet-20250219",
            Name = "Claude 3.7 Sonnet",
            SupportsThinking = true
        },
        new ClaudeModelInfo
        {
            Id = "claude-3-5-sonnet-20241022",
            Name = "Claude 3.5 Sonnet",
            SupportsThinking = false
        },
        new ClaudeModelInfo
        {
            Id = "claude-3-5-haiku-20241022",
            Name = "Claude 3.5 Haiku",
            SupportsThinking = false
        }
    ];
}
