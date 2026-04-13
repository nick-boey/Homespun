import { describe, it, expect, beforeEach } from 'vitest'
import { useAppStore } from './app-store'

describe('useAppStore', () => {
  beforeEach(() => {
    // Reset the store before each test
    useAppStore.setState({
      theme: 'system',
      sidebarOpen: true,
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
