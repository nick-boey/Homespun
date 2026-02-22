namespace Homespun.Features.AgentOrchestration.Services;

/// <summary>
/// Configuration options for the mini prompt service.
/// </summary>
public class MiniPromptOptions
{
    public const string SectionName = "MiniPrompt";

    /// <summary>
    /// URL of the sidecar worker for executing mini prompts.
    /// When null, mini prompts are executed locally using the ClaudeAgentSdk.
    /// </summary>
    public string? SidecarUrl { get; set; }

    /// <summary>
    /// Timeout for mini prompt HTTP requests to the sidecar.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
