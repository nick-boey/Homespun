/**
 * Tests for the Zustand session-events store.
 * Covers task 8.4 of the a2a-native-messaging OpenSpec change: lastSeenSeq + reducer
 * state survives component unmount/remount.
 */

import { afterEach, describe, expect, it } from 'vitest'
import { useSessionEventsStore } from './session-events-store'
import { initialAGUISessionState } from '@/features/sessions/utils/agui-reducer'

afterEach(() => {
  // Clear any state accumulated across tests so each test starts clean.
  const snapshot = useSessionEventsStore.getState()
  for (const sessionId of Object.keys(snapshot.sessions)) {
    snapshot.clearSession(sessionId)
  }
})

describe('useSessionEventsStore', () => {
  it('returns the initial state for an unknown session', () => {
    const state = useSessionEventsStore.getState().getState('never-seen')
    expect(state).toEqual(initialAGUISessionState)
  })

  it('persists per-session state across getState calls (unmount/remount surrogate)', () => {
    const store = useSessionEventsStore.getState()
    store.setState('s1', {
      ...initialAGUISessionState,
      lastSeenSeq: 42,
    })

    // A fresh getState call simulates the hook re-reading after unmount/remount.
    const read = useSessionEventsStore.getState().getState('s1')
    expect(read.lastSeenSeq).toBe(42)
  })

  it('isolates state across sessions', () => {
    const store = useSessionEventsStore.getState()
    store.setState('s1', { ...initialAGUISessionState, lastSeenSeq: 5 })
    store.setState('s2', { ...initialAGUISessionState, lastSeenSeq: 99 })

    expect(useSessionEventsStore.getState().getState('s1').lastSeenSeq).toBe(5)
    expect(useSessionEventsStore.getState().getState('s2').lastSeenSeq).toBe(99)
  })

  it('clearSession drops a session but leaves others intact', () => {
    const store = useSessionEventsStore.getState()
    store.setState('s1', { ...initialAGUISessionState, lastSeenSeq: 5 })
    store.setState('s2', { ...initialAGUISessionState, lastSeenSeq: 99 })

    store.clearSession('s1')

    expect(useSessionEventsStore.getState().getState('s1')).toEqual(initialAGUISessionState)
    expect(useSessionEventsStore.getState().getState('s2').lastSeenSeq).toBe(99)
  })
})
