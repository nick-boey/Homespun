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
            .Where(p => p.ProjectId == null && p.SessionType == null)
            .ToList()
            .AsReadOnly();
    }

    public IReadOnlyList<AgentPrompt> GetProjectPrompts(string projectId)
    {
        return _dataStore.GetAgentPromptsByProject(projectId)
            .Where(p => p.SessionType == null)
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

    public AgentPrompt? GetPrompt(string id)
    {
        return _dataStore.GetAgentPrompt(id);
    }

    public Task<AgentPrompt> CreatePromptAsync(string name, string? initialMessage, SessionMode mode)
    {
        return CreatePromptAsync(name, initialMessage, mode, projectId: null);
    }

    public async Task<AgentPrompt> CreatePromptAsync(string name, string? initialMessage, SessionMode mode, string? projectId)
    {
        var prompt = new AgentPrompt
        {
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
        var prompt = _dataStore.GetAgentPrompt(id)
            ?? throw new InvalidOperationException($"Agent prompt with ID '{id}' not found.");

        prompt.Name = name;
        prompt.InitialMessage = initialMessage;
        prompt.Mode = mode;
        prompt.UpdatedAt = DateTime.UtcNow;

        await _dataStore.UpdateAgentPromptAsync(prompt);
        return prompt;
    }

    public async Task DeletePromptAsync(string id)
    {
        await _dataStore.RemoveAgentPromptAsync(id);
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

    public async Task EnsureDefaultPromptsAsync()
    {
        var existingPrompts = GetAllPrompts();

        // Create Plan prompt if it doesn't exist
        if (!existingPrompts.Any(p => p.Name == "Plan"))
        {
            await CreatePromptAsync(
                "Plan",
                GetDefaultPlanMessage(),
                SessionMode.Plan);
        }

        // Create Build prompt if it doesn't exist
        if (!existingPrompts.Any(p => p.Name == "Build"))
        {
            await CreatePromptAsync(
                "Build",
                GetDefaultBuildMessage(),
                SessionMode.Build);
        }

        // Create Rebase prompt if it doesn't exist
        if (!existingPrompts.Any(p => p.Name.Equals("Rebase", StringComparison.OrdinalIgnoreCase)))
        {
            await CreatePromptAsync(
                "Rebase",
                GetDefaultRebaseMessage(),
                SessionMode.Build);
        }

        // Create IssueAgentModification prompt if it doesn't exist
        // Check all prompts (including session-type-specific ones)
        var hasIssueAgentModification = _dataStore.AgentPrompts
            .Any(p => p.Name.Equals("IssueAgentModification", StringComparison.OrdinalIgnoreCase));
        if (!hasIssueAgentModification)
        {
            await CreateSessionTypePromptAsync(
                "IssueAgentModification",
                GetDefaultIssueAgentModificationMessage(),
                SessionMode.Build,
                SessionType.IssueAgentModification);
        }

        // Create IssueAgentSystem prompt if it doesn't exist
        var hasIssueAgentSystem = _dataStore.AgentPrompts
            .Any(p => p.Name.Equals("IssueAgentSystem", StringComparison.OrdinalIgnoreCase));
        if (!hasIssueAgentSystem)
        {
            await CreateSessionTypePromptAsync(
                "IssueAgentSystem",
                GetDefaultIssueAgentSystemMessage(),
                SessionMode.Build,
                SessionType.IssueAgentSystem);
        }
    }

    private async Task<AgentPrompt> CreateSessionTypePromptAsync(
        string name,
        string? initialMessage,
        SessionMode mode,
        SessionType sessionType)
    {
        var prompt = new AgentPrompt
        {
            Name = name,
            InitialMessage = initialMessage,
            Mode = mode,
            ProjectId = null,
            SessionType = sessionType,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await _dataStore.AddAgentPromptAsync(prompt);
        return prompt;
    }

    private static string GetDefaultPlanMessage()
    {
        return """
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
            """;
    }

    private static string GetDefaultBuildMessage()
    {
        return """
            ## Issue: {{title}}

            **ID:** {{id}}
            **Type:** {{type}}
            **Branch:** {{branch}}

            ### Description
            {{description}}

            ---

            Please implement this issue. Start by understanding the requirements and exploring the codebase, then proceed with the implementation.
            """;
    }

    private static string GetDefaultRebaseMessage()
    {
        return """
            ## Rebase Request

            Please rebase branch `{{branch}}` onto the latest default branch.

            Follow the workflow in your system prompt:
            1. Fetch the latest changes
            2. Analyze the commits to be rebased
            3. Perform the rebase
            4. Resolve any conflicts using the context provided
            5. Run tests to verify no regressions
            6. Push with --force-with-lease when ready
            """;
    }

    private static string GetDefaultIssueAgentModificationMessage()
    {
        return """
            ## Issue Modification Request

            You are an agent designed to modify Fleece issues based on user instructions.

            IMPORTANT CONSTRAINTS:
            - You may ONLY use the Fleece CLI tool to make modifications
            - Do NOT write any files in the repository
            - Do NOT make any code changes
            - Focus solely on issue modifications using fleece commands

            {{#if selectedIssueId}}
            **Selected Issue:** {{selectedIssueId}}
            {{/if}}

            Available fleece commands:
            - fleece list --oneline - List all issues
            - fleece show <id> --json - Show issue details
            - fleece edit <id> -t <title> -s <status> -d <description> - Edit issue
            - fleece create -t <title> -s <status> -y <type> - Create new issue
            - fleece edit <id> --parent-issues <parent>:<order> - Set parent hierarchy
            - fleece list --tree - View issue hierarchy
            - fleece list --next - View task graph

            User request: {{userPrompt}}
            """;
    }

    private static string GetDefaultIssueAgentSystemMessage()
    {
        return """
            # Issue Agent System Prompt

            You are an agent specialized in managing Fleece issues. Your role is to help users organize, modify, and maintain their issue tracking.

            ## CONSTRAINTS

            **IMPORTANT: You must follow these constraints strictly:**

            1. **Only use the Fleece CLI** - All modifications must be done through `fleece` commands
            2. **No file modifications** - Do NOT write, create, or modify any files in the repository
            3. **No code changes** - Do NOT make any code changes or edits to source files
            4. **Focus on issues only** - Your sole purpose is issue management

            ## AVAILABLE COMMANDS

            ### Viewing Issues
            - `fleece list --oneline` - List all issues in compact format
            - `fleece list --tree` - View issues as parent-child hierarchy
            - `fleece list --next` - View task graph showing execution order
            - `fleece show <id> --json` - Show full details of a specific issue

            ### Modifying Issues
            - `fleece edit <id> -t <title>` - Update issue title
            - `fleece edit <id> -s <status>` - Update status (open, progress, review, complete, archived, closed)
            - `fleece edit <id> -d <description>` - Update description
            - `fleece edit <id> -y <type>` - Update type (task, bug, chore, feature)
            - `fleece edit <id> --parent-issues <parent-id>:<lex-order>` - Set parent relationship

            ### Creating Issues
            - `fleece create -t <title> -s <status> -y <type>` - Create new issue
            - `fleece create -t <title> -y <type> --parent-issues <parent-id>:<lex-order>` - Create as child of existing issue

            ## BEST PRACTICES

            1. **Always verify first** - Before modifying an issue, use `fleece show <id> --json` to understand its current state
            2. **Use the hierarchy** - Break down large tasks into sub-tasks using parent-child relationships
            3. **Status workflow** - Issues typically flow: open → progress → review → complete
            4. **Be precise** - Make targeted changes based on user instructions
            5. **Confirm changes** - After making changes, verify with `fleece show <id> --json`

            ## ISSUE TYPES
            - `task` - General work items
            - `bug` - Defects or issues to fix
            - `feature` - New functionality
            - `chore` - Maintenance or housekeeping

            ## STATUS VALUES
            - `open` - Not yet started
            - `progress` - Currently being worked on
            - `review` - Ready for review
            - `complete` - Finished
            - `archived` - No longer relevant
            - `closed` - Abandoned or won't fix
            """;
    }
}
