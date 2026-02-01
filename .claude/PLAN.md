# Implementation Plan: Show Full Plan in Session

## Issue Summary
**ID:** kHkkSN

When exiting plan mode in the session window:
1. The full plan is not shown to the user
2. The plan is not written to the `.claude` folder as expected due to permission issues
3. Instead, plans may be written to a `PLAN.md` file in the repository root

## Architecture Overview

### Current State
- **Session Mode**: Defined in `SessionMode.cs` as `Plan` or `Build`
- **Plan Mode Restrictions**: `SessionOptionsFactory.cs` restricts tools to read-only tools (`Read`, `Glob`, `Grep`, `WebFetch`, `WebSearch`, `Task`, `AskUserQuestion`)
- **No ExitPlanMode Handling**: The codebase does not currently capture or handle the `ExitPlanMode` tool that Claude uses to exit plan mode
- **SignalR Events**: Real-time updates use `ClaudeCodeHub.cs` with extension methods for broadcasting events
- **Message Processing**: `ClaudeSessionService.cs` processes SDK messages but doesn't specifically handle `ExitPlanMode`

### Key Files to Modify

| File | Purpose | Changes Required |
|------|---------|-----------------|
| `ClaudeSessionService.cs` | Message processing | Detect `ExitPlanMode` tool use, collect plan content, broadcast event |
| `ClaudeCodeHub.cs` | SignalR hub | Add `BroadcastPlanCompleted` extension method |
| `Session.razor` | UI component | Handle `PlanCompleted` event, display plan modal |
| `ClaudeSession.cs` | Data model | Add `PlanContent` property to store the plan |
| `ToolResultParser.cs` | Tool result parsing | Add parser for `ExitPlanMode` tool |
| `ToolResultData.cs` | Data types | Add `ExitPlanModeToolData` class |
| `SessionOptionsFactory.cs` | Tool restrictions | Add `ExitPlanMode` to allowed tools in Plan mode |

## Implementation Steps

### Step 1: Add ExitPlanMode to Allowed Tools
**File:** `src/Homespun/Features/ClaudeCode/Services/SessionOptionsFactory.cs`

Add `"ExitPlanMode"` to the `PlanModeTools` array:
```csharp
private static readonly string[] PlanModeTools =
[
    "Read",
    "Glob",
    "Grep",
    "WebFetch",
    "WebSearch",
    "Task",
    "AskUserQuestion",
    "ExitPlanMode"  // Add this
];
```

### Step 2: Add Plan Content to Session Model
**File:** `src/Homespun/Features/ClaudeCode/Data/ClaudeSession.cs`

Add property to store the completed plan:
```csharp
/// <summary>
/// The completed plan content (collected when ExitPlanMode is called).
/// </summary>
public string? PlanContent { get; set; }

/// <summary>
/// The path to the plan file if one was written.
/// </summary>
public string? PlanFilePath { get; set; }
```

### Step 3: Add ExitPlanMode Tool Data Type
**File:** `src/Homespun/Features/ClaudeCode/Data/ToolResultData.cs`

Add new data class:
```csharp
/// <summary>
/// Tool-specific data for ExitPlanMode results.
/// </summary>
public class ExitPlanModeToolData : ITypedToolData
{
    /// <summary>
    /// The full plan content.
    /// </summary>
    public required string PlanContent { get; init; }

    /// <summary>
    /// Any allowed prompts/permissions specified in the tool call.
    /// </summary>
    public List<AllowedPrompt>? AllowedPrompts { get; init; }
}

public class AllowedPrompt
{
    public required string Tool { get; init; }
    public required string Prompt { get; init; }
}
```

### Step 4: Add Plan Parsing to ToolResultParser
**File:** `src/Homespun/Features/ClaudeCode/Services/ToolResultParser.cs`

Add parser for ExitPlanMode:
```csharp
// In the Parse method switch:
"exitplanmode" => ParseExitPlanModeResult(contentString, isError),

// New method:
private ToolResultData ParseExitPlanModeResult(string content, bool isError)
{
    return new ToolResultData
    {
        ToolName = "ExitPlanMode",
        Summary = "Plan completed",
        IsSuccess = !isError,
        TypedData = new ExitPlanModeToolData
        {
            PlanContent = content
        }
    };
}
```

