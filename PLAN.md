# Implementation Plan: Fix the Session Chat UI (J5YVtM)

## Issue Summary
The session chat window needs several UI/UX improvements:
1. Move bypass permissions drop-down above the chat box for full-width textarea
2. Change Enter to create newlines; Ctrl+Enter to submit
3. Add model selector dropdown (opus, sonnet, haiku)
4. Add agent prompts dropdown with placeholder replacement
5. Auto-scroll to bottom on new messages + scroll-to-bottom button
6. Add context clearing button with separator

## Current State Analysis

### Files to Modify
- **Primary:** `/src/Homespun/Features/ClaudeCode/Components/Pages/Session.razor` (1,583 lines)
- **Service:** `/src/Homespun/Features/ClaudeCode/Services/IClaudeSessionService.cs` (model switching)
- **Service:** `/src/Homespun/Features/ClaudeCode/Services/ClaudeSessionService.cs` (model switching)
- **Data:** `/src/Homespun/Features/ClaudeCode/Data/ClaudeSession.cs` (context clear markers)

### New Test Files
- `/tests/Homespun.Tests/Components/SessionChatInputTests.cs` (input behavior tests)
- `/tests/Homespun.Tests/Components/SessionChatScrollTests.cs` (scroll behavior tests)
- `/tests/Homespun.Tests/Components/SessionChatControlsTests.cs` (dropdowns, clear context)

---

## Implementation Phases

### Phase 1: Reorganize Input Area Layout (TDD)

**Goal:** Move controls above textarea for full-width input

**Tests to Write First:**
```csharp
[Test] public void InputArea_ShouldHaveControlsRowAboveTextarea()
[Test] public void Textarea_ShouldSpanFullWidth()
[Test] public void ControlsRow_ShouldContainPermissionSelect()
```

**Changes:**
1. Create new `.input-controls` row containing:
   - Permission mode dropdown
   - Model selector dropdown
   - Prompt selector dropdown
   - Clear context button
2. Textarea on separate row below, spanning full width
3. Send button positioned at bottom-right

**New HTML Structure:**
```razor
<div class="input-area">
    <div class="input-controls">
        <select class="permission-select">...</select>
        <select class="model-select">...</select>
        <select class="prompt-select">...</select>
        <button class="btn btn-outline-secondary btn-sm clear-context-btn">Clear Context</button>
    </div>
    <div class="input-row">
        <textarea>...</textarea>
        <button class="send-button">Send</button>
    </div>
</div>
```

---

### Phase 2: Change Keyboard Behavior (TDD)

**Goal:** Enter creates newline; Ctrl+Enter submits

**Tests to Write First:**
```csharp
[Test] public void HandleKeyDown_Enter_ShouldNotSubmit()
[Test] public void HandleKeyDown_ShiftEnter_ShouldNotSubmit()
[Test] public void HandleKeyDown_CtrlEnter_ShouldSubmit()
[Test] public void HandleKeyDown_MetaEnter_ShouldSubmit()  // for Mac
[Test] public void SendButton_Click_ShouldSubmit()
```

**Changes:**
Update `HandleKeyDown` method:
```csharp
private async Task HandleKeyDown(KeyboardEventArgs e)
{
    // Only submit on Ctrl+Enter or Cmd+Enter
    if (e.Key == "Enter" && (e.CtrlKey || e.MetaKey))
    {
        await SendMessage();
    }
    // Enter without modifiers just creates newlines (default behavior)
}
```

Update placeholder text to indicate: `"Type a message... (Ctrl+Enter to send)"`

---

### Phase 3: Add Model Selector (TDD)

**Goal:** Allow switching models during session

**Tests to Write First:**
```csharp
[Test] public void ModelSelector_ShouldDisplayCurrentModel()
[Test] public void ModelSelector_ShouldHaveThreeOptions()
[Test] public void ModelSelector_Change_ShouldUpdateSession()
[Test] public void ModelSelector_ShouldBeDisabledWhenRunning()
```

**Changes:**
1. Add `_selectedModel` field to Session.razor
2. Add model selector dropdown to input-controls
3. Pass selected model to `SendMessageAsync` (needs service update)
4. Update `IClaudeSessionService.SendMessageAsync` signature to accept model parameter

**Service Changes:**
```csharp
Task SendMessageAsync(string sessionId, string message, PermissionMode permissionMode, string? model = null);
```

---

### Phase 4: Add Prompt Selector (TDD)

**Goal:** Select from pre-written prompts with placeholder replacement

**Tests to Write First:**
```csharp
[Test] public void PromptSelector_ShouldLoadAgentPrompts()
[Test] public void PromptSelector_ShouldHaveDefaultEmptyOption()
[Test] public void PromptSelector_Selection_ShouldPopulateTextarea()
[Test] public void PromptSelector_ShouldReplacePlaceholders()
[Test] public void PromptSelector_Selection_ShouldClearPreviousInput()
```

**Changes:**
1. Inject `IAgentPromptService` into Session.razor
2. Load prompts on initialization
3. Add prompt selector dropdown with "(Select a prompt...)" default
4. On selection:
   - Clear current `_inputMessage`
   - Render template with PromptContext (use session metadata)
   - Populate `_inputMessage` with rendered prompt
   - Reset dropdown to default

---

### Phase 5: Auto-Scroll and Scroll-to-Bottom Button (TDD)

