import { useMemo } from 'react'
import { AssistantRuntimeProvider, ThreadPrimitive, Tools, useAui } from '@assistant-ui/react'

import type { AGUISessionState } from '@/features/sessions/utils/agui-reducer'

import { toolkit } from '@/features/sessions/runtime/toolkit'
import { SessionIdProvider } from '@/features/sessions/runtime/session-context'
import { useSessionAssistantRuntime } from '@/features/sessions/runtime/useSessionAssistantRuntime'

import { AssistantMessage, SystemMessage, UserMessage } from './messages'

export interface ChatSurfaceProps {
  state: AGUISessionState
  sendMessage: (text: string) => Promise<void> | void
  cancel?: () => Promise<void> | void
  composer?: React.ReactNode
  headerSlot?: React.ReactNode
  footerSlot?: React.ReactNode
  className?: string
  isLoading?: boolean
  /**
   * Session id threaded to interactive tool renderers
   * (`ask_user_question`, `propose_plan`). Fixture-driven stories can omit it.
   */
  sessionId?: string
}

export function ChatSurface({
  state,
  sendMessage,
  cancel,
  composer,
  headerSlot,
  footerSlot,
  className,
  isLoading,
  sessionId,
}: ChatSurfaceProps) {
  const runtime = useSessionAssistantRuntime({ state, sendMessage, cancel })
  const tools = useMemo(() => Tools({ toolkit }), [])
  const aui = useAui({ tools })

  return (
    <SessionIdProvider sessionId={sessionId ?? null}>
      <AssistantRuntimeProvider runtime={runtime} aui={aui}>
        <div className={className}>
          {headerSlot}
          <ThreadPrimitive.Root>
            <ThreadPrimitive.Viewport>
              {isLoading && state.messages.length === 0 ? (
                <div className="flex flex-1 items-center justify-center p-8">
                  <p className="text-muted-foreground">Loading…</p>
                </div>
              ) : (
                <div className="flex flex-col gap-4 p-4">
                  <ThreadPrimitive.Messages
                    components={{
                      UserMessage,
                      AssistantMessage,
                      SystemMessage,
                    }}
                  />
                </div>
              )}
            </ThreadPrimitive.Viewport>
            {footerSlot}
            {composer}
          </ThreadPrimitive.Root>
        </div>
      </AssistantRuntimeProvider>
    </SessionIdProvider>
  )
}
