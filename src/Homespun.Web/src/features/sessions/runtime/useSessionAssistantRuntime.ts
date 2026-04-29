import { useMemo } from 'react'
import {
  useExternalStoreRuntime,
  type AssistantRuntime,
  type AppendMessage,
} from '@assistant-ui/react'

import type { AGUISessionState, AGUIMessage } from '../utils/agui-reducer'

import { coalesceAssistantMessages } from './coalesceAssistantMessages'
import { convertAGUIMessage } from './convertAGUIMessage'

export interface UseSessionAssistantRuntimeOptions {
  state: AGUISessionState
  sendMessage: (text: string) => Promise<void> | void
  cancel?: () => Promise<void> | void
}

function extractText(message: AppendMessage): string {
  const parts = message.content
  if (typeof parts === 'string') return parts
  return parts
    .filter((p): p is { type: 'text'; text: string } => p.type === 'text')
    .map((p) => p.text)
    .join('')
}

export function useSessionAssistantRuntime(
  options: UseSessionAssistantRuntimeOptions
): AssistantRuntime {
  const { state, sendMessage, cancel } = options
  const messages = useMemo(() => coalesceAssistantMessages(state.messages), [state.messages])
  return useExternalStoreRuntime<AGUIMessage>({
    messages,
    isRunning: state.isRunning,
    convertMessage: convertAGUIMessage,
    onNew: async (message: AppendMessage) => {
      const text = extractText(message)
      await sendMessage(text)
    },
    onCancel: cancel
      ? async () => {
          await cancel()
        }
      : undefined,
    // Interactive tool renderers (ask_user_question, propose_plan) call
    // `addResult` from their Toolkit `render` when the user commits. The
    // external-store runtime otherwise throws "Runtime does not support tool
    // results". The renderers own the hub dispatch (AnswerQuestion /
    // ApprovePlan); the server-side synthesised `TOOL_CALL_RESULT` envelope
    // is what drives the reducer into receipt mode, so this callback just
    // needs to accept the call without throwing.
    onAddToolResult: () => {},
  })
}
