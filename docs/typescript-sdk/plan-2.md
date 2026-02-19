# Phase 2: Update C# Message Types ✅ COMPLETE

## Context

The current C# codebase has a custom `AgentEvent` hierarchy that the Docker execution service produces by parsing SSE events from the worker. The Hono worker (Phase 1) now streams raw `SDKMessage` types from the TypeScript SDK. This phase updates the C# side to consume these SDK message types directly, eliminating lossy translation layers.

## 2.1 New `SdkMessage` Types

Create `src/Homespun/Features/ClaudeCode/Data/SdkMessages.cs` to mirror the TypeScript SDK types:

```csharp
// Base type - all SDK messages have type and session_id
public abstract record SdkMessage(string Type, string SessionId);

// SDKAssistantMessage
public record SdkAssistantMessage(
    string SessionId, string Uuid,
    SdkApiMessage Message,
    string? ParentToolUseId
) : SdkMessage("assistant", SessionId);

// SDKUserMessage
public record SdkUserMessage(
    string SessionId, string? Uuid,
    SdkApiMessage Message,
    string? ParentToolUseId
) : SdkMessage("user", SessionId);

// SDKResultMessage
public record SdkResultMessage(
    string SessionId, string Uuid, string Subtype,
    int DurationMs, bool IsError, int NumTurns,
    decimal TotalCostUsd, string? Result
) : SdkMessage("result", SessionId);

// SDKSystemMessage
public record SdkSystemMessage(
    string SessionId, string Uuid, string Subtype,
    string? Model, List<string>? Tools
) : SdkMessage("system", SessionId);

// SDKPartialAssistantMessage (stream events)
public record SdkStreamEvent(
    string SessionId, string Uuid,
    JsonElement Event,
    string? ParentToolUseId
) : SdkMessage("stream_event", SessionId);

// Anthropic API message structure
public record SdkApiMessage(
    string Role,
    List<SdkContentBlock> Content
);

// Content blocks (text, tool_use, tool_result, thinking)
public abstract record SdkContentBlock(string Type);

public record SdkTextBlock(string Text) : SdkContentBlock("text");
public record SdkThinkingBlock(string Thinking) : SdkContentBlock("thinking");
public record SdkToolUseBlock(string Id, string Name, JsonElement Input) : SdkContentBlock("tool_use");
public record SdkToolResultBlock(string ToolUseId, JsonElement Content, bool? IsError) : SdkContentBlock("tool_result");
```

## 2.2 SSE Parser for SDK Messages

Create `src/Homespun/Features/ClaudeCode/Services/SdkMessageParser.cs`:

A `JsonConverter<SdkMessage>` that deserializes based on the `type` field:
- `"assistant"` -> `SdkAssistantMessage`
- `"user"` -> `SdkUserMessage`
- `"result"` -> `SdkResultMessage`
- `"system"` -> `SdkSystemMessage`
- `"stream_event"` -> `SdkStreamEvent`

And a `JsonConverter<SdkContentBlock>` that deserializes based on block `type`:
- `"text"` -> `SdkTextBlock`
- `"thinking"` -> `SdkThinkingBlock`
- `"tool_use"` -> `SdkToolUseBlock`
- `"tool_result"` -> `SdkToolResultBlock`

## 2.3 Update `IAgentExecutionService`

Change the interface to yield `SdkMessage` instead of `AgentEvent`:

```csharp
public interface IAgentExecutionService
{
    IAsyncEnumerable<SdkMessage> StartSessionAsync(AgentStartRequest request, CancellationToken ct);
    IAsyncEnumerable<SdkMessage> SendMessageAsync(AgentMessageRequest request, CancellationToken ct);
    Task StopSessionAsync(string sessionId, CancellationToken ct);
    Task InterruptSessionAsync(string sessionId, CancellationToken ct);
    Task<AgentSessionStatus?> GetSessionStatusAsync(string sessionId, CancellationToken ct);
    Task<string?> ReadFileFromAgentAsync(string sessionId, string filePath, CancellationToken ct);
}
```

Remove `AnswerQuestionAsync` - question answering is handled by sending a formatted message via `SendMessageAsync`.

## 2.4 Add New Request Fields

```csharp
public record AgentStartRequest(
    string WorkingDirectory,
    SessionMode Mode,
    string Model,
    string Prompt,
    string? SystemPrompt = null,
    string? ResumeSessionId = null,
    // New: per-issue context
    string? IssueId = null,
    string? ProjectId = null,
    string? ProjectName = null
);
```

## 2.5 Update `ClaudeSessionService`

The main processing methods (`ProcessAgentEventAsync` and related) must be refactored to consume `SdkMessage` types instead of `AgentEvent`. The core mapping:

| SDK Message Type | Processing |
|---|---|
| `SdkAssistantMessage` | Extract content blocks from `message.content` array -> `ClaudeMessageContent` items |
| `SdkStreamEvent` | Parse `event` JsonElement for streaming deltas (`content_block_start`/`delta`/`stop`) |
| `SdkResultMessage` | Extract cost, duration, `session_id` for resumption |
| `SdkUserMessage` | Extract tool results from content blocks |
| `SdkSystemMessage` | Extract model, tools info for session metadata |

The existing `ClaudeMessage` / `ClaudeMessageContent` UI types stay unchanged - only the input processing layer changes.

## 2.6 Update `LocalAgentExecutionService`

Update to yield `SdkMessage` types. The local C# SDK (`ClaudeSdkClient`) produces types that map closely:

| C# SDK Type | New SdkMessage Type |
|---|---|
| `StreamEvent` | `SdkStreamEvent` |
| `AssistantMessage` | `SdkAssistantMessage` |
| `ResultMessage` | `SdkResultMessage` |
| `UserMessage` | `SdkUserMessage` |
| `SystemMessage` | `SdkSystemMessage` |

## Critical Files to Modify
- `src/Homespun/Features/ClaudeCode/Services/IAgentExecutionService.cs` - Interface + request/event types
- `src/Homespun/Features/ClaudeCode/Services/ClaudeSessionService.cs` - Main orchestrator (~1386 lines)
- `src/Homespun/Features/ClaudeCode/Services/LocalAgentExecutionService.cs` - Local mode (~588 lines)
- `src/Homespun/Features/ClaudeCode/Data/ClaudeMessage.cs` - Keep as-is (UI types)
- `src/Homespun/Features/ClaudeCode/Data/ClaudeMessageContent.cs` - Keep as-is (UI types)

## Tests
- Unit tests for `SdkMessageParser` JSON deserialization of each message type
- Unit tests for `SdkContentBlock` deserialization (text, thinking, tool_use, tool_result)
- Update existing `ClaudeSessionService` tests to use new `SdkMessage` types
- Update existing `LocalAgentExecutionService` tests

## Verification
1. `dotnet build` compiles without errors
2. `dotnet test tests/Homespun.Tests` passes with updated message types
3. Local mode (mini agents) still work end-to-end
4. SignalR hub still broadcasts correct `ClaudeMessage`/`ClaudeMessageContent` to the UI
