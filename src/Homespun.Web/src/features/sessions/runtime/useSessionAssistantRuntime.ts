import {
  useExternalStoreRuntime,
  type AssistantRuntime,
  type AppendMessage,
} from '@assistant-ui/react'

import type { AGUISessionState, AGUIMessage } from '../utils/agui-reducer'

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
  return useExternalStoreRuntime<AGUIMessage>({
    messages: state.messages,
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
  })
}
