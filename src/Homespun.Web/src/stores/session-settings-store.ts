import { create } from 'zustand'
import { devtools } from 'zustand/middleware'
import type { SessionMode } from '@/types/signalr'

export type ModelSelection = 'opus' | 'sonnet' | 'haiku'

export interface SessionSettings {
  mode: SessionMode
  model: ModelSelection
}

interface SessionSettingsState {
  // Map of sessionId -> settings
  sessions: Record<string, SessionSettings>

  // Initialize settings for a new session (called when agent starts)
  initSession: (sessionId: string, mode: SessionMode, model: ModelSelection) => void

  // Update settings from server data
  updateSession: (sessionId: string, mode: SessionMode, model: string) => void

  // Get settings for a session (returns undefined if not cached)
  getSession: (sessionId: string) => SessionSettings | undefined

  // Remove session from cache (when session ends)
  removeSession: (sessionId: string) => void
}

export const useSessionSettingsStore = create<SessionSettingsState>()(
  devtools(
    (set, get) => ({
      sessions: {},

      initSession: (sessionId, mode, model) =>
        set(
          (state) => ({
            sessions: { ...state.sessions, [sessionId]: { mode, model } },
          }),
          undefined,
          'initSession'
        ),

      updateSession: (sessionId, mode, model) =>
        set(
          (state) => ({
            sessions: {
              ...state.sessions,
              [sessionId]: { mode, model: model as ModelSelection },
            },
          }),
          undefined,
          'updateSession'
        ),

      getSession: (sessionId) => get().sessions[sessionId],

      removeSession: (sessionId) =>
        set(
          (state) => {
            // eslint-disable-next-line @typescript-eslint/no-unused-vars
            const { [sessionId]: _, ...rest } = state.sessions
            return { sessions: rest }
          },
          undefined,
          'removeSession'
        ),
    }),
    { name: 'session-settings-store' }
  )
)
