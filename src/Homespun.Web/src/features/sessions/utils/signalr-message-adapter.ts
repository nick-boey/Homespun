import type {
  ClaudeMessage as SignalRMessage,
  ClaudeMessageContent as SignalRContent,
} from '@/types/signalr'
import type { ClaudeMessage, ClaudeMessageContent } from '@/types/tool-execution'

/**
 * Converts a SignalR message content to our tool execution content format
 */
function convertContent(content: SignalRContent): ClaudeMessageContent {
  // Map the type string to our expected format
  let contentType: ClaudeMessageContent['contentType']

  switch (content.type) {
    case 'Text':
      contentType = 'text'
      break
    case 'ToolUse':
      contentType = 'tool_use'
      break
    case 'ToolResult':
      contentType = 'tool_result'
      break
    case 'Thinking':
      contentType = 'thinking'
      break
    default:
      // Default to text for unknown types
      contentType = 'text'
  }

  const base: ClaudeMessageContent = {
    contentType,
    toolUseId: content.toolUseId,
  }

  // Map specific properties based on content type
  switch (contentType) {
    case 'text':
      base.text = content.text ?? ''
      break
    case 'thinking':
      base.text = content.thinking ?? ''
      break
    case 'tool_use':
      base.name = content.toolName ?? ''
      base.input = content.toolInput ? JSON.parse(content.toolInput) : {}
      base.toolUseId = content.toolUseId
      // If toolResult exists, it means the tool has completed
      if (content.toolResult !== undefined) {
        base.toolResult = content.toolResult
        base.isError = content.toolSuccess === false
      }
      break
    case 'tool_result':
      base.content = content.toolResult ?? ''
      base.isError = content.toolSuccess === false
      break
  }

  return base
}

/**
 * Converts a SignalR message to our tool execution message format
 */
export function convertSignalRMessage(message: SignalRMessage): ClaudeMessage {
  // Map role - SignalR uses "User"/"Assistant", we need numeric values
  const roleMap: Record<string, number> = {
    User: 0,
    Assistant: 1,
  }

  return {
    id: message.id,
    role: (roleMap[message.role] ?? 0) as 0 | 1,
    content: message.content.map(convertContent),
    createdAt: message.createdAt,
  }
}

/**
 * Converts an array of SignalR messages to our format
 */
export function convertSignalRMessages(messages: SignalRMessage[]): ClaudeMessage[] {
  return messages.map(convertSignalRMessage)
}
