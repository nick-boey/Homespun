import type {
  ClaudeMessage,
  ClaudeMessageContent,
  ToolExecutionGroup,
  MessageDisplayItem,
} from '@/types/tool-execution'
import { ClaudeMessageRole } from '@/api'

/**
 * Groups tool executions from a list of Claude messages
 * @param messages Array of Claude messages to process
 * @returns Array of display items (messages and tool groups)
 */
export function groupToolExecutions(messages: ClaudeMessage[]): MessageDisplayItem[] {
  const displayItems: MessageDisplayItem[] = []
  const toolResultsMap = new Map<string, ClaudeMessageContent>()
  let currentGroup: ToolExecutionGroup | null = null
  const processedMessageIds = new Set<string>()

  // First pass: collect all tool results by toolUseId
  messages.forEach((message) => {
    if (message.content) {
      message.content.forEach((content) => {
        if (content.contentType === 'tool_result' && content.toolUseId) {
          toolResultsMap.set(content.toolUseId, content)
        }
      })
    }
  })

  // Second pass: process messages and create groups
  messages.forEach((message) => {
    // Skip if we've already processed this message as part of a tool result
    if (processedMessageIds.has(message.id)) {
      return
    }

    if (!message.content || message.content.length === 0) {
      // Empty message, add as regular message
      if (currentGroup) {
        displayItems.push({ type: 'toolGroup', group: currentGroup })
        currentGroup = null
      }
      displayItems.push({ type: 'message', message })
      return
    }

    const hasOnlyToolResults = message.content.every((c) => c.contentType === 'tool_result')

    // Skip user messages that only contain tool results (they've been matched)
    // Support both numeric (legacy) and camelCase role formats
    const role = String(message.role)
    const isUserRole = role === ClaudeMessageRole.USER || role === '0'
    const isAssistantRole = role === ClaudeMessageRole.ASSISTANT || role === '1'

    if (hasOnlyToolResults && isUserRole) {
      processedMessageIds.add(message.id)
      return
    }

    // For non-assistant messages (user messages), end any current group
    if (!isAssistantRole && currentGroup) {
      displayItems.push({ type: 'toolGroup', group: currentGroup })
      currentGroup = null
    }

    // Process content blocks
    const nonToolContent: ClaudeMessageContent[] = []
    const toolUseContent: ClaudeMessageContent[] = []

    message.content.forEach((content) => {
      if (content.contentType === 'tool_use') {
        toolUseContent.push(content)
      } else if (content.contentType !== 'tool_result') {
        nonToolContent.push(content)
      }
    })

    // If there are non-tool content blocks, emit them as a message first
    if (nonToolContent.length > 0) {
      if (currentGroup) {
        displayItems.push({ type: 'toolGroup', group: currentGroup })
        currentGroup = null
      }
      displayItems.push({
        type: 'message',
        message: {
          ...message,
          content: nonToolContent,
        },
      })
    }

    // Process tool uses
    if (toolUseContent.length > 0) {
      if (!currentGroup) {
        currentGroup = {
          id: `group-${message.id}`,
          executions: [],
          timestamp: message.createdAt,
          originalMessageIds: [message.id],
        }
      }

      toolUseContent.forEach((toolUse) => {
        // Check for toolResult in the toolUse content block itself
        let toolResult: ClaudeMessageContent | undefined
        let isRunning = true

        if (toolUse.toolResult !== undefined) {
          // Tool result is embedded in the tool use block
          toolResult = {
            contentType: 'tool_result',
            toolUseId: toolUse.toolUseId,
            content: toolUse.toolResult,
            isError: toolUse.isError,
          }
          isRunning = false
        } else if (toolUse.toolUseId && toolResultsMap.has(toolUse.toolUseId)) {
          // Tool result is in a separate message
          toolResult = toolResultsMap.get(toolUse.toolUseId)
          isRunning = false

          // Mark the tool result message as processed
          const resultMessage = messages.find((m) =>
            m.content?.some(
              (c) => c.contentType === 'tool_result' && c.toolUseId === toolUse.toolUseId
            )
          )
          if (resultMessage) {
            processedMessageIds.add(resultMessage.id)
            if (currentGroup && !currentGroup.originalMessageIds.includes(resultMessage.id)) {
              currentGroup.originalMessageIds.push(resultMessage.id)
            }
          }
        }

        currentGroup?.executions.push({
          toolUse,
          toolResult,
          isRunning,
        })
      })
    } else {
      // No tool content in this message
      if (!hasOnlyToolResults && nonToolContent.length === 0 && message.content.length > 0) {
        // This is a message with only non-tool, non-result content that we haven't handled
        if (currentGroup) {
          displayItems.push({ type: 'toolGroup', group: currentGroup })
          currentGroup = null
        }
        displayItems.push({ type: 'message', message })
      }
    }
  })

  // Flush any remaining group
  if (currentGroup) {
    displayItems.push({ type: 'toolGroup', group: currentGroup })
  }

  return displayItems
}
