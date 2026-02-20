
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
    /// Gets an agent prompt by ID.
    /// </summary>
    AgentPrompt? GetPrompt(string id);

    /// <summary>
    /// Creates a new global agent prompt.
    /// </summary>
    Task<AgentPrompt> CreatePromptAsync(string name, string? initialMessage, SessionMode mode);

    /// <summary>
    /// Creates a new agent prompt, optionally scoped to a project.
    /// </summary>
    Task<AgentPrompt> CreatePromptAsync(string name, string? initialMessage, SessionMode mode, string? projectId);

    /// <summary>
    /// Updates an existing agent prompt.
    /// </summary>
    Task<AgentPrompt> UpdatePromptAsync(string id, string name, string? initialMessage, SessionMode mode);

    /// <summary>
    /// Deletes an agent prompt.
    /// </summary>
    Task DeletePromptAsync(string id);

    /// <summary>
    /// Renders a template string with context values.
    /// Supports placeholders: {{title}}, {{id}}, {{description}}, {{branch}}, {{type}}
    /// </summary>
    string? RenderTemplate(string? template, PromptContext context);

    /// <summary>
    /// Ensures default agent prompts (Plan, Build, Rebase) exist as global prompts.
    /// </summary>
    Task EnsureDefaultPromptsAsync();
}
