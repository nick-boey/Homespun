namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// POCO for deserializing default prompt definitions from JSON resource file.
/// </summary>
public class DefaultPromptDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Mode { get; set; } = "build";
    public string? InitialMessage { get; set; }
    public string? SessionType { get; set; }
    public string? Category { get; set; }
}
