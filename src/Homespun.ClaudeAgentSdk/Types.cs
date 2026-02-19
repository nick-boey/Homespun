using System.Text.Json.Serialization;

namespace Homespun.ClaudeAgentSdk;

/// <summary>
/// Permission modes for Claude Code.
/// </summary>
public enum PermissionMode
{
    Default,
    AcceptEdits,
    Plan,
    BypassPermissions
}

/// <summary>
/// Behavior when a JSON message exceeds the maximum buffer size.
/// </summary>
public enum BufferOverflowBehavior
{
    /// <summary>
    /// Throw an exception and terminate the session (original behavior).
    /// </summary>
    ThrowException,

    /// <summary>
    /// Skip the oversized message and continue processing (default).
    /// A warning will be logged via the Stderr callback.
    /// </summary>
    SkipMessage
}

/// <summary>
/// Setting source types.
/// </summary>
public enum SettingSource
{
    User,
    Project,
    Local
}

/// <summary>
/// Permission update destination.
/// </summary>
public enum PermissionUpdateDestination
{
    UserSettings,
    ProjectSettings,
    LocalSettings,
    Session
}

/// <summary>
/// Permission behavior.
/// </summary>
public enum PermissionBehavior
{
    Allow,
    Deny,
    Ask
}

/// <summary>
/// System prompt preset configuration.
/// </summary>
public class SystemPromptPreset
{
    public string Type { get; set; } = "preset";
    public string Preset { get; set; } = "claude_code";
    public string? Append { get; set; }
}

/// <summary>
/// Agent definition configuration.
/// </summary>
public class AgentDefinition
{
    public required string Description { get; set; }
    public required string Prompt { get; set; }
    public List<string>? Tools { get; set; }
    public string? Model { get; set; }
}

/// <summary>
/// Permission rule value.
/// </summary>
public class PermissionRuleValue
{
    public required string ToolName { get; set; }
    public string? RuleContent { get; set; }
}

/// <summary>
/// Permission update configuration.
/// </summary>
public class PermissionUpdate
{
    public required string Type { get; set; }
    public List<PermissionRuleValue>? Rules { get; set; }
    public PermissionBehavior? Behavior { get; set; }
    public PermissionMode? Mode { get; set; }
    public List<string>? Directories { get; set; }
    public PermissionUpdateDestination? Destination { get; set; }
}

/// <summary>
/// Context information for tool permission callbacks.
/// </summary>
public class ToolPermissionContext
{
    public object? Signal { get; set; }
    public List<PermissionUpdate> Suggestions { get; set; } = new();
}

/// <summary>
/// Allow permission result.
/// </summary>
public class PermissionResultAllow
{
    public string Behavior { get; set; } = "allow";
    public Dictionary<string, object>? UpdatedInput { get; set; }
    public List<PermissionUpdate>? UpdatedPermissions { get; set; }
}

/// <summary>
/// Deny permission result.
/// </summary>
public class PermissionResultDeny
{
    public string Behavior { get; set; } = "deny";
    public string Message { get; set; } = "";
    public bool Interrupt { get; set; }
}

/// <summary>
/// Hook context information.
/// </summary>
public class HookContext
{
    public object? Signal { get; set; }
}

/// <summary>
/// Hook JSON output.
/// </summary>
public class HookJsonOutput
{
    public string? Decision { get; set; }
    public string? SystemMessage { get; set; }
    public object? HookSpecificOutput { get; set; }
}

/// <summary>
/// Hook matcher configuration.
/// </summary>
public class HookMatcher
{
    public string? Matcher { get; set; }
    public List<Func<Dictionary<string, object>, string?, HookContext, Task<HookJsonOutput>>> Hooks { get; set; } = new();
}

/// <summary>
/// MCP stdio server configuration.
/// </summary>
public class McpStdioServerConfig
{
    public string Type { get; set; } = "stdio";
    public required string Command { get; set; }
    public List<string>? Args { get; set; }
    public Dictionary<string, string>? Env { get; set; }
}