### Step 5: Add SignalR Broadcast for Plan Completion
**File:** `src/Homespun/Features/ClaudeCode/Hubs/ClaudeCodeHub.cs`

Add extension method:
```csharp
/// <summary>
/// Broadcasts when a plan is completed (ExitPlanMode called).
/// </summary>
public static async Task BroadcastPlanCompleted(
    this IHubContext<ClaudeCodeHub> hubContext,
    string sessionId,
    string planContent,
    string? planFilePath = null)
{
    await hubContext.Clients.Group($"session-{sessionId}")
        .SendAsync("PlanCompleted", sessionId, planContent, planFilePath);
}
```

### Step 6: Detect ExitPlanMode in Session Service
**File:** `src/Homespun/Features/ClaudeCode/Services/ClaudeSessionService.cs`

Modify `CreateToolUseContent` to detect ExitPlanMode and collect plan:
```csharp
private async Task<ClaudeMessageContent> CreateToolUseContentAsync(
    string sessionId,
    ClaudeSession session,
    Dictionary<string, object> blockData,
    int index)
{
    var toolUseId = GetStringValue(blockData, "id") ?? "";
    var toolName = GetStringValue(blockData, "name") ?? "unknown";

    // Track the tool use ID -> name mapping
    if (!string.IsNullOrEmpty(toolUseId))
    {
        var sessionToolUses = _sessionToolUses.GetOrAdd(sessionId, _ => new ConcurrentDictionary<string, string>());
        sessionToolUses[toolUseId] = toolName;
    }

    // Handle ExitPlanMode specially - collect plan content and broadcast
    if (toolName.Equals("ExitPlanMode", StringComparison.OrdinalIgnoreCase))
    {
        await HandleExitPlanModeAsync(sessionId, session, blockData);
    }

    return new ClaudeMessageContent
    {
        Type = ClaudeContentType.ToolUse,
        ToolName = toolName,
        ToolUseId = toolUseId,
        ToolInput = "",
        IsStreaming = true,
        Index = index
    };
}

private async Task HandleExitPlanModeAsync(
    string sessionId,
    ClaudeSession session,
    Dictionary<string, object> blockData)
{
    // Collect all text content from assistant messages as the plan
    var planContent = CollectPlanContent(session);
    session.PlanContent = planContent;

    // Check if a plan file path was specified in the tool input
    var planFilePath = TryGetPlanFilePath(blockData);
    session.PlanFilePath = planFilePath;

    // Broadcast to UI
    await _hubContext.BroadcastPlanCompleted(sessionId, planContent, planFilePath);

    _logger.LogInformation("ExitPlanMode called for session {SessionId}, plan content length: {Length}",
        sessionId, planContent.Length);
}

private string CollectPlanContent(ClaudeSession session)
{
    var planBuilder = new StringBuilder();

    foreach (var message in session.Messages)
    {
        if (message.Role == ClaudeMessageRole.Assistant)
        {
            foreach (var content in message.Content)
            {
                if (content.Type == ClaudeContentType.Text && !string.IsNullOrEmpty(content.Text))
                {
                    if (planBuilder.Length > 0)
                        planBuilder.AppendLine();
                    planBuilder.Append(content.Text);
                }
            }
        }
    }

    return planBuilder.ToString();
}

private string? TryGetPlanFilePath(Dictionary<string, object> blockData)
{
    // ExitPlanMode may specify a plan file path in its input
    // This would be in the tool's input parameters if present
    return null; // TODO: Extract from input if present
}
```

### Step 7: Add UI Handler for Plan Display
**File:** `src/Homespun/Features/ClaudeCode/Components/Pages/Session.razor`

Add SignalR handler and modal display:

```razor
@* Add modal markup in the component *@
@if (_showPlanModal && !string.IsNullOrEmpty(_completedPlanContent))
{
    <div class="plan-modal-overlay" @onclick="ClosePlanModal">
        <div class="plan-modal" @onclick:stopPropagation>
            <div class="plan-modal-header">
                <h3>📋 Implementation Plan</h3>
                <button class="btn btn-sm btn-outline-secondary" @onclick="ClosePlanModal">×</button>
            </div>
            <div class="plan-modal-content markdown-content">
                @((MarkupString)MarkdownService.RenderToHtml(_completedPlanContent))
            </div>
            <div class="plan-modal-footer">
                <button class="btn btn-primary" @onclick="ClosePlanModal">Continue to Build</button>
            </div>
        </div>
    </div>
}
```

