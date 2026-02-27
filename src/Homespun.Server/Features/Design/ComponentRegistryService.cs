namespace Homespun.Features.Design;

/// <summary>
/// Service that provides metadata for all design system components.
/// </summary>
public class ComponentRegistryService : IComponentRegistryService
{
    private readonly List<ComponentMetadata> _components = new()
    {
        // Core UI Components
        new ComponentMetadata
        {
            Id = "work-item",
            Name = "WorkItem",
            Description = "Displays a work item (issue/PR) with status indicator, title, and graph lines for hierarchy visualization.",
            Category = "Core",
            ComponentPath = "Shared/WorkItem.razor",
            Tags = new() { "status", "issue", "pr", "list" }
        },
        new ComponentMetadata
        {
            Id = "pr-status-badges",
            Name = "PrStatusBadges",
            Description = "Displays pull request status with multiple badges showing checks, approval, merge readiness, and conflicts.",
            Category = "Core",
            ComponentPath = "Shared/PrStatusBadges.razor",
            Tags = new() { "status", "pr", "badge", "github" }
        },
        new ComponentMetadata
        {
            Id = "agent-status-indicator",
            Name = "AgentStatusIndicator",
            Description = "Shows active agent session counts (working/waiting) with real-time SignalR updates.",
            Category = "Core",
            ComponentPath = "Shared/AgentStatusIndicator.razor",
            Tags = new() { "agent", "status", "realtime" }
        },
        new ComponentMetadata
        {
            Id = "notification-banner",
            Name = "NotificationBanner",
            Description = "Displays notification messages with different severity levels (info, warning, action required) and dismissible actions.",
            Category = "Core",
            ComponentPath = "Shared/NotificationBanner.razor",
            Tags = new() { "notification", "alert", "banner" }
        },
        new ComponentMetadata
        {
            Id = "form-message",
            Name = "FormMessage",
            Description = "Inline alert component for displaying error, success, warning, and info messages in forms.",
            Category = "Forms",
            ComponentPath = "Shared/FormMessage.razor",
            Tags = new() { "form", "alert", "message", "error", "success", "warning", "info" }
        },
        new ComponentMetadata
        {
            Id = "model-selector",
            Name = "ModelSelector",
            Description = "Dropdown selector for choosing AI model (Opus, Sonnet, Haiku).",
            Category = "Forms",
            ComponentPath = "Shared/ModelSelector.razor",
            Tags = new() { "form", "select", "model", "ai" }
        },
        new ComponentMetadata
        {
            Id = "info-list",
            Name = "InfoList",
            Description = "A reusable component for displaying definition lists (dt/dd pairs).",
            Category = "Core",
            ComponentPath = "Shared/InfoList.razor",
            Tags = new() { "list", "info", "display" }
        },
        new ComponentMetadata
        {
            Id = "quick-issue-create-bar",
            Name = "QuickIssueCreateBar",
            Description = "Fixed bottom bar for quickly creating issues with type selector, group, and title inputs.",
            Category = "Forms",
            ComponentPath = "Shared/QuickIssueCreateBar.razor",
            Tags = new() { "form", "issue", "create", "quick" }
        },

        // Panel Components
        new ComponentMetadata
        {
            Id = "issue-detail-panel",
            Name = "IssueDetailPanel",
            Description = "Detailed panel showing issue information, session status, and available actions.",
            Category = "Panels",
            ComponentPath = "Shared/IssueDetailPanel.razor",
            Tags = new() { "panel", "issue", "detail" }
        },
        new ComponentMetadata
        {
            Id = "current-pull-request-detail-panel",
            Name = "CurrentPullRequestDetailPanel",
            Description = "Panel displaying pull request details with status, checks, and merge options.",
            Category = "Panels",
            ComponentPath = "Shared/CurrentPullRequestDetailPanel.razor",
            Tags = new() { "panel", "pr", "detail" }
        },
        new ComponentMetadata
        {
            Id = "merged-pull-request-detail-panel",
            Name = "MergedPullRequestDetailPanel",
            Description = "Panel displaying merged pull request details with linked issue information.",
            Category = "Panels",
            ComponentPath = "Shared/MergedPullRequestDetailPanel.razor",
            Tags = new() { "panel", "pr", "merged", "issue" }
        },
        new ComponentMetadata
        {
            Id = "sessions-panel",
            Name = "SessionsPanel",
            Description = "Unified panel for managing Claude Code sessions. Groups by project globally, or by status when filtered to a specific project.",
            Category = "Panels",
            ComponentPath = "Shared/SessionsPanel.razor",
            Tags = new() { "panel", "session", "agent", "management" }
        },
        new ComponentMetadata
        {
            Id = "agent-status-panel",
            Name = "AgentStatusPanel",
            Description = "Panel showing the current status of an agent session.",
            Category = "Panels",
            ComponentPath = "Shared/AgentStatusPanel.razor",
            Tags = new() { "panel", "agent", "status" }
        },
        new ComponentMetadata
        {
            Id = "clone-management-panel",
            Name = "CloneManagementPanel",
            Description = "Panel for managing git clones with create, delete, and switch actions.",
            Category = "Panels",
            ComponentPath = "Shared/CloneManagementPanel.razor",
            Tags = new() { "panel", "git", "clone" }
        },
        new ComponentMetadata
        {
            Id = "session-history-list",
            Name = "SessionHistoryList",
            Description = "List of Claude Code session history items.",
            Category = "Panels",
            ComponentPath = "Shared/SessionHistoryList.razor",
            Tags = new() { "list", "session", "history" }
        },
        new ComponentMetadata
        {
            Id = "agent-selector",
            Name = "AgentSelector",
            Description = "Selector for choosing which agent/prompt to use when starting a session.",
            Category = "Forms",
            ComponentPath = "Shared/AgentSelector.razor",
            Tags = new() { "form", "agent", "selector" }
        },

        // Chat Components
        new ComponentMetadata
        {
            Id = "chat-message",
            Name = "ChatMessage",
            Description = "Displays a single chat message from user or assistant with timestamp.",
            Category = "Chat",
            ComponentPath = "Shared/Chat/ChatMessage.razor",
            Tags = new() { "chat", "message" }
        },
        new ComponentMetadata
        {
            Id = "chat-input",
            Name = "ChatInput",
            Description = "Text input area for sending chat messages with send button.",
            Category = "Chat",
            ComponentPath = "Shared/Chat/ChatInput.razor",
            Tags = new() { "chat", "input", "form" }
        },
        new ComponentMetadata
        {
            Id = "content-block",
            Name = "ContentBlock",
            Description = "Renders different content block types (text, tool use, tool result, thinking).",
            Category = "Chat",
            ComponentPath = "Shared/Chat/ContentBlock.razor",
            Tags = new() { "chat", "content" }
        },
        new ComponentMetadata
        {
            Id = "text-block",
            Name = "TextBlock",
            Description = "Renders text content with markdown support.",
            Category = "Chat",
            ComponentPath = "Shared/Chat/TextBlock.razor",
            Tags = new() { "chat", "text", "markdown" }
        },
        new ComponentMetadata
        {
            Id = "tool-use-block",
            Name = "ToolUseBlock",
            Description = "Displays tool invocation with name and input parameters.",
            Category = "Chat",
            ComponentPath = "Shared/Chat/ToolUseBlock.razor",
            Tags = new() { "chat", "tool" }
        },
        new ComponentMetadata
        {
            Id = "tool-result-block",
            Name = "ToolResultBlock",
            Description = "Displays the result of a tool invocation.",
            Category = "Chat",
            ComponentPath = "Shared/Chat/ToolResultBlock.razor",
            Tags = new() { "chat", "tool", "result" }
        },
        new ComponentMetadata
        {
            Id = "thinking-block",
            Name = "ThinkingBlock",
            Description = "Displays Claude's thinking/reasoning process.",
            Category = "Chat",
            ComponentPath = "Shared/Chat/ThinkingBlock.razor",
            Tags = new() { "chat", "thinking" }
        },
        new ComponentMetadata
        {
            Id = "processing-indicator",
            Name = "ProcessingIndicator",
            Description = "Shows that Claude is processing/thinking.",
            Category = "Chat",
            ComponentPath = "Shared/Chat/ProcessingIndicator.razor",
            Tags = new() { "chat", "loading", "processing" }
        },
        new ComponentMetadata
        {
            Id = "loading-spinner",
            Name = "LoadingSpinner",
            Description = "Reusable loading spinner with sm/md/lg size variants and optional label text.",
            Category = "Core",
            ComponentPath = "Shared/LoadingSpinner.razor",
            Tags = new() { "loading", "spinner", "status", "feedback" }
        },
        new ComponentMetadata
        {
            Id = "task-graph-view",
            Name = "TaskGraphView",
            Description = "Renders a task graph with lane-based layout, connectors, and interactive issue rows.",
            Category = "Core",
            ComponentPath = "Components/TaskGraphView.razor",
            Tags = new() { "graph", "task", "issue", "visualization" }
        },
        new ComponentMetadata
        {
            Id = "split-button",
            Name = "SplitButton",
            Description = "A button with a primary action and a dropdown for secondary actions.",
            Category = "Core",
            ComponentPath = "Components/SplitButton.razor",
            Tags = new() { "button", "dropdown", "action" }
        },
        new ComponentMetadata
        {
            Id = "issue-row-actions",
            Name = "IssueRowActions",
            Description = "Inline action buttons for issue rows, showing edit and run agent controls. Appears on hover or selection.",
            Category = "Issue",
            ComponentPath = "Components/IssueRowActions.razor",
            Tags = new() { "issue", "button", "action", "inline" }
        },
        new ComponentMetadata
        {
            Id = "issue-control-panel",
            Name = "IssueControlPanel",
            Description = "Sticky control panel for creating new issues above/below the selected issue in the task graph. Provides touch-friendly buttons for mobile users.",
            Category = "Issue",
            ComponentPath = "Components/IssueControlPanel.razor",
            Tags = new() { "issue", "button", "action", "mobile", "sticky" }
        },
        new ComponentMetadata
        {
            Id = "toolbar-button",
            Name = "ToolbarButton",
            Description = "Reusable icon button with CSS tooltip showing action name and keyboard shortcut.",
            Category = "Core",
            ComponentPath = "Components/ToolbarButton.razor",
            Tags = new() { "button", "icon", "tooltip", "toolbar" }
        },
        new ComponentMetadata
        {
            Id = "project-toolbar",
            Name = "ProjectToolbar",
            Description = "Toolbar for the project page containing edit and run agent buttons for the selected issue.",
            Category = "Core",
            ComponentPath = "Components/ProjectToolbar.razor",
            Tags = new() { "toolbar", "issue", "action", "project" }
        }
    };

    /// <inheritdoc />
    public IReadOnlyList<ComponentMetadata> GetAllComponents() => _components.AsReadOnly();

    /// <inheritdoc />
    public ComponentMetadata? GetComponent(string id) =>
        _components.FirstOrDefault(c => c.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc />
    public IReadOnlyList<ComponentMetadata> GetComponentsByCategory(string category) =>
        _components
            .Where(c => c.Category.Equals(category, StringComparison.OrdinalIgnoreCase))
            .ToList()
            .AsReadOnly();

    /// <inheritdoc />
    public IReadOnlyList<string> GetCategories() =>
        _components
            .Select(c => c.Category)
            .Distinct()
            .OrderBy(c => c)
            .ToList()
            .AsReadOnly();
}
