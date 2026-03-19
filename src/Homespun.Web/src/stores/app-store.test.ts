import { describe, it, expect, beforeEach } from 'vitest'
import { useAppStore } from './app-store'
import { ViewMode } from '@/features/issues/types'

describe('useAppStore', () => {
  beforeEach(() => {
    // Reset the store before each test
    useAppStore.setState({
      theme: 'system',
      sidebarOpen: true,
      issuesViewMode: ViewMode.Next,
    })
  })

  describe('issuesViewMode', () => {
    it('defaults to Next view mode', () => {
      const viewMode = useAppStore.getState().issuesViewMode
      expect(viewMode).toBe(ViewMode.Next)
    })

    it('setIssuesViewMode updates the view mode to Tree', () => {
      useAppStore.getState().setIssuesViewMode(ViewMode.Tree)

      const viewMode = useAppStore.getState().issuesViewMode
      expect(viewMode).toBe(ViewMode.Tree)
    })

    it('setIssuesViewMode updates the view mode to Next', () => {
      // First set to Tree
      useAppStore.getState().setIssuesViewMode(ViewMode.Tree)
      // Then set back to Next
      useAppStore.getState().setIssuesViewMode(ViewMode.Next)

      const viewMode = useAppStore.getState().issuesViewMode
      expect(viewMode).toBe(ViewMode.Next)
    })

    it('does not affect other state when updating view mode', () => {
      useAppStore.setState({ sidebarOpen: false })
      useAppStore.getState().setIssuesViewMode(ViewMode.Tree)

      const state = useAppStore.getState()
      expect(state.issuesViewMode).toBe(ViewMode.Tree)
      expect(state.sidebarOpen).toBe(false)
      expect(state.theme).toBe('system')
    })
  })

  describe('existing functionality', () => {
    it('setTheme updates theme', () => {
      useAppStore.getState().setTheme('dark')
      expect(useAppStore.getState().theme).toBe('dark')
    })

    it('toggleSidebar toggles sidebarOpen', () => {
      expect(useAppStore.getState().sidebarOpen).toBe(true)
      useAppStore.getState().toggleSidebar()
      expect(useAppStore.getState().sidebarOpen).toBe(false)
    })

    it('setSidebarOpen sets sidebar state', () => {
      useAppStore.getState().setSidebarOpen(false)
      expect(useAppStore.getState().sidebarOpen).toBe(false)
    })
  })
})
