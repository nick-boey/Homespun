using System.Text.RegularExpressions;
using Homespun.Features.ClaudeCode.Services;
using Homespun.Features.PullRequests.Data;
using Homespun.Shared.Models.Sessions;
using Microsoft.Extensions.Logging;

namespace Homespun.Features.Testing.Services;

/// <summary>
/// Mock implementation of IAgentPromptService using MockDataStore.
/// </summary>
public partial class MockAgentPromptService : IAgentPromptService
{
    private readonly IDataStore _dataStore;
    private readonly ILogger<MockAgentPromptService> _logger;

    public MockAgentPromptService(
        IDataStore dataStore,
        ILogger<MockAgentPromptService> logger)
    {
        _dataStore = dataStore;
        _logger = logger;
    }

    public IReadOnlyList<AgentPrompt> GetAllPrompts()
    {
        _logger.LogDebug("[Mock] GetAllPrompts (global only, excluding session-type prompts)");
        return _dataStore.AgentPrompts
            .Where(p => p.ProjectId == null && p.SessionType == null)
            .ToList()
            .AsReadOnly();
    }

    public IReadOnlyList<AgentPrompt> GetProjectPrompts(string projectId)
    {
        _logger.LogDebug("[Mock] GetProjectPrompts {ProjectId}", projectId);
        return _dataStore.GetAgentPromptsByProject(projectId)
            .Where(p => p.SessionType == null)
            .ToList()
            .AsReadOnly();
    }

    public AgentPrompt? GetPromptBySessionType(SessionType sessionType)
    {
        _logger.LogDebug("[Mock] GetPromptBySessionType {SessionType}", sessionType);
        return _dataStore.AgentPrompts
            .FirstOrDefault(p => p.SessionType == sessionType);
    }

    public IReadOnlyList<AgentPrompt> GetIssueAgentPrompts()
    {
        _logger.LogDebug("[Mock] GetIssueAgentPrompts");
        return _dataStore.AgentPrompts
            .Where(p => p.SessionType == SessionType.IssueAgentModification
                     || p.SessionType == SessionType.IssueAgentSystem)
            .ToList()
            .AsReadOnly();
    }

    public IReadOnlyList<AgentPrompt> GetPromptsForProject(string projectId)
    {
        _logger.LogDebug("[Mock] GetPromptsForProject {ProjectId}", projectId);
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
        _logger.LogDebug("[Mock] GetGlobalPromptsNotOverridden {ProjectId}", projectId);
        var projectPrompts = GetProjectPrompts(projectId);
        var projectPromptNames = projectPrompts
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return GetAllPrompts()
            .Where(g => !projectPromptNames.Contains(g.Name))
            .ToList()
            .AsReadOnly();
    }

    public AgentPrompt? GetPrompt(string id)
    {
        _logger.LogDebug("[Mock] GetPrompt {Id}", id);
        return _dataStore.GetAgentPrompt(id);
    }

    public Task<AgentPrompt> CreatePromptAsync(string name, string? initialMessage, SessionMode mode)
    {
        return CreatePromptAsync(name, initialMessage, mode, projectId: null);
    }

    public async Task<AgentPrompt> CreatePromptAsync(string name, string? initialMessage, SessionMode mode, string? projectId)
    {
        _logger.LogDebug("[Mock] CreatePrompt {Name} (ProjectId: {ProjectId})", name, projectId ?? "global");

        var prompt = new AgentPrompt
        {
            Id = Guid.NewGuid().ToString("N")[..6],
            Name = name,
            InitialMessage = initialMessage,
            Mode = mode,
            ProjectId = projectId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _dataStore.AddAgentPromptAsync(prompt);
        return prompt;
    }

    public async Task<AgentPrompt> UpdatePromptAsync(string id, string name, string? initialMessage, SessionMode mode)
    {
        _logger.LogDebug("[Mock] UpdatePrompt {Id}", id);

        var prompt = _dataStore.GetAgentPrompt(id);
        if (prompt == null)
        {
            throw new InvalidOperationException($"Prompt {id} not found");
        }

        prompt.Name = name;
        prompt.InitialMessage = initialMessage;
        prompt.Mode = mode;
        prompt.UpdatedAt = DateTime.UtcNow;

        await _dataStore.UpdateAgentPromptAsync(prompt);
        return prompt;
    }

    public async Task DeletePromptAsync(string id)
    {
        _logger.LogDebug("[Mock] DeletePrompt {Id}", id);
        await _dataStore.RemoveAgentPromptAsync(id);
    }

    public async Task<AgentPrompt> CreateOverrideAsync(string globalPromptId, string projectId, string? initialMessage)
    {
        _logger.LogDebug("[Mock] CreateOverride {GlobalPromptId} for {ProjectId}", globalPromptId, projectId);

        var globalPrompt = _dataStore.GetAgentPrompt(globalPromptId)
            ?? throw new InvalidOperationException($"Agent prompt with ID '{globalPromptId}' not found.");

        if (globalPrompt.ProjectId != null)
        {
            throw new InvalidOperationException("Cannot create override from a non-global prompt. Only global prompts can be overridden.");
        }

        var overridePrompt = new AgentPrompt
        {
            Id = Guid.NewGuid().ToString("N")[..6],
            Name = globalPrompt.Name,
            InitialMessage = initialMessage ?? globalPrompt.InitialMessage,
            Mode = globalPrompt.Mode,
            ProjectId = projectId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _dataStore.AddAgentPromptAsync(overridePrompt);
        return overridePrompt;
    }

    public string? RenderTemplate(string? template, PromptContext context)
    {
        if (string.IsNullOrEmpty(template))
        {
            return template;
        }

        var result = template;
        result = TemplatePlaceholderRegex().Replace(result, match =>
        {
            var placeholder = match.Groups[1].Value.ToLowerInvariant();
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
                _ => match.Value // Keep unknown placeholders as-is
            };
        });

        return result;
    }

    public async Task EnsureDefaultPromptsAsync()
    {
        _logger.LogDebug("[Mock] EnsureDefaultPromptsAsync");

        var existingPrompts = GetAllPrompts();

        // Check if Plan prompt exists
        if (!existingPrompts.Any(p => p.Name == "Plan"))
        {
            await _dataStore.AddAgentPromptAsync(new AgentPrompt
            {
                Id = "plan",
                Name = "Plan",
                InitialMessage = """
                    ## Issue: {{title}}

                    **ID:** {{id}}
                    **Type:** {{type}}
                    **Branch:** {{branch}}

                    ### Description
                    {{description}}

                    ---

                    Please analyze this issue and create a detailed implementation plan. Consider:
                    - What files need to be modified or created
                    - The approach and architecture
                    - Any potential challenges or risks
                    - Test requirements
                    """,
                Mode = SessionMode.Plan,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }

        // Check if Build prompt exists
        if (!existingPrompts.Any(p => p.Name == "Build"))
        {
            await _dataStore.AddAgentPromptAsync(new AgentPrompt
            {
                Id = "build",
                Name = "Build",
                InitialMessage = """
                    ## Issue: {{title}}

                    **ID:** {{id}}
                    **Type:** {{type}}
                    **Branch:** {{branch}}

                    ### Description
                    {{description}}

                    ---

                    Please implement this issue. Write the code, create tests as needed, and ensure the implementation is complete and working.
                    """,
                Mode = SessionMode.Build,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
        }
    }

    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex TemplatePlaceholderRegex();
}
