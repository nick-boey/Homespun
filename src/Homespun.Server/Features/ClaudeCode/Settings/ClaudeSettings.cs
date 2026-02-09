using System.Text.Json.Serialization;

namespace Homespun.Features.ClaudeCode.Settings;

/// <summary>
/// Represents the .claude/settings.json file structure.
/// </summary>
public class ClaudeSettings
{
    /// <summary>
    /// Hooks configuration keyed by hook type (e.g., "SessionStart", "PreToolUse").
    /// </summary>
    [JsonPropertyName("hooks")]
    public Dictionary<string, List<HookGroup>>? Hooks { get; set; }
}

/// <summary>
/// A group of hooks with an optional matcher pattern.
/// </summary>
public class HookGroup
{
    /// <summary>
    /// Optional regex pattern to match against (e.g., tool names for PreToolUse hooks).
    /// </summary>
    [JsonPropertyName("matcher")]
    public string? Matcher { get; set; }

    /// <summary>
    /// List of hook definitions in this group.
    /// </summary>
    [JsonPropertyName("hooks")]
    public List<HookDefinition> Hooks { get; set; } = [];
}

/// <summary>
/// A single hook definition.
/// </summary>
public class HookDefinition
{
    /// <summary>
    /// The type of hook. Currently only "command" is supported.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "command";

    /// <summary>
    /// The command to execute (for "command" type hooks).
    /// </summary>
    [JsonPropertyName("command")]
    public string? Command { get; set; }

    /// <summary>
    /// Optional timeout in seconds for command execution.
    /// </summary>
    [JsonPropertyName("timeout")]
    public int? Timeout { get; set; }
}

/// <summary>
/// Result of executing a hook command.
/// </summary>
public class HookExecutionResult
{
    /// <summary>
    /// The type of hook that was executed (e.g., "SessionStart").
    /// </summary>
    public required string HookType { get; init; }

    /// <summary>
    /// The command that was executed.
    /// </summary>
    public required string Command { get; init; }

    /// <summary>
    /// Whether the command executed successfully (exit code 0).
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Standard output from the command.
    /// </summary>
    public string? Output { get; init; }

    /// <summary>
    /// Standard error from the command.
    /// </summary>
    public string? Error { get; init; }

    /// <summary>
    /// The exit code of the command.
    /// </summary>
    public int ExitCode { get; init; }

    /// <summary>
    /// How long the command took to execute.
    /// </summary>
    public TimeSpan Duration { get; init; }
}
