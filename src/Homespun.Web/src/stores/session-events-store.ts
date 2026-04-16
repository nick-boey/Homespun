/**
 * Per-session scratch state for AG-UI envelope ingestion. Lives outside React so that
 * `lastSeenSeq` survives component unmount/remount within the same browser session — if
 * the user navigates away from a session view and back, we don't want to refetch events 0..N
 * again and replay them all through the reducer.
 *
 * Not persisted to localStorage on purpose: after a hard reload we want a clean slate,
 * because the server's event log is the only source of truth and `lastSeenSeq` means
 * nothing without the in-memory reducer state that accompanied it.
 */

import { create } from 'zustand'
import { devtools } from 'zustand/middleware'
import type { AGUISessionState } from '@/features/sessions/utils/agui-reducer'
import { initialAGUISessionState } from '@/features/sessions/utils/agui-reducer'

export interface SessionEventsStoreState {
  /**
   * Per-session reducer state. Indexed by sessionId so multi-session views (agent tree)
   * can keep each session's render state hot without collisions.
   */
  sessions: Record<string, AGUISessionState>

  /** Replace the full state for a session. Typically called by the reducer hook. */
  setState: (sessionId: string, next: AGUISessionState) => void

  /** Get the current state for a session, or the initial state if none exists. */
  getState: (sessionId: string) => AGUISessionState

  /** Drop a session's state (e.g. after context-clear / session stop). */
  clearSession: (sessionId: string) => void
}

export const useSessionEventsStore = create<SessionEventsStoreState>()(
  devtools(
    (set, get) => ({
      sessions: {},

      setState: (sessionId, next) =>
        set(
          (state) => ({ sessions: { ...state.sessions, [sessionId]: next } }),
          undefined,
          `setState/${sessionId}`
        ),

      getState: (sessionId) => get().sessions[sessionId] ?? initialAGUISessionState,

      clearSession: (sessionId) =>
        set(
          (state) => {
            const next = { ...state.sessions }
            delete next[sessionId]
            return { sessions: next }
          },
          undefined,
          `clearSession/${sessionId}`
        ),
    }),
    { name: 'session-events-store' }
  )
)
