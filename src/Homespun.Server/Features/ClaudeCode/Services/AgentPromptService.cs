using System.Text.Json;
using System.Text.RegularExpressions;
using Homespun.Features.PullRequests.Data;
using Homespun.Shared.Models.Sessions;

namespace Homespun.Features.ClaudeCode.Services;

/// <summary>
/// Service for managing custom agent prompts.
/// </summary>
public partial class AgentPromptService : IAgentPromptService
{
    private readonly IDataStore _dataStore;

    public AgentPromptService(IDataStore dataStore)
    {
        _dataStore = dataStore;
    }

    public IReadOnlyList<AgentPrompt> GetAllPrompts()
    {
        return _dataStore.AgentPrompts
            .Where(p => p.ProjectId == null && p.SessionType == null && p.Category == PromptCategory.Standard)
            .ToList()
            .AsReadOnly();
    }

    public IReadOnlyList<AgentPrompt> GetProjectPrompts(string projectId)
    {
        return _dataStore.GetAgentPromptsByProject(projectId)
            .Where(p => p.SessionType == null && p.Category == PromptCategory.Standard)
            .ToList()
            .AsReadOnly();
    }

    public IReadOnlyList<AgentPrompt> GetPromptsForProject(string projectId)
    {
        var projectPrompts = GetProjectPrompts(projectId);
        var globalPrompts = GetAllPrompts();

        var globalPromptNames = globalPrompts
            .Select(g => g.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var projectPromptNames = projectPrompts
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Mark project prompts that override global prompts
        foreach (var prompt in projectPrompts)
        {
            prompt.IsOverride = globalPromptNames.Contains(prompt.Name);
        }

        // Include only global prompts that are not overridden
        var nonOverriddenGlobalPrompts = globalPrompts
            .Where(g => !projectPromptNames.Contains(g.Name))
            .ToList();

        return projectPrompts.Concat(nonOverriddenGlobalPrompts).ToList().AsReadOnly();
    }

    public IReadOnlyList<AgentPrompt> GetGlobalPromptsNotOverridden(string projectId)
    {
        var projectPrompts = GetProjectPrompts(projectId);
        var projectPromptNames = projectPrompts
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return GetAllPrompts()
            .Where(g => !projectPromptNames.Contains(g.Name))
            .ToList()
            .AsReadOnly();
    }

    public AgentPrompt? GetPromptBySessionType(SessionType sessionType)
    {
        return _dataStore.AgentPrompts
            .FirstOrDefault(p => p.SessionType == sessionType);
    }

    public IReadOnlyList<AgentPrompt> GetIssueAgentPrompts()
    {
        return _dataStore.AgentPrompts
            .Where(p => p.SessionType == SessionType.IssueAgentModification
                     || p.SessionType == SessionType.IssueAgentSystem)
            .ToList()
            .AsReadOnly();
    }

    public IReadOnlyList<AgentPrompt> GetIssueAgentUserPrompts()
    {
        return _dataStore.AgentPrompts
            .Where(p => p.Category == PromptCategory.IssueAgent && p.SessionType == null && p.ProjectId == null)
            .ToList()
            .AsReadOnly();
    }

    public IReadOnlyList<AgentPrompt> GetIssueAgentProjectPrompts(string projectId)
    {
        return _dataStore.GetAgentPromptsByProject(projectId)
            .Where(p => p.Category == PromptCategory.IssueAgent && p.SessionType == null)
            .ToList()
            .AsReadOnly();
    }

    public IReadOnlyList<AgentPrompt> GetIssueAgentPromptsForProject(string projectId)
    {
        var projectPrompts = GetIssueAgentProjectPrompts(projectId);
        var globalPrompts = GetIssueAgentUserPrompts();

        var globalPromptNames = globalPrompts
            .Select(g => g.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var projectPromptNames = projectPrompts
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Mark project prompts that override global prompts
        foreach (var prompt in projectPrompts)
        {
            prompt.IsOverride = globalPromptNames.Contains(prompt.Name);
        }

        // Include only global prompts that are not overridden
        var nonOverriddenGlobalPrompts = globalPrompts
            .Where(g => !projectPromptNames.Contains(g.Name))
            .ToList();

        return projectPrompts.Concat(nonOverriddenGlobalPrompts).ToList().AsReadOnly();
    }

    public AgentPrompt? GetPrompt(string name, string? projectId)
    {
        return _dataStore.GetAgentPrompt(name, projectId);
    }

    public Task<AgentPrompt> CreatePromptAsync(string name, string? initialMessage, SessionMode mode)
    {
        return CreatePromptAsync(name, initialMessage, mode, projectId: null);
    }

    public async Task<AgentPrompt> CreatePromptAsync(string name, string? initialMessage, SessionMode mode, string? projectId,
        PromptCategory category = PromptCategory.Standard)
    {
        // Validate no duplicate name within the same scope
        var existing = _dataStore.GetAgentPrompt(name, projectId);
        if (existing != null)
        {
            throw new InvalidOperationException(
                $"A prompt with name '{name}' already exists in this scope.");
        }

        var prompt = new AgentPrompt
        {
            Name = name,
            InitialMessage = initialMessage,
            Mode = mode,
            ProjectId = projectId,
            Category = category,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _dataStore.AddAgentPromptAsync(prompt);
        return prompt;
    }

    public async Task<AgentPrompt> UpdatePromptAsync(string name, string? projectId, string? initialMessage, SessionMode mode)
    {
        var prompt = _dataStore.GetAgentPrompt(name, projectId)
            ?? throw new InvalidOperationException($"Agent prompt '{name}' not found.");

        prompt.InitialMessage = initialMessage;
        prompt.Mode = mode;
        prompt.UpdatedAt = DateTime.UtcNow;

        await _dataStore.UpdateAgentPromptAsync(prompt);
        return prompt;
    }

    public async Task DeletePromptAsync(string name, string? projectId)
    {
        await _dataStore.RemoveAgentPromptAsync(name, projectId);
    }

    public async Task<AgentPrompt> CreateOverrideAsync(string globalPromptName, string projectId, string? initialMessage)
    {
        var globalPrompt = _dataStore.GetAgentPrompt(globalPromptName, null)
            ?? throw new InvalidOperationException($"Global agent prompt '{globalPromptName}' not found.");

        if (globalPrompt.ProjectId != null)
        {
            throw new InvalidOperationException("Cannot create override from a non-global prompt. Only global prompts can be overridden.");
        }

        // Check if override already exists
        var existing = _dataStore.GetAgentPrompt(globalPromptName, projectId);
        if (existing != null)
        {
            throw new InvalidOperationException(
                $"A prompt with name '{globalPromptName}' already exists in this project scope.");
        }

        var overridePrompt = new AgentPrompt
        {
            Name = globalPrompt.Name,
            InitialMessage = initialMessage ?? globalPrompt.InitialMessage,
            Mode = globalPrompt.Mode,
            ProjectId = projectId,
            Category = globalPrompt.Category,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _dataStore.AddAgentPromptAsync(overridePrompt);
        return overridePrompt;
    }

    public async Task<AgentPrompt> RemoveOverrideAsync(string name, string projectId)
    {
        var prompt = _dataStore.GetAgentPrompt(name, projectId)
            ?? throw new InvalidOperationException($"Agent prompt '{name}' not found in project '{projectId}'.");

        // Find the global prompt with the same name
        var globalPrompt = GetAllPrompts()
            .FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        if (globalPrompt == null)
        {
            throw new InvalidOperationException($"Cannot remove override: prompt '{name}' is not an override of a global prompt.");
        }

        // Delete the project prompt
        await _dataStore.RemoveAgentPromptAsync(name, projectId);

        return globalPrompt;
    }

    public string? RenderTemplate(string? template, PromptContext context)
    {
        if (template == null)
            return null;

        var result = template;

        // Handle {{#if placeholder}}content{{/if}} conditional blocks first
        result = ConditionalRegex().Replace(result, match =>
        {
            var placeholder = match.Groups[1].Value.ToLowerInvariant();
            var content = match.Groups[2].Value;

            var value = GetPlaceholderValue(placeholder, context);

            // If value is non-empty, include the content; otherwise remove the block
            return string.IsNullOrEmpty(value) ? string.Empty : content;
        });

        // Replace simple {{placeholder}} placeholders (case-insensitive)
        result = PlaceholderRegex().Replace(result, match =>
        {
            var placeholder = match.Groups[1].Value.ToLowerInvariant();
            return GetPlaceholderValue(placeholder, context) ?? match.Value;
        });

        return result;
    }

    private static string? GetPlaceholderValue(string placeholder, PromptContext context)
    {
        return placeholder switch
        {
            "title" => context.Title,
            "id" => context.Id,
            "description" => context.Description ?? string.Empty,
            "branch" => context.Branch,
            "type" => context.Type,
            "context" => context.Context ?? string.Empty,
            "selectedissueid" => context.SelectedIssueId ?? string.Empty,
            "userprompt" => context.UserPrompt ?? string.Empty,
            _ => null // Return null for unknown placeholders
        };
    }

    [GeneratedRegex(@"\{\{#if (\w+)\}\}(.*?)\{\{/if\}\}", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ConditionalRegex();

    [GeneratedRegex(@"\{\{(\w+)\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex PlaceholderRegex();

    public async Task RestoreDefaultPromptsAsync()
    {
        // Delete all global prompts (ProjectId == null)
        var globalPrompts = _dataStore.AgentPrompts
            .Where(p => p.ProjectId == null)
            .ToList();

        foreach (var prompt in globalPrompts)
        {
            await _dataStore.RemoveAgentPromptAsync(prompt.Name, null);
        }

        // Re-create defaults from default-prompts.json
        await EnsureDefaultPromptsAsync();
    }

    public async Task DeleteAllProjectPromptsAsync(string projectId)
    {
        var projectPrompts = _dataStore.GetAgentPromptsByProject(projectId).ToList();

        foreach (var prompt in projectPrompts)
        {
            await _dataStore.RemoveAgentPromptAsync(prompt.Name, projectId);
        }
    }

    public async Task EnsureDefaultPromptsAsync()
    {
        var definitions = LoadDefaultPromptDefinitions();
        var allExistingPrompts = _dataStore.AgentPrompts;

        foreach (var def in definitions)
        {
            var sessionType = ParseSessionType(def.SessionType);
            var mode = ParseSessionMode(def.Mode);
            var category = ParsePromptCategory(def.Category);

            var existing = allExistingPrompts.FirstOrDefault(p =>
                p.Name.Equals(def.Name, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                // Migration: update category on existing prompts if it changed
                if (existing.Category != category)
                {
                    existing.Category = category;
                    existing.UpdatedAt = DateTime.UtcNow;
                    await _dataStore.UpdateAgentPromptAsync(existing);
                }

                continue;
            }

            if (sessionType != null)
            {
                await CreateSessionTypePromptAsync(def.Name, def.InitialMessage, mode, sessionType.Value, category);
            }
            else
            {
                await CreateCategorizedPromptAsync(def.Name, def.InitialMessage, mode, category);
            }
        }
    }

    private async Task<AgentPrompt> CreateSessionTypePromptAsync(
        string name,
        string? initialMessage,
        SessionMode mode,
        SessionType sessionType,
        PromptCategory category = PromptCategory.Standard)
    {
        var prompt = new AgentPrompt
        {
            Name = name,
            InitialMessage = initialMessage,
            Mode = mode,
            ProjectId = null,
            SessionType = sessionType,
            Category = category,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _dataStore.AddAgentPromptAsync(prompt);
        return prompt;
    }

    private async Task<AgentPrompt> CreateCategorizedPromptAsync(
        string name,
        string? initialMessage,
        SessionMode mode,
        PromptCategory category)
    {
        var prompt = new AgentPrompt
        {
            Name = name,
            InitialMessage = initialMessage,
            Mode = mode,
            ProjectId = null,
            Category = category,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _dataStore.AddAgentPromptAsync(prompt);
        return prompt;
    }

    internal static IReadOnlyList<DefaultPromptDefinition> LoadDefaultPromptDefinitions()
    {
        var assemblyDir = Path.GetDirectoryName(typeof(AgentPromptService).Assembly.Location)!;
        var jsonPath = Path.Combine(assemblyDir, "Features", "ClaudeCode", "Resources", "default-prompts.json");
        var json = File.ReadAllText(jsonPath);
        return JsonSerializer.Deserialize<List<DefaultPromptDefinition>>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }) ?? [];
    }

    internal static SessionMode ParseSessionMode(string mode) =>
        mode.Equals("plan", StringComparison.OrdinalIgnoreCase) ? SessionMode.Plan : SessionMode.Build;

    internal static SessionType? ParseSessionType(string? sessionType) =>
        sessionType?.ToLowerInvariant() switch
        {
            "issueagentmodification" => SessionType.IssueAgentModification,
            "issueagentsystem" => SessionType.IssueAgentSystem,
            _ => null
        };

    internal static PromptCategory ParsePromptCategory(string? category) =>
        category?.ToLowerInvariant() switch
        {
            "issueagent" => PromptCategory.IssueAgent,
            _ => PromptCategory.Standard
        };
}
