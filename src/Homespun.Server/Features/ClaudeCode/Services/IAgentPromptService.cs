using Homespun.Shared.Models.Sessions;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Service for managing custom agent prompts.
/// </summary>
public interface IAgentPromptService
{
    /// <summary>
    /// Gets all global agent prompts (those with no ProjectId).
    /// </summary>
    IReadOnlyList<AgentPrompt> GetAllPrompts();

    /// <summary>
    /// Gets agent prompts scoped to a specific project (not including global prompts).
    /// </summary>
    IReadOnlyList<AgentPrompt> GetProjectPrompts(string projectId);

    /// <summary>
    /// Gets prompts available in a project context: project-specific prompts, plus global prompts
    /// that are not overridden by project prompts (matched by name, case-insensitive).
    /// </summary>
    IReadOnlyList<AgentPrompt> GetPromptsForProject(string projectId);

    /// <summary>
    /// Gets global prompts that are not overridden by project-specific prompts.
    /// Useful for showing which global prompts can still be "copied" to project scope.
    /// </summary>
    IReadOnlyList<AgentPrompt> GetGlobalPromptsNotOverridden(string projectId);

    /// <summary>
    /// Gets an agent prompt by name and project scope (composite key).
    /// </summary>
    AgentPrompt? GetPrompt(string name, string? projectId);

    /// <summary>
    /// Creates a new global agent prompt.
    /// </summary>
    Task<AgentPrompt> CreatePromptAsync(string name, string? initialMessage, SessionMode mode);

    /// <summary>
    /// Creates a new agent prompt, optionally scoped to a project.
    /// </summary>
    Task<AgentPrompt> CreatePromptAsync(string name, string? initialMessage, SessionMode mode, string? projectId,
        PromptCategory category = PromptCategory.Standard);

    /// <summary>
    /// Updates an existing agent prompt identified by name and project scope.
    /// </summary>
    Task<AgentPrompt> UpdatePromptAsync(string name, string? projectId, string? initialMessage, SessionMode mode);

    /// <summary>
    /// Deletes an agent prompt identified by name and project scope.
    /// </summary>
    Task DeletePromptAsync(string name, string? projectId);

    /// <summary>
    /// Renders a template string with context values.
    /// Supports placeholders: {{title}}, {{id}}, {{description}}, {{branch}}, {{type}}
    /// </summary>
    string? RenderTemplate(string? template, PromptContext context);

    /// <summary>
    /// Ensures default agent prompts (Plan, Build, Rebase) exist as global prompts.
    /// </summary>
    Task EnsureDefaultPromptsAsync();

    /// <summary>
    /// Gets a prompt by its session type.
    /// Used to retrieve specialized prompts like IssueAgentModification.
    /// </summary>
    /// <param name="sessionType">The session type to find a prompt for.</param>
    /// <returns>The prompt for the session type, or null if not found.</returns>
    AgentPrompt? GetPromptBySessionType(SessionType sessionType);

    /// <summary>
    /// Gets all issue agent prompts (IssueAgentModification and IssueAgentSystem).
    /// These are specialized prompts for the Issues Agent workflow.
    /// </summary>
    IReadOnlyList<AgentPrompt> GetIssueAgentPrompts();

    /// <summary>
    /// Gets global prompts with Category = IssueAgent (excluding SessionType prompts).
    /// These are user-selectable prompts for issue agent sessions.
    /// </summary>
    IReadOnlyList<AgentPrompt> GetIssueAgentUserPrompts();

    /// <summary>
    /// Gets project-scoped prompts with Category = IssueAgent (excluding SessionType prompts).
    /// </summary>
    IReadOnlyList<AgentPrompt> GetIssueAgentProjectPrompts(string projectId);

    /// <summary>
    /// Gets merged issue agent prompts for a project context: project-specific IssueAgent prompts,
    /// plus global IssueAgent prompts that are not overridden by project prompts (matched by name, case-insensitive).
    /// </summary>
    IReadOnlyList<AgentPrompt> GetIssueAgentPromptsForProject(string projectId);

    /// <summary>
    /// Creates a project-scoped prompt that overrides a global prompt.
    /// Copies the name and mode from the global prompt.
    /// </summary>
    /// <param name="globalPromptName">The name of the global prompt to override.</param>
    /// <param name="projectId">The project ID to scope the new prompt to.</param>
    /// <param name="initialMessage">Optional custom message. If null, copies from the global prompt.</param>
    /// <returns>The newly created project-scoped prompt.</returns>
    Task<AgentPrompt> CreateOverrideAsync(string globalPromptName, string projectId, string? initialMessage);

    /// <summary>
    /// Removes a project-scoped override prompt, reverting to the global prompt.
    /// </summary>
    /// <param name="name">The name of the project prompt override to remove.</param>
    /// <param name="projectId">The project ID of the override.</param>
    /// <returns>The global prompt that will now take effect.</returns>
    Task<AgentPrompt> RemoveOverrideAsync(string name, string projectId);

    /// <summary>
    /// Deletes all global prompts and re-creates defaults from default-prompts.json.
    /// Project-scoped prompts are not affected.
    /// </summary>
    Task RestoreDefaultPromptsAsync();

    /// <summary>
    /// Deletes all project-scoped prompts for the given project.
    /// </summary>
    Task DeleteAllProjectPromptsAsync(string projectId);
}
