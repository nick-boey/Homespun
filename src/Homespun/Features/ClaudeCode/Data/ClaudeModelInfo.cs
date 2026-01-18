namespace Homespun.Features.ClaudeCode.Data;

/// <summary>
/// Information about a Claude model available for use.
/// </summary>
public class ClaudeModelInfo
{
    /// <summary>
    /// The model ID (e.g., "sonnet", "opus", "haiku").
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Display name for the model.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Full ID for the model (same as Id for simple names like "sonnet").
    /// </summary>
    public string FullId => Id;

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
    /// Uses simple names that resolve to the latest version.
    /// </summary>
    public static readonly IReadOnlyList<ClaudeModelInfo> AvailableModels =
    [
        new ClaudeModelInfo
        {
            Id = "opus",
            Name = "Claude Opus",
            SupportsThinking = true
        },
        new ClaudeModelInfo
        {
            Id = "sonnet",
            Name = "Claude Sonnet",
            SupportsThinking = true
        },
        new ClaudeModelInfo
        {
            Id = "haiku",
            Name = "Claude Haiku",
            SupportsThinking = false
        }
    ];
}
