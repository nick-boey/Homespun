import { create } from 'zustand'
import { devtools, persist } from 'zustand/middleware'

export type PermissionMode = 'default' | 'bypass' | 'accept-edits' | 'plan'

export type ModelSelection = 'opus' | 'sonnet' | 'haiku'

interface ChatInputState {
  permissionMode: PermissionMode
  model: ModelSelection
  setPermissionMode: (mode: PermissionMode) => void
  setModel: (model: ModelSelection) => void
}

export const useChatInputStore = create<ChatInputState>()(
  devtools(
    persist(
      (set) => ({
        permissionMode: 'default',
        model: 'opus',
        setPermissionMode: (mode) => set({ permissionMode: mode }),
        setModel: (model) => set({ model }),
      }),
      {
        name: 'homespun-chat-input-storage',
      }
    )
  )
)
