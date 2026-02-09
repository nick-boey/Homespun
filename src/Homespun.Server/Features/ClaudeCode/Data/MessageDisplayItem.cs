namespace Homespun.Features.ClaudeCode.Data;

/// <summary>
/// A single tool execution pair (tool use + optional tool result).
/// Links a ToolUse content block to its corresponding ToolResult via ToolUseId.
/// </summary>
public class ToolExecution
{
    /// <summary>
    /// The tool use content block from the assistant message.
    /// </summary>
    public required ClaudeMessageContent ToolUse { get; init; }

    /// <summary>
    /// The tool result content block from the user (tool result) message.
    /// Null if the tool is still running and no result has been received yet.
    /// </summary>
    public ClaudeMessageContent? ToolResult { get; set; }

    /// <summary>
    /// Whether this tool execution is still in progress (streaming or awaiting result).
    /// </summary>
    public bool IsRunning => ToolUse.IsStreaming || ToolResult == null;
}

/// <summary>
/// A group of consecutive tool executions to display as a single consolidated bubble.
/// Groups all tool use/result pairs that occur between non-tool messages.
/// </summary>
public class ToolExecutionGroup
{
    /// <summary>
    /// The list of tool executions in this group, in order.
    /// </summary>
    public List<ToolExecution> Executions { get; init; } = [];

    /// <summary>
    /// The timestamp of the first tool execution in this group (for display purposes).
    /// </summary>
    public DateTime Timestamp { get; init; }

    /// <summary>
    /// The original messages that were grouped (for context separator checks).
    /// </summary>
    public List<ClaudeMessage> OriginalMessages { get; init; } = [];
}
