import { describe, it, expect, beforeEach } from 'vitest'
import { renderHook } from '@testing-library/react'
import { useSessionSettings } from './use-session-settings'
import { useSessionSettingsStore } from '@/stores/session-settings-store'
import type { ClaudeSession } from '@/types/signalr'

const createMockSession = (overrides: Partial<ClaudeSession> = {}): ClaudeSession => ({
  id: 'session-1',
  entityId: 'entity-1',
  projectId: 'project-1',
  workingDirectory: '/workdir',
  model: 'opus',
  mode: 'build',
  status: 'waitingForInput',
  createdAt: '2024-01-01T00:00:00Z',
  lastActivityAt: '2024-01-01T00:00:00Z',
  messages: [],
  totalCostUsd: 0,
  totalDurationMs: 0,
  hasPendingPlanApproval: false,
  contextClearMarkers: [],
  ...overrides,
})

describe('useSessionSettings', () => {
  beforeEach(() => {
    // Reset the store before each test
    useSessionSettingsStore.setState({ sessions: {} })
  })

  describe('when session data is available', () => {
    it('returns mode and model from session', () => {
      const session = createMockSession({ mode: 'plan', model: 'sonnet' })

      const { result } = renderHook(() => useSessionSettings('session-1', session))

      expect(result.current.mode).toBe('plan')
      expect(result.current.model).toBe('sonnet')
    })

    it('prefers session data over cached data', () => {
      // Set up cache with different values
      useSessionSettingsStore.getState().initSession('session-1', 'plan', 'haiku')

      const session = createMockSession({ mode: 'build', model: 'opus' })

      const { result } = renderHook(() => useSessionSettings('session-1', session))

      expect(result.current.mode).toBe('build')
      expect(result.current.model).toBe('opus')
    })
  })

  describe('when session data is null (not found)', () => {
    it('falls back to cached values', () => {
      useSessionSettingsStore.getState().initSession('session-1', 'plan', 'sonnet')

      const { result } = renderHook(() => useSessionSettings('session-1', null))

      expect(result.current.mode).toBe('plan')
      expect(result.current.model).toBe('sonnet')
    })

    it('falls back to defaults when no cache exists', () => {
      const { result } = renderHook(() => useSessionSettings('session-1', null))

      expect(result.current.mode).toBe('build')
      expect(result.current.model).toBe('opus')
    })
  })

  describe('when session data is undefined (loading)', () => {
    it('falls back to cached values', () => {
      useSessionSettingsStore.getState().initSession('session-1', 'plan', 'haiku')

      const { result } = renderHook(() => useSessionSettings('session-1', undefined))

      expect(result.current.mode).toBe('plan')
      expect(result.current.model).toBe('haiku')
    })

    it('falls back to defaults when no cache exists', () => {
      const { result } = renderHook(() => useSessionSettings('session-1', undefined))

      expect(result.current.mode).toBe('build')
      expect(result.current.model).toBe('opus')
    })
  })

  describe('reactive updates', () => {
    it('updates when session changes', () => {
      const session1 = createMockSession({ mode: 'build', model: 'opus' })
      const session2 = createMockSession({ mode: 'plan', model: 'sonnet' })

      const { result, rerender } = renderHook(
        ({ session }) => useSessionSettings('session-1', session),
        { initialProps: { session: session1 } }
      )

      expect(result.current.mode).toBe('build')
      expect(result.current.model).toBe('opus')

      rerender({ session: session2 })

      expect(result.current.mode).toBe('plan')
      expect(result.current.model).toBe('sonnet')
    })
  })
})