/// <summary>
/// MCP SSE server configuration.
/// </summary>
public class McpSseServerConfig
{
    public string Type { get; set; } = "sse";
    public required string Url { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
}

/// <summary>
/// MCP HTTP server configuration.
/// </summary>
public class McpHttpServerConfig
{
    public string Type { get; set; } = "http";
    public required string Url { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
}

/// <summary>
/// SDK MCP server configuration.
/// </summary>
public class McpSdkServerConfig
{
    public string Type { get; set; } = "sdk";
    public required string Name { get; set; }
    public required object Instance { get; set; }
}

/// <summary>
/// Text content block.
/// </summary>
public class TextBlock
{
    public required string Text { get; set; }
}

/// <summary>
/// Thinking content block.
/// </summary>
public class ThinkingBlock
{
    public required string Thinking { get; set; }
    public required string Signature { get; set; }
}

/// <summary>
/// Tool use content block.
/// </summary>
public class ToolUseBlock
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required Dictionary<string, object> Input { get; set; }
}

/// <summary>
/// Tool result content block.
/// </summary>
public class ToolResultBlock
{
    public required string ToolUseId { get; set; }
    public object? Content { get; set; }
    public bool? IsError { get; set; }
}

/// <summary>
/// Base class for all messages.
/// </summary>
public abstract class Message
{
}

/// <summary>
/// User message.
/// </summary>
public class UserMessage : Message
{
    public required object Content { get; set; }
    public string? ParentToolUseId { get; set; }
}

/// <summary>
/// Assistant message with content blocks.
/// </summary>
public class AssistantMessage : Message
{
    public required List<object> Content { get; set; }
    public required string Model { get; set; }
    public string? ParentToolUseId { get; set; }
}

/// <summary>
/// System message with metadata.
/// </summary>
public class SystemMessage : Message
{
    public required string Subtype { get; set; }
    public required Dictionary<string, object> Data { get; set; }
}

/// <summary>
/// Result message with cost and usage information.
/// </summary>
public class ResultMessage : Message
{
    public required string Subtype { get; set; }
    public required int DurationMs { get; set; }
    public required int DurationApiMs { get; set; }
    public required bool IsError { get; set; }
    public required int NumTurns { get; set; }
    public required string SessionId { get; set; }
    public double? TotalCostUsd { get; set; }
    public Dictionary<string, object>? Usage { get; set; }
    public string? Result { get; set; }
}

/// <summary>
/// Stream event for partial message updates during streaming.
/// </summary>
public class StreamEvent : Message
{
    public required string Uuid { get; set; }
    public required string SessionId { get; set; }
    public required Dictionary<string, object> Event { get; set; }
    public string? ParentToolUseId { get; set; }
}

/// <summary>
/// Control request message from Claude Code for MCP tool operations.
/// </summary>
public class ControlRequest : Message
{
    public required string ControlType { get; set; }
    public Dictionary<string, object>? Data { get; set; }
    public string? ParentToolUseId { get; set; }
}

/// <summary>
/// Rate limit event message from Claude API.
/// Indicates that rate limiting is occurring and the SDK should wait.
/// </summary>
public class RateLimitEvent : Message
{
    public Dictionary<string, object>? Data { get; set; }
}

/// <summary>
/// Query options for Claude SDK.
/// </summary>
public class ClaudeAgentOptions
{
    public List<string> AllowedTools { get; set; } = new();
    public object? SystemPrompt { get; set; }
    public object? McpServers { get; set; }
    public PermissionMode? PermissionMode { get; set; }
    public bool ContinueConversation { get; set; }
    public string? Resume { get; set; }
    public int? MaxTurns { get; set; }
    public List<string> DisallowedTools { get; set; } = new();
    public string? Model { get; set; }
    public string? PermissionPromptToolName { get; set; }
    public string? Cwd { get; set; }
    /// <summary>
    /// Explicit path to the Claude CLI executable. If not specified, auto-discovery is used.
    /// </summary>
    public string? CliPath { get; set; }
    public string? Settings { get; set; }
    public List<string> AddDirs { get; set; } = new();
    public Dictionary<string, string> Env { get; set; } = new();
    public Dictionary<string, string?> ExtraArgs { get; set; } = new();
    public int? MaxBufferSize { get; set; }
    /// <summary>
    /// Behavior when a JSON message exceeds the maximum buffer size.
    /// Defaults to SkipMessage for graceful degradation.
    /// </summary>
    public BufferOverflowBehavior BufferOverflowBehavior { get; set; } = BufferOverflowBehavior.SkipMessage;
    /// <summary>
    /// Callback invoked when a buffer overflow occurs.
    /// Parameters: (estimated message type, actual buffer size, max buffer size).
    /// </summary>
    public Action<string?, int, int>? OnBufferOverflow { get; set; }
    public Action<string>? Stderr { get; set; }
    public Func<string, Dictionary<string, object>, ToolPermissionContext, Task<object>>? CanUseTool { get; set; }
    public Dictionary<string, List<HookMatcher>>? Hooks { get; set; }
    public string? User { get; set; }
    public bool IncludePartialMessages { get; set; }
    public bool ForkSession { get; set; }
    public Dictionary<string, AgentDefinition>? Agents { get; set; }
    public List<SettingSource>? SettingSources { get; set; }
}
