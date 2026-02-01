# Bug Report: AskUserQuestion Tool UI Not Displaying

**Date Reported**: 2026-01-31
**Severity**: High (Feature not functional)
**Component**: ClaudeSessionService / Session.razor

## Summary

When Claude uses the `AskUserQuestion` tool, the interactive question UI with clickable option buttons does not appear. The session fails to enter the `WaitingForQuestionAnswer` state, and the `pendingQuestion` remains null.

## Expected Behavior

1. Claude uses `AskUserQuestion` tool with questions and options
2. Session status changes from `Running` to `WaitingForQuestionAnswer` (status code 3)
3. `session.PendingQuestion` is populated with the parsed question data
4. SignalR broadcasts `QuestionReceived` event to clients
5. UI renders the `.question-container` with:
   - Question header tag
   - Question text
   - Clickable option buttons
   - "Other..." button for custom input
   - "Submit Answers" button

## Actual Behavior

1. Claude uses `AskUserQuestion` tool ✓
2. Session status changes to `WaitingForInput` (status code 2) ✗
3. `session.PendingQuestion` is `null` ✗
4. `QuestionReceived` event not received by UI
5. Raw JSON is displayed instead of interactive buttons

## Evidence

### API Response
```json
{
  "status": 2,
  "pendingQuestion": null,
  "messages": [
    {
      "content": [
        {
          "type": 2,
          "toolName": "AskUserQuestion",
          "toolInput": "{\"questions\": [{\"question\":\"What type of content...\",\"header\":\"Content type\",\"multiSelect\":false,\"options\":[...]}]}"
        }
      ]
    }
  ]
}
```

Note: `status: 2` = `WaitingForInput`, should be `status: 3` = `WaitingForQuestionAnswer`

### UI Display
- Session badge shows "Waiting" (correct label for `WaitingForInput`)
- Should show "Question" (label for `WaitingForQuestionAnswer`)
- `.question-container` element not present in DOM

## Root Cause Analysis

The bug is in `ClaudeSessionService.cs`. The `HandleAskUserQuestionTool` method (lines 552-624) is designed to:
1. Parse the AskUserQuestion JSON
2. Set `session.PendingQuestion`
3. Set `session.Status = WaitingForQuestionAnswer`
4. Broadcast via SignalR

However, this method appears to not be executing. The issue is likely in `HandleContentBlockStop` (lines 514-547):

```csharp
private async Task HandleContentBlockStop(...)
{
    var index = GetIntValue(eventData, "index") ?? -1;

    // This lookup may find the wrong block or return null
    var streamingBlock = index >= 0
        ? assistantMessage.Content.FirstOrDefault(c => c.IsStreaming && c.Index == index)
        : assistantMessage.Content.LastOrDefault(c => c.IsStreaming);

    if (streamingBlock != null)
    {
        // Only called if streamingBlock is found AND is AskUserQuestion
        if (streamingBlock.Type == ClaudeContentType.ToolUse &&
            streamingBlock.ToolName == "AskUserQuestion" &&
            !string.IsNullOrEmpty(streamingBlock.ToolInput))
        {
            await HandleAskUserQuestionTool(...);
        }
    }
}
```

### Possible Causes

1. **Index collision**: Multiple content blocks may have the same index value, causing `FirstOrDefault` to find the wrong block (e.g., a text block instead of the tool_use block)

2. **Block already marked as not streaming**: The block's `IsStreaming` flag may be set to `false` before `HandleContentBlockStop` runs, causing the lookup to fail

3. **Timing issue**: The `AssistantMessage` processing (lines 347-354) marks all blocks as `IsStreaming = false`, which might run before `content_block_stop` events are processed

4. **Index lookup failure**: When index is -1, it falls back to `LastOrDefault`, which might return a text block that was added after the tool_use block

## Reproduction Steps

1. Start the app in mock-live mode: `./scripts/mock-live.sh`
2. Navigate to Projects → Demo Project
3. Click "Start Test Agent"
4. Wait for initial task to complete
5. Open the session chat
6. Send message: "Please ask me a clarifying question using the AskUserQuestion tool"
7. Wait for Claude's response
8. **Observe**: AskUserQuestion JSON is shown as raw text, no interactive buttons appear

## Files Involved

- `src/Homespun/Features/ClaudeCode/Services/ClaudeSessionService.cs`
  - `HandleContentBlockStop()` - line 514
  - `HandleAskUserQuestionTool()` - line 552
  - `SendMessageAsync()` - lines 306-316 (status finalization)

