import { create } from 'zustand'
import { devtools, persist } from 'zustand/middleware'
import { ViewMode } from '@/features/issues/types'

interface AppState {
  theme: 'light' | 'dark' | 'system'
  sidebarOpen: boolean
  issuesViewMode: ViewMode
  setTheme: (theme: 'light' | 'dark' | 'system') => void
  toggleSidebar: () => void
  setSidebarOpen: (open: boolean) => void
  setIssuesViewMode: (mode: ViewMode) => void
}

export const useAppStore = create<AppState>()(
  devtools(
    persist(
      (set) => ({
        theme: 'system',
        sidebarOpen: true,
        issuesViewMode: ViewMode.Next,
        setTheme: (theme) => set({ theme }),
        toggleSidebar: () => set((state) => ({ sidebarOpen: !state.sidebarOpen })),
        setSidebarOpen: (open) => set({ sidebarOpen: open }),
        setIssuesViewMode: (mode) => set({ issuesViewMode: mode }),
      }),
      {
        name: 'homespun-app-storage',
      }
    )
  )
)
