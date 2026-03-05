import { create } from 'zustand'
import { devtools, persist } from 'zustand/middleware'
import type { SessionMode } from '@/types/signalr'

export type ModelSelection = 'opus' | 'sonnet' | 'haiku'

interface ChatInputState {
  sessionMode: SessionMode
  model: ModelSelection
  setSessionMode: (mode: SessionMode) => void
  setModel: (model: ModelSelection) => void
}

export const useChatInputStore = create<ChatInputState>()(
  devtools(
    persist(
      (set) => ({
        sessionMode: 'Build' as SessionMode,
        model: 'opus',
        setSessionMode: (mode) => set({ sessionMode: mode }),
        setModel: (model) => set({ model }),
      }),
      {
        name: 'homespun-chat-input-storage',
      }
    )
  )
)