Add code-behind:
```csharp
private bool _showPlanModal = false;
private string? _completedPlanContent;

// In SetupSignalR method:
_hubConnection.On<string, string, string?>("PlanCompleted", (sessionId, planContent, planFilePath) =>
{
    _ = InvokeAsync(() =>
    {
        try
        {
            LogDebug("PlanCompleted", $"Plan length: {planContent.Length} chars");

            if (_session != null && _session.Id == sessionId)
            {
                _completedPlanContent = planContent;
                _showPlanModal = true;
                StateHasChanged();
            }
        }
        catch (Exception ex)
        {
            LogDebug("ERROR", $"PlanCompleted: {ex.Message}");
        }
    });
});

private void ClosePlanModal()
{
    _showPlanModal = false;
    StateHasChanged();
}
```

Add CSS for modal:
```css
.plan-modal-overlay {
    position: fixed;
    inset: 0;
    background: rgba(0, 0, 0, 0.5);
    display: flex;
    align-items: center;
    justify-content: center;
    z-index: 1000;
}

.plan-modal {
    background: var(--bg-primary);
    border-radius: var(--radius-lg);
    border: 1px solid var(--border-color);
    max-width: 800px;
    max-height: 80vh;
    width: 90%;
    display: flex;
    flex-direction: column;
}

.plan-modal-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding: var(--spacing-md);
    border-bottom: 1px solid var(--border-color);
}

.plan-modal-header h3 {
    margin: 0;
}

.plan-modal-content {
    flex: 1;
    overflow-y: auto;
    padding: var(--spacing-lg);
}

.plan-modal-footer {
    padding: var(--spacing-md);
    border-top: 1px solid var(--border-color);
    display: flex;
    justify-content: flex-end;
}
```

### Step 8: Handle Plan File Permissions

The issue mentions plans being written to `PLAN.md` in the repository instead of `.claude` folder. This is a Claude CLI behavior. To address this:

1. **Ensure `.claude` directory exists and is writable** in the container environment
2. **Monitor for PLAN.md file creation** in the working directory and display its contents

Add to `ClaudeSessionService.cs`:
```csharp
private async Task<string?> TryReadPlanFileAsync(string workingDirectory)
{
    // Check for PLAN.md in working directory (Claude CLI fallback location)
    var planFilePath = Path.Combine(workingDirectory, "PLAN.md");
    if (File.Exists(planFilePath))
    {
        try
        {
            return await File.ReadAllTextAsync(planFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read plan file at {Path}", planFilePath);
        }
    }

    // Check for plan in .claude folder
    var claudePlanPath = Path.Combine(workingDirectory, ".claude", "plan.md");
    if (File.Exists(claudePlanPath))
    {
        try
        {
            return await File.ReadAllTextAsync(claudePlanPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not read plan file at {Path}", claudePlanPath);
        }
    }

    return null;
}
```

## Testing Strategy

1. **Unit Tests**
   - Test `ToolResultParser.Parse()` for `ExitPlanMode` tool
   - Test `CollectPlanContent()` method

2. **Integration Tests**
   - Test SignalR broadcast for `PlanCompleted` event
   - Test UI modal display

3. **Manual Testing with Mock Container**
   - Use `mock.sh` to start the container
   - Create a session in Plan mode
   - Ask Claude to create a plan
   - Verify `ExitPlanMode` triggers the modal
   - Verify plan content is displayed correctly

## Potential Risks

1. **ExitPlanMode timing**: The tool may be called before all text content is streamed, requiring buffering
2. **Large plans**: Very large plans may cause UI performance issues - consider pagination
3. **Plan file permissions**: Container environment may have different permission behaviors

## Success Criteria

- [ ] When Claude calls `ExitPlanMode`, the full plan is displayed in a modal
- [ ] The plan content includes all assistant text from the planning session
- [ ] If a `PLAN.md` file is created, its contents are detected and displayed
- [ ] The UI properly handles the plan completed event via SignalR
- [ ] Plan mode can successfully transition to build mode after plan approval
