using Homespun.Shared.Models.Sessions;

namespace Homespun.Client.Services;

/// <summary>
/// Service for transforming session messages into display items.
/// Handles grouping of tool executions and ordering of content blocks,
/// ensuring thinking blocks appear in correct order relative to tool use blocks.
/// </summary>
public class MessageDisplayService
{
    /// <summary>
    /// Transforms a list of messages into display items, grouping tool executions
    /// and preserving the correct order of thinking/text blocks relative to tools.
    /// </summary>
    /// <param name="messages">The list of messages to transform.</param>
    /// <returns>A list of display items (ClaudeMessage or ToolExecutionGroup).</returns>
    public List<object> GetDisplayItems(IReadOnlyList<ClaudeMessage> messages)
    {
        var displayItems = new List<object>();
        var currentToolGroup = new List<ToolExecution>();
        var currentGroupMessages = new List<ClaudeMessage>();
        var currentGroupTimestamp = DateTime.UtcNow;

        void FlushToolGroup()
        {
            if (currentToolGroup.Count > 0)
            {
                displayItems.Add(new ToolExecutionGroup
                {
                    Executions = new List<ToolExecution>(currentToolGroup),
                    Timestamp = currentGroupTimestamp,
                    OriginalMessages = new List<ClaudeMessage>(currentGroupMessages)
                });
                currentToolGroup.Clear();
                currentGroupMessages.Clear();
            }
        }

        void FlushNonToolBlocks(ClaudeMessage originalMessage, List<ClaudeMessageContent> blocks)
        {
            if (blocks.Count > 0)
            {
                // Create a synthetic message with only the non-tool content
                var syntheticMessage = new ClaudeMessage
                {
                    Id = originalMessage.Id,
                    SessionId = originalMessage.SessionId,
                    Role = originalMessage.Role,
                    CreatedAt = originalMessage.CreatedAt,
                    IsStreaming = originalMessage.IsStreaming
                };
                foreach (var block in blocks)
                {
                    syntheticMessage.Content.Add(block);
                }
                displayItems.Add(syntheticMessage);
                blocks.Clear();
            }
        }

        for (var i = 0; i < messages.Count; i++)
        {
            var msg = messages[i];

            if (msg.Role == ClaudeMessageRole.Assistant)
            {
                ProcessAssistantMessage(msg, displayItems, currentToolGroup, currentGroupMessages,
                    ref currentGroupTimestamp, FlushToolGroup, FlushNonToolBlocks);
            }
            else if (msg.Role == ClaudeMessageRole.User)
            {
                if (!IsRealUserMessage(msg))
                {
                    // This is a tool result message - pair results with pending tool uses
                    ProcessToolResultMessage(msg, currentToolGroup, currentGroupMessages);
                }
                else
                {
                    // Real user message - flush tool group and emit
                    FlushToolGroup();
                    displayItems.Add(msg);
                }
            }
        }

        // Flush any remaining tool group
        FlushToolGroup();

        return displayItems;
    }

    private void ProcessAssistantMessage(
        ClaudeMessage msg,
        List<object> displayItems,
        List<ToolExecution> currentToolGroup,
        List<ClaudeMessage> currentGroupMessages,
        ref DateTime currentGroupTimestamp,
        Action flushToolGroup,
        Action<ClaudeMessage, List<ClaudeMessageContent>> flushNonToolBlocks)
    {
        // Check if this message has any tool use blocks
        var hasToolUseBlocks = msg.Content.Any(c => c.Type == ClaudeContentType.ToolUse);

        if (!hasToolUseBlocks)
        {
            // No tool use blocks - flush any pending tool group and emit as regular message
            flushToolGroup();
            displayItems.Add(msg);
            return;
        }

        // Message has tool use blocks - process in order to preserve thinking/tool interleaving
        // Sort by Index, putting blocks without valid index at the end (preserving list order for them)
        var orderedBlocks = msg.Content
            .Select((block, listIndex) => (block, listIndex))
            .OrderBy(x => x.block.Index >= 0 ? x.block.Index : int.MaxValue)
            .ThenBy(x => x.listIndex) // Preserve list order for blocks with same/no index
            .Select(x => x.block)
            .ToList();

        var pendingNonToolBlocks = new List<ClaudeMessageContent>();
        var addedToCurrentGroup = false;

        foreach (var block in orderedBlocks)
        {
            if (block.Type == ClaudeContentType.ToolUse)
            {
                // Flush any pending non-tool content first
                if (pendingNonToolBlocks.Count > 0)
                {
                    flushToolGroup();
                    flushNonToolBlocks(msg, pendingNonToolBlocks);
                }

                // Track the timestamp for the group
                if (currentToolGroup.Count == 0)
                {
                    currentGroupTimestamp = msg.CreatedAt;
                }

                if (!addedToCurrentGroup)
                {
                    currentGroupMessages.Add(msg);
                    addedToCurrentGroup = true;
                }

                // Add tool use to current group
                currentToolGroup.Add(new ToolExecution { ToolUse = block });
            }
            else if (block.Type == ClaudeContentType.Text || block.Type == ClaudeContentType.Thinking)
            {
                // Accumulate non-tool content
                pendingNonToolBlocks.Add(block);
            }
        }

        // Flush any remaining non-tool content
        if (pendingNonToolBlocks.Count > 0)
        {
            flushToolGroup();
            flushNonToolBlocks(msg, pendingNonToolBlocks);
        }
    }

    private void ProcessToolResultMessage(
        ClaudeMessage msg,
        List<ToolExecution> currentToolGroup,
        List<ClaudeMessage> currentGroupMessages)
    {
        currentGroupMessages.Add(msg);

        foreach (var resultContent in msg.Content.Where(c => c.Type == ClaudeContentType.ToolResult))
        {
            // Find the matching tool use by ToolUseId
            var matchingExecution = currentToolGroup
                .FirstOrDefault(e => e.ToolResult == null && e.ToolUse.ToolUseId == resultContent.ToolUseId);

            if (matchingExecution != null)
            {
                matchingExecution.ToolResult = resultContent;
            }
            else
            {
                // Fallback: match by position (first unmatched tool use)
                var unmatchedExecution = currentToolGroup.FirstOrDefault(e => e.ToolResult == null);
                if (unmatchedExecution != null)
                {
                    unmatchedExecution.ToolResult = resultContent;
                }
                else
                {
                    // No matching tool use found - create a standalone execution
                    currentToolGroup.Add(new ToolExecution
                    {
                        ToolUse = new ClaudeMessageContent
                        {
                            Type = ClaudeContentType.ToolUse,
                            ToolName = resultContent.ParsedToolResult?.ToolName ?? resultContent.ToolName ?? "Tool",
                            ToolUseId = resultContent.ToolUseId
                        },
                        ToolResult = resultContent
                    });
                }
            }
        }
    }

    /// <summary>
    /// Determines if a message is a "real" user message (not just tool results).
    /// </summary>
    private static bool IsRealUserMessage(ClaudeMessage message)
    {
        if (message.Role != ClaudeMessageRole.User) return false;

        // If the message has no content, treat it as a real user message (edge case)
        if (message.Content.Count == 0) return true;

        // If all content blocks are ToolResult, this is NOT a real user message
        var hasOnlyToolResults = message.Content.All(c => c.Type == ClaudeContentType.ToolResult);
        return !hasOnlyToolResults;
    }
}
