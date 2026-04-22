import { createContext, useContext, type ReactNode } from 'react'

/**
 * Provides the current session id to descendants inside the `ChatSurface` tree.
 *
 * Interactive tool renderers (`ask_user_question`, `propose_plan`) need the
 * session id so they can dispatch user decisions through the `AnswerQuestion`
 * / `ApprovePlan` SignalR hub methods. The Toolkit render signature does not
 * expose routing state, so this context is the minimum-surface bridge.
 * Fixture-driven Storybook harnesses that don't hit a real hub simply omit
 * the provider — the renderer treats a missing session id as a read-only
 * preview.
 */
const SessionIdContext = createContext<string | null>(null)

export function SessionIdProvider({
  sessionId,
  children,
}: {
  sessionId: string | null
  children: ReactNode
}) {
  return <SessionIdContext.Provider value={sessionId}>{children}</SessionIdContext.Provider>
}

export function useSessionId(): string | null {
  return useContext(SessionIdContext)
}