**Goal:** Auto-scroll on new messages; show scroll button when scrolled up

**Tests to Write First:**
```csharp
[Test] public void MessagesArea_ShouldAutoScrollOnNewMessage()
[Test] public void ScrollToBottomButton_ShouldBeHiddenWhenAtBottom()
[Test] public void ScrollToBottomButton_ShouldBeVisibleWhenScrolledUp()
[Test] public void ScrollToBottomButton_Click_ShouldScrollToBottom()
```

**Changes:**
1. Fix CSS selector bug (`.messages-container` → `.messages-area`)
2. Add scroll event listener via JS interop to track scroll position
3. Add `_showScrollToBottomButton` state
4. Add floating scroll-to-bottom button in messages area
5. Auto-scroll logic:
   - Only auto-scroll if user is near bottom (within 100px threshold)
   - If user scrolled up, show button but don't auto-scroll

**JS Interop:**
```javascript
// Add to wwwroot/js/session-chat.js
window.sessionChat = {
    isNearBottom: (element, threshold) => {
        return element.scrollHeight - element.scrollTop - element.clientHeight < threshold;
    },
    scrollToBottom: (element) => {
        element.scrollTo({ top: element.scrollHeight, behavior: 'smooth' });
    }
};
```

---

### Phase 6: Clear Context Button (TDD)

**Goal:** Clear context and add visual separator; keep old messages

**Tests to Write First:**
```csharp
[Test] public void ClearContextButton_ShouldBeVisible()
[Test] public void ClearContextButton_Click_ShouldAddSeparator()
[Test] public void ClearContextButton_ShouldNotDeleteMessages()
[Test] public void ContextSeparator_ShouldBeVisuallyDistinct()
[Test] public void ClearContextButton_ShouldBeDisabledWhenRunning()
```

**Changes:**
1. Add `ContextCleared` property to messages (or use special marker message)
2. Add `ClearContext()` method that:
   - Adds a separator marker to the message list
   - Notifies backend to clear conversation context (if needed)
3. Render separator in message list with timestamp
4. Style separator distinctly (horizontal line with "Context Cleared" label)

**Data Model Option:**
```csharp
// In ClaudeSession.cs
public List<DateTime> ContextClearMarkers { get; set; } = new();
```

**UI Rendering:**
```razor
@foreach (var message in _session.Messages)
{
    @* Check if context was cleared before this message *@
    @if (ShouldShowContextSeparator(message))
    {
        <div class="context-separator">
            <span>Context Cleared</span>
        </div>
    }
    // ... render message
}
```

---

## Test File Structure

### `/tests/Homespun.Tests/Components/SessionChatInputTests.cs`
Tests for:
- Keyboard handling (Enter vs Ctrl+Enter)
- Input field binding
- Disabled states
- Placeholder text

### `/tests/Homespun.Tests/Components/SessionChatScrollTests.cs`
Tests for:
- Auto-scroll behavior
- Scroll-to-bottom button visibility
- Scroll position tracking

### `/tests/Homespun.Tests/Components/SessionChatControlsTests.cs`
Tests for:
- Model selector functionality
- Prompt selector and placeholder replacement
- Permission mode selector
- Clear context button

---

## CSS Updates

```css
/* New styles to add */
.input-controls {
    display: flex;
    gap: var(--spacing-sm);
    padding: var(--spacing-sm) var(--spacing-md);
    flex-wrap: wrap;
    align-items: center;
}

.input-row {
    display: flex;
    gap: var(--spacing-sm);
    padding: 0 var(--spacing-md) var(--spacing-md);
}

.input-row textarea {
    flex: 1;
    /* full width in this row */
}

.model-select, .prompt-select {
    /* similar styling to permission-select */
}

.scroll-to-bottom-btn {
    position: absolute;
    bottom: var(--spacing-md);
    right: var(--spacing-md);
    z-index: 10;
    border-radius: 50%;
    /* floating button styling */
}

.context-separator {
    display: flex;
    align-items: center;
    gap: var(--spacing-md);
    padding: var(--spacing-md);
    color: var(--text-muted);
}

.context-separator::before,
.context-separator::after {
    content: '';
    flex: 1;
    height: 1px;
    background: var(--border-color);
}
```

---

## Implementation Order

1. **Write tests for Phase 1** (layout reorganization)
2. **Implement Phase 1** - Make tests pass
3. **Write tests for Phase 2** (keyboard behavior)
4. **Implement Phase 2** - Make tests pass
5. Continue TDD cycle for Phases 3-6

---

## Risks and Mitigations

| Risk | Mitigation |
|------|------------|
| JS interop for scroll detection | Fallback to simple auto-scroll without button |
| Model switching during running session | Disable selector when session is running |
| Breaking existing keyboard shortcuts | Comprehensive test coverage before changes |
| SignalR state sync with context clearing | Clear context locally first, sync with backend async |

---

## Definition of Done

- [ ] All new tests pass
- [ ] Existing tests still pass
- [ ] Input controls above textarea, full-width input
- [ ] Ctrl+Enter to submit (Enter for newlines)
- [ ] Model selector working
- [ ] Prompt selector with placeholder replacement
- [ ] Auto-scroll with scroll-to-bottom button
- [ ] Clear context with separator
- [ ] Visual polish matches existing UI patterns
