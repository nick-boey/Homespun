using System.Text.Json;

namespace Homespun.Features.ClaudeCode.Data;

/// <summary>
/// Base type for SDK messages from the Claude Agent SDK.
/// Mirrors the TypeScript SDK's SDKMessage union type.
/// </summary>
public abstract record SdkMessage(string Type, string SessionId);

/// <summary>
/// Assistant message containing the model's response.
/// </summary>
public record SdkAssistantMessage(
    string SessionId,
    string? Uuid,
    SdkApiMessage Message,
    string? ParentToolUseId
) : SdkMessage("assistant", SessionId);

/// <summary>
/// User message containing tool results or user input.
/// </summary>
public record SdkUserMessage(
    string SessionId,
    string? Uuid,
    SdkApiMessage Message,
    string? ParentToolUseId
) : SdkMessage("user", SessionId);

/// <summary>
/// Result message indicating the session turn has completed.
/// When IsError is true, Subtype indicates the error type:
/// - "success": Normal completion
/// - "error_max_turns": Maximum conversation turns reached
/// - "error_during_execution": Error occurred during tool execution
/// - "error_max_budget_usd": Session budget limit reached
/// - "error_max_structured_output_retries": Failed to generate structured output
/// </summary>
public record SdkResultMessage(
    string SessionId,
    string? Uuid,
    string? Subtype,
    int DurationMs,
    int DurationApiMs,
    bool IsError,
    int NumTurns,
    decimal TotalCostUsd,
    string? Result,
    List<string>? Errors = null
) : SdkMessage("result", SessionId);

/// <summary>
/// System message containing session metadata like model info and available tools.
/// </summary>
public record SdkSystemMessage(
    string SessionId,
    string? Uuid,
    string? Subtype,
    string? Model,
    List<string>? Tools
) : SdkMessage("system", SessionId);

/// <summary>
/// Control event emitted by the worker when AskUserQuestion is intercepted in canUseTool.
/// Contains the raw questions JSON so the server can parse and display them.
/// </summary>
public record SdkQuestionPendingMessage(
    string SessionId,
    string QuestionsJson
) : SdkMessage("question_pending", SessionId);

/// <summary>
/// Control event emitted by the worker when ExitPlanMode is intercepted in canUseTool.
/// Contains the plan content so the server can display it and wait for user approval.
/// </summary>
public record SdkPlanPendingMessage(
    string SessionId,
    string PlanJson
) : SdkMessage("plan_pending", SessionId);

/// <summary>
/// Stream event containing incremental content (content_block_start/delta/stop).
/// </summary>
public record SdkStreamEvent(
    string SessionId,
    string? Uuid,
    JsonElement? Event,
    string? ParentToolUseId
) : SdkMessage("stream_event", SessionId);

/// <summary>
/// Anthropic API message structure containing role and content blocks.
/// </summary>
public record SdkApiMessage(string Role, List<SdkContentBlock> Content);

/// <summary>
/// Base type for content blocks within an API message.
/// </summary>
public abstract record SdkContentBlock(string Type);

/// <summary>
/// Text content block.
/// </summary>
public record SdkTextBlock(string? Text) : SdkContentBlock("text");

/// <summary>
/// Thinking/reasoning content block.
/// </summary>
public record SdkThinkingBlock(string? Thinking) : SdkContentBlock("thinking");

/// <summary>
/// Tool use content block.
/// </summary>
public record SdkToolUseBlock(string Id, string Name, JsonElement Input) : SdkContentBlock("tool_use");

/// <summary>
/// Tool result content block.
/// </summary>
public record SdkToolResultBlock(string ToolUseId, JsonElement Content, bool? IsError) : SdkContentBlock("tool_result");
