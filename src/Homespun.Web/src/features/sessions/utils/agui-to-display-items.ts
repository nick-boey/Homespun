/**
 * Converts AG-UI reducer messages into the legacy {@link MessageDisplayItem} shape
 * consumed by {@link MessageList} / {@link ToolExecutionGroupDisplay} /
 * {@link ToolExecutionRow}. Lets the render tree keep rich tool-call cards while the
 * underlying data flows through the A2A-native event pipeline.
 *
 * Each AGUIMessage is partitioned into (a) text/thinking blocks rendered as a chat
 * bubble and (b) toolUse blocks rendered as a grouped tool-execution card. Tool
 * results are already folded into `AGUIToolUseBlock.result` by the reducer, so the
 * grouper does not need to cross-reference separate tool-result messages.
 */

import type {
  ClaudeMessage,
  ClaudeMessageContent,
  MessageDisplayItem,
  ToolExecution,
  ToolExecutionGroup,
} from '@/types/tool-execution'
import type { AGUIContentBlock, AGUIMessage, AGUIToolUseBlock } from './agui-reducer'

export function aguiMessagesToDisplayItems(messages: AGUIMessage[]): MessageDisplayItem[] {
  const items: MessageDisplayItem[] = []

  for (const msg of messages) {
    const textAndThinking = msg.content.filter(
      (b): b is Exclude<AGUIContentBlock, AGUIToolUseBlock> => b.kind !== 'toolUse'
    )
    const toolUses = msg.content.filter((b): b is AGUIToolUseBlock => b.kind === 'toolUse')

    if (textAndThinking.length > 0) {
      items.push({
        type: 'message',
        message: toDisplayMessage(msg, textAndThinking),
      })
    }

    if (toolUses.length > 0) {
      items.push({
        type: 'toolGroup',
        group: toToolGroup(msg, toolUses),
      })
    }
  }

  return items
}

function toDisplayMessage(
  msg: AGUIMessage,
  blocks: Array<Exclude<AGUIContentBlock, AGUIToolUseBlock>>
): ClaudeMessage {
  const content: ClaudeMessageContent[] = blocks.map((b) =>
    b.kind === 'text'
      ? { contentType: 'text', text: b.text }
      : { contentType: 'thinking', text: b.text }
  )
  return {
    id: msg.id,
    role: msg.role === 'user' ? 'User' : 'Assistant',
    content,
    createdAt: new Date(msg.createdAt).toISOString(),
  }
}

function toToolGroup(msg: AGUIMessage, toolUses: AGUIToolUseBlock[]): ToolExecutionGroup {
  const executions: ToolExecution[] = toolUses.map((tu) => {
    const parsedInput = tryParseInput(tu.input)
    const toolResult: ClaudeMessageContent | undefined =
      tu.result !== undefined
        ? {
            contentType: 'tool_result',
            toolUseId: tu.toolCallId,
            content: tu.result,
          }
        : undefined
    return {
      toolUse: {
        contentType: 'tool_use',
        toolUseId: tu.toolCallId,
        name: tu.toolName,
        input: parsedInput,
      },
      toolResult,
      isRunning: tu.isStreaming,
    }
  })

  return {
    id: `group-${msg.id}`,
    executions,
    timestamp: new Date(msg.createdAt).toISOString(),
    originalMessageIds: [msg.id],
  }
}

function tryParseInput(raw: string): Record<string, unknown> {
  if (!raw) return {}
  try {
    const parsed = JSON.parse(raw)
    if (parsed && typeof parsed === 'object' && !Array.isArray(parsed)) {
      return parsed as Record<string, unknown>
    }
  } catch {
    // Stream may have partial JSON mid-turn — ignore until complete.
  }
  return {}
}