- `src/Homespun/Features/ClaudeCode/Components/Pages/Session.razor`
  - Question UI rendering - lines 144-199
  - `QuestionReceived` SignalR handler - lines 1631-1668

- `src/Homespun/Features/ClaudeCode/Hubs/ClaudeCodeHub.cs`
  - `BroadcastQuestionReceived()` - lines 213-220

## Suggested Fix

Add logging to trace the exact point of failure:

```csharp
private async Task HandleContentBlockStop(...)
{
    var index = GetIntValue(eventData, "index") ?? -1;
    _logger.LogInformation("content_block_stop received for index {Index}", index);

    var streamingBlock = index >= 0
        ? assistantMessage.Content.FirstOrDefault(c => c.IsStreaming && c.Index == index)
        : assistantMessage.Content.LastOrDefault(c => c.IsStreaming);

    _logger.LogInformation("Found streamingBlock: {Found}, Type: {Type}, ToolName: {ToolName}, IsStreaming: {IsStreaming}",
        streamingBlock != null,
        streamingBlock?.Type,
        streamingBlock?.ToolName,
        streamingBlock?.IsStreaming);

    // ... rest of method
}
```

Then fix based on findings. Likely solutions:
1. Use a more robust block lookup that doesn't depend on `IsStreaming` flag
2. Process `content_block_stop` before `AssistantMessage` finalizes blocks
3. Track tool_use blocks separately by their `ToolUseId` instead of index

## Impact

- Cannot complete manual testing of AskUserQuestion feature
- Users cannot interact with Claude's clarifying questions
- Sessions remain stuck in `WaitingForInput` instead of prompting for answers

## Related Code References

- PR: feat: implement AskUserQuestion tool for interactive Claude sessions
- Branch: sessions/feature/question-tool+pYKwWR
- Issue: pYKwWR - Question tool

---

## Resolution (2026-01-31)

**Status**: FIXED

**Root Cause**: The `AssistantMessage` event was arriving before the final `content_block_stop` events in the stream. When `AssistantMessage` was processed, it set `IsStreaming = false` on all content blocks (lines 347-354). Subsequently, when `content_block_stop` events were processed, the lookup in `HandleContentBlockStop` failed because it filtered on `c.IsStreaming && c.Index == index`, and `IsStreaming` was already `false`.

The same issue affected `HandleContentBlockDelta`, which meant the `ToolInput` JSON was never populated, causing the secondary condition `!string.IsNullOrEmpty(streamingBlock.ToolInput)` to also fail.

**Fix Applied**: Modified both `HandleContentBlockDelta` and `HandleContentBlockStop` to remove the `IsStreaming` filter from the content block lookup. The lookup now uses only the `Index` to find the correct block:

```csharp
// Before (broken):
var streamingBlock = index >= 0
    ? assistantMessage.Content.FirstOrDefault(c => c.IsStreaming && c.Index == index)
    : assistantMessage.Content.LastOrDefault(c => c.IsStreaming);

// After (fixed):
var streamingBlock = index >= 0
    ? assistantMessage.Content.FirstOrDefault(c => c.Index == index)
    : assistantMessage.Content.LastOrDefault();
```

**Files Modified**:
- `src/Homespun/Features/ClaudeCode/Services/ClaudeSessionService.cs`
  - `HandleContentBlockDelta()` - removed `IsStreaming` filter from block lookup
  - `HandleContentBlockStop()` - removed `IsStreaming` filter from block lookup
  - `ProcessSdkMessageAsync()` - added fallback AskUserQuestion detection in `AssistantMessage` handler

- `src/Homespun/Features/ClaudeCode/Components/Pages/Session.razor`
  - `OnInitializedAsync()` - added initialization of `_pendingQuestion` from session data on page load

**Additional Fixes**:
1. Added fallback detection in `AssistantMessage` handler to catch AskUserQuestion tools that may not have been processed via streaming events
2. Fixed UI initialization to load `_pendingQuestion` from session state when the page loads (previously it was only set via SignalR events)

**Verification**:
- Tested with mock-live mode
- Session status correctly changes to `WaitingForQuestionAnswer` (status 3)
- `pendingQuestion` is correctly populated with parsed question data
- Interactive question UI with clickable option buttons appears in the chat
- Status badge shows "Question" instead of "Waiting"
