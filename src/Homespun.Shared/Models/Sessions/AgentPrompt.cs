namespace Homespun.Shared.Models.Sessions;

/// <summary>
/// Represents a custom agent prompt template that can be used to start sessions.
/// </summary>
public class AgentPrompt
{
    /// <summary>
    /// Unique identifier for the agent prompt.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..6];

    /// <summary>
    /// Display name for the agent prompt (e.g., "Plan", "Build").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The initial message template sent to the agent.
    /// Can contain placeholders like {{title}}, {{description}}, {{branch}}, {{id}}, {{type}}.
    /// </summary>
    public string? InitialMessage { get; set; }

    /// <summary>
    /// The session mode (Plan or Build) which determines available tools.
    /// </summary>
    public SessionMode Mode { get; set; } = SessionMode.Build;

    /// <summary>
    /// Optional project ID. Null means this is a global prompt;
    /// when set, the prompt is scoped to that specific project.
    /// </summary>
    public string? ProjectId { get; set; }

    /// <summary>
    /// When the agent prompt was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the agent prompt was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optional session type that restricts this prompt to specialized session workflows.
    /// When set, the prompt is excluded from standard prompt lists and used only for its
    /// specific session type (e.g., IssueModify prompts are only used by the Issues Agent).
    /// Null means this is a standard prompt available for normal agent sessions.
    /// </summary>
    public SessionType? SessionType { get; set; }
}
