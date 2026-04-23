import type { ThreadMessageLike } from '@assistant-ui/react'

import type { AGUIContentBlock, AGUIMessage } from '../utils/agui-reducer'

type ContentPart = ThreadMessageLike['content'] extends string | readonly (infer P)[] ? P : never

function convertBlock(block: AGUIContentBlock): ContentPart {
  switch (block.kind) {
    case 'text':
      return { type: 'text', text: block.text }
    case 'thinking':
      return { type: 'reasoning', text: block.text }
    case 'toolUse':
      return {
        type: 'tool-call',
        toolCallId: block.toolCallId,
        toolName: block.toolName,
        argsText: block.input,
        result: block.result,
      }
  }
}

export function convertAGUIMessage(message: AGUIMessage): ThreadMessageLike {
  const role: 'user' | 'assistant' | 'system' = message.role === 'tool' ? 'assistant' : message.role
  return {
    id: message.id,
    role,
    content: message.content.map(convertBlock),
  }
}
