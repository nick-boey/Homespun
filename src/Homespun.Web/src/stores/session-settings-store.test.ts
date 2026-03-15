import { describe, it, expect, beforeEach } from 'vitest'
import { useSessionSettingsStore } from './session-settings-store'

describe('useSessionSettingsStore', () => {
  beforeEach(() => {
    // Reset the store before each test
    useSessionSettingsStore.setState({ sessions: {} })
  })

  describe('initSession', () => {
    it('creates a new session entry with mode and model', () => {
      useSessionSettingsStore.getState().initSession('session-1', 'build', 'opus')

      const session = useSessionSettingsStore.getState().getSession('session-1')
      expect(session).toEqual({ mode: 'build', model: 'opus' })
    })

    it('overwrites existing session entry', () => {
      useSessionSettingsStore.getState().initSession('session-1', 'build', 'opus')
      useSessionSettingsStore.getState().initSession('session-1', 'plan', 'sonnet')

      const session = useSessionSettingsStore.getState().getSession('session-1')
      expect(session).toEqual({ mode: 'plan', model: 'sonnet' })
    })

    it('does not affect other sessions', () => {
      useSessionSettingsStore.getState().initSession('session-1', 'build', 'opus')
      useSessionSettingsStore.getState().initSession('session-2', 'plan', 'haiku')

      const session1 = useSessionSettingsStore.getState().getSession('session-1')
      const session2 = useSessionSettingsStore.getState().getSession('session-2')

      expect(session1).toEqual({ mode: 'build', model: 'opus' })
      expect(session2).toEqual({ mode: 'plan', model: 'haiku' })
    })
  })

  describe('updateSession', () => {
    it('updates existing session entry', () => {
      useSessionSettingsStore.getState().initSession('session-1', 'build', 'opus')
      useSessionSettingsStore.getState().updateSession('session-1', 'plan', 'sonnet')

      const session = useSessionSettingsStore.getState().getSession('session-1')
      expect(session).toEqual({ mode: 'plan', model: 'sonnet' })
    })

    it('creates entry if session does not exist', () => {
      useSessionSettingsStore.getState().updateSession('session-1', 'plan', 'haiku')

      const session = useSessionSettingsStore.getState().getSession('session-1')
      expect(session).toEqual({ mode: 'plan', model: 'haiku' })
    })

    it('does not affect other sessions', () => {
      useSessionSettingsStore.getState().initSession('session-1', 'build', 'opus')
      useSessionSettingsStore.getState().initSession('session-2', 'build', 'opus')
      useSessionSettingsStore.getState().updateSession('session-1', 'plan', 'sonnet')

      const session1 = useSessionSettingsStore.getState().getSession('session-1')
      const session2 = useSessionSettingsStore.getState().getSession('session-2')

      expect(session1).toEqual({ mode: 'plan', model: 'sonnet' })
      expect(session2).toEqual({ mode: 'build', model: 'opus' })
    })
  })

  describe('getSession', () => {
    it('returns undefined for non-existent session', () => {
      const session = useSessionSettingsStore.getState().getSession('non-existent')
      expect(session).toBeUndefined()
    })

    it('returns session settings for existing session', () => {
      useSessionSettingsStore.getState().initSession('session-1', 'build', 'opus')

      const session = useSessionSettingsStore.getState().getSession('session-1')
      expect(session).toEqual({ mode: 'build', model: 'opus' })
    })
  })

  describe('removeSession', () => {
    it('removes session from store', () => {
      useSessionSettingsStore.getState().initSession('session-1', 'build', 'opus')
      useSessionSettingsStore.getState().removeSession('session-1')

      const session = useSessionSettingsStore.getState().getSession('session-1')
      expect(session).toBeUndefined()
    })

    it('does nothing for non-existent session', () => {
      useSessionSettingsStore.getState().initSession('session-1', 'build', 'opus')
      useSessionSettingsStore.getState().removeSession('non-existent')

      const session = useSessionSettingsStore.getState().getSession('session-1')
      expect(session).toEqual({ mode: 'build', model: 'opus' })
    })

    it('does not affect other sessions', () => {
      useSessionSettingsStore.getState().initSession('session-1', 'build', 'opus')
      useSessionSettingsStore.getState().initSession('session-2', 'plan', 'sonnet')
      useSessionSettingsStore.getState().removeSession('session-1')

      const session1 = useSessionSettingsStore.getState().getSession('session-1')
      const session2 = useSessionSettingsStore.getState().getSession('session-2')

      expect(session1).toBeUndefined()
      expect(session2).toEqual({ mode: 'plan', model: 'sonnet' })
    })
  })

  describe('react hook usage', () => {
    it('can select specific session from store', () => {
      useSessionSettingsStore.getState().initSession('session-1', 'build', 'opus')

      // Simulating useSessionSettingsStore((s) => s.sessions['session-1'])
      const selected = useSessionSettingsStore.getState().sessions['session-1']
      expect(selected).toEqual({ mode: 'build', model: 'opus' })
    })
  })
})
