namespace Homespun.Shared.Models.Sessions;

/// <summary>
/// Context information for rendering prompt templates.
/// </summary>
public class PromptContext
{
    /// <summary>
    /// The title of the issue or PR.
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// The unique identifier (issue ID or PR number).
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// The description text.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The branch name.
    /// </summary>
    public string Branch { get; set; } = string.Empty;

    /// <summary>
    /// The type (e.g., Feature, Bug, Task for issues; or "PullRequest" for PRs).
    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// The issue's hierarchical context showing ancestors and direct children.
    /// Format: tree structure with each line showing "- id [type] [status] title"
    /// </summary>
    public string? Context { get; set; }

    /// <summary>
    /// The selected issue ID for issue agent sessions.
    /// Used with the {{selectedIssueId}} placeholder.
    /// </summary>
    public string? SelectedIssueId { get; set; }

    /// <summary>
    /// User-provided instructions/prompt for the agent.
    /// Used with the {{userPrompt}} placeholder.
    /// </summary>
    public string? UserPrompt { get; set; }
}
