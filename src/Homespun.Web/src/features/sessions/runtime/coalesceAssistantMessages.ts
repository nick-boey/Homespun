import type { AGUIMessage } from '../utils/agui-reducer'

/**
 * Merge consecutive assistant `AGUIMessage`s into a single message for rendering.
 *
 * The reducer keeps one `AGUIMessage` per SDK message so diagnostics retain
 * boundaries. AUI's `ToolGroup` primitive only groups consecutive `tool-call`
 * parts within a single message, so when Claude emits each tool call in its
 * own assistant message (the typical sequential case), the UI ends up with N
 * single-tool groups instead of one group of N tools.
 *
 * Coalescing here is presentation-only: the merged message keeps the first
 * source message's `id` and `createdAt`; any user/system message between two
 * assistant messages breaks the chain, so distinct user turns still render as
 * separate assistant messages.
 */
export function coalesceAssistantMessages(messages: AGUIMessage[]): AGUIMessage[] {
  const out: AGUIMessage[] = []
  for (const m of messages) {
    const prev = out[out.length - 1]
    if (prev && prev.role === 'assistant' && m.role === 'assistant') {
      out[out.length - 1] = {
        ...prev,
        content: [...prev.content, ...m.content],
      }
    } else {
      out.push(m)
    }
  }
  return out
}
