import { describe, it, expect, beforeEach } from 'vitest'
import { useSessionSettingsStore } from './session-settings-store'

describe('useSessionSettingsStore', () => {
  beforeEach(() => {
    // Reset the store before each test
    useSessionSettingsStore.setState({ sessions: {} })
  })

  describe('initSession', () => {
    it('creates a new session entry with mode and model', () => {
      useSessionSettingsStore.getState().initSession('session-1', 'Build', 'opus')

      const session = useSessionSettingsStore.getState().getSession('session-1')
      expect(session).toEqual({ mode: 'Build', model: 'opus' })
    })

    it('overwrites existing session entry', () => {
      useSessionSettingsStore.getState().initSession('session-1', 'Build', 'opus')
      useSessionSettingsStore.getState().initSession('session-1', 'Plan', 'sonnet')

      const session = useSessionSettingsStore.getState().getSession('session-1')
      expect(session).toEqual({ mode: 'Plan', model: 'sonnet' })
    })

    it('does not affect other sessions', () => {
      useSessionSettingsStore.getState().initSession('session-1', 'Build', 'opus')
      useSessionSettingsStore.getState().initSession('session-2', 'Plan', 'haiku')

      const session1 = useSessionSettingsStore.getState().getSession('session-1')
      const session2 = useSessionSettingsStore.getState().getSession('session-2')

      expect(session1).toEqual({ mode: 'Build', model: 'opus' })
      expect(session2).toEqual({ mode: 'Plan', model: 'haiku' })
    })
  })

  describe('updateSession', () => {
    it('updates existing session entry', () => {
      useSessionSettingsStore.getState().initSession('session-1', 'Build', 'opus')
      useSessionSettingsStore.getState().updateSession('session-1', 'Plan', 'sonnet')

      const session = useSessionSettingsStore.getState().getSession('session-1')
      expect(session).toEqual({ mode: 'Plan', model: 'sonnet' })
    })

    it('creates entry if session does not exist', () => {
      useSessionSettingsStore.getState().updateSession('session-1', 'Plan', 'haiku')

      const session = useSessionSettingsStore.getState().getSession('session-1')
      expect(session).toEqual({ mode: 'Plan', model: 'haiku' })
    })

    it('does not affect other sessions', () => {
      useSessionSettingsStore.getState().initSession('session-1', 'Build', 'opus')
      useSessionSettingsStore.getState().initSession('session-2', 'Build', 'opus')
      useSessionSettingsStore.getState().updateSession('session-1', 'Plan', 'sonnet')

      const session1 = useSessionSettingsStore.getState().getSession('session-1')
      const session2 = useSessionSettingsStore.getState().getSession('session-2')

      expect(session1).toEqual({ mode: 'Plan', model: 'sonnet' })
      expect(session2).toEqual({ mode: 'Build', model: 'opus' })
    })
  })

  describe('getSession', () => {
    it('returns undefined for non-existent session', () => {
      const session = useSessionSettingsStore.getState().getSession('non-existent')
      expect(session).toBeUndefined()
    })

    it('returns session settings for existing session', () => {
      useSessionSettingsStore.getState().initSession('session-1', 'Build', 'opus')

      const session = useSessionSettingsStore.getState().getSession('session-1')
      expect(session).toEqual({ mode: 'Build', model: 'opus' })
    })
  })

  describe('removeSession', () => {
    it('removes session from store', () => {
      useSessionSettingsStore.getState().initSession('session-1', 'Build', 'opus')
      useSessionSettingsStore.getState().removeSession('session-1')

      const session = useSessionSettingsStore.getState().getSession('session-1')
      expect(session).toBeUndefined()
    })

    it('does nothing for non-existent session', () => {
      useSessionSettingsStore.getState().initSession('session-1', 'Build', 'opus')
      useSessionSettingsStore.getState().removeSession('non-existent')

      const session = useSessionSettingsStore.getState().getSession('session-1')
      expect(session).toEqual({ mode: 'Build', model: 'opus' })
    })

    it('does not affect other sessions', () => {
      useSessionSettingsStore.getState().initSession('session-1', 'Build', 'opus')
      useSessionSettingsStore.getState().initSession('session-2', 'Plan', 'sonnet')
      useSessionSettingsStore.getState().removeSession('session-1')

      const session1 = useSessionSettingsStore.getState().getSession('session-1')
      const session2 = useSessionSettingsStore.getState().getSession('session-2')

      expect(session1).toBeUndefined()
      expect(session2).toEqual({ mode: 'Plan', model: 'sonnet' })
    })
  })

  describe('react hook usage', () => {
    it('can select specific session from store', () => {
      useSessionSettingsStore.getState().initSession('session-1', 'Build', 'opus')

      // Simulating useSessionSettingsStore((s) => s.sessions['session-1'])
      const selected = useSessionSettingsStore.getState().sessions['session-1']
      expect(selected).toEqual({ mode: 'Build', model: 'opus' })
    })
  })
})
