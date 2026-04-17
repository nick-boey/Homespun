import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook } from '@testing-library/react'
import type { SessionEventEnvelope } from '@/types/session-events'

vi.mock('@/lib/session-event-log', () => ({
  sessionEventLog: vi.fn(),
}))

const stubState = () => ({
  lastSeenSeq: 0,
  messages: [],
  pendingQuestion: null,
  pendingPlan: null,
  toolCallIndex: {},
  systemInit: null,
  hookEvents: [],
  isRunning: false,
  lastError: null,
  unknownEvents: [],
  appliedEnvelopeKeys: new Set<string>(),
})

vi.mock('@/stores/session-events-store', () => ({
  useSessionEventsStore: Object.assign(
    (selector: (state: unknown) => unknown) =>
      selector({
        sessions: {},
        setState: vi.fn(),
        getState: (_id: string) => stubState(),
      }),
    {
      getState: () => ({
        getState: (_id: string) => stubState(),
      }),
    }
  ),
}))

const handlers = new Map<string, (...args: unknown[]) => void>()
const mockConnection = {
  on: vi.fn((event: string, h: (...args: unknown[]) => void) => {
    handlers.set(event, h)
  }),
  off: vi.fn(),
}

vi.mock('@/providers/signalr-provider', () => ({
  useClaudeCodeHub: () => ({ connection: mockConnection, isConnected: false }),
}))

vi.mock('@/api', () => ({
  client: { getConfig: () => ({ baseUrl: '' }) },
}))

import { useSessionEvents } from './use-session-events'
import { sessionEventLog } from '@/lib/session-event-log'

describe('useSessionEvents hop logging', () => {
  beforeEach(() => {
    vi.mocked(sessionEventLog).mockClear()
    handlers.clear()
    mockConnection.on.mockClear()
  })

  it('calls sessionEventLog once per envelope per hop (rx + reducer.apply)', () => {
    renderHook(() => useSessionEvents('S1'))

    const handler = handlers.get('ReceiveSessionEvent')
    expect(handler).toBeDefined()

    const envelope: SessionEventEnvelope = {
      seq: 1,
      sessionId: 'S1',
      eventId: 'e1',
      event: {
        type: 'TEXT_MESSAGE_CONTENT',
        messageId: 'M1',
        delta: 'hi',
        timestamp: 0,
      } as SessionEventEnvelope['event'],
    }

    handler!('S1', envelope)

    const calls = vi.mocked(sessionEventLog).mock.calls
    const rxCalls = calls.filter((c) => c[0] === 'client.signalr.rx')
    const applyCalls = calls.filter((c) => c[0] === 'client.reducer.apply')
    expect(rxCalls).toHaveLength(1)
    expect(applyCalls).toHaveLength(1)
    expect(rxCalls[0][1]).toMatchObject({
      SessionId: 'S1',
      EventId: 'e1',
      Seq: 1,
      AGUIType: 'TEXT_MESSAGE_CONTENT',
    })
    expect(applyCalls[0][1]).toMatchObject({
      SessionId: 'S1',
      EventId: 'e1',
      Seq: 1,
      AGUIType: 'TEXT_MESSAGE_CONTENT',
    })
  })

  it('drops duplicate envelopes after the first hop log', () => {
    renderHook(() => useSessionEvents('S1'))
    const handler = handlers.get('ReceiveSessionEvent')!

    const envelope: SessionEventEnvelope = {
      seq: 1,
      sessionId: 'S1',
      eventId: 'e1',
      event: {
        type: 'TEXT_MESSAGE_CONTENT',
        messageId: 'M1',
        delta: 'hi',
        timestamp: 0,
      } as SessionEventEnvelope['event'],
    }

    handler('S1', envelope)
    handler('S1', envelope)

    const calls = vi.mocked(sessionEventLog).mock.calls
    // Both receive calls log client.signalr.rx; only the first passes dedup so
    // only one client.reducer.apply is emitted.
    expect(calls.filter((c) => c[0] === 'client.signalr.rx')).toHaveLength(2)
    expect(calls.filter((c) => c[0] === 'client.reducer.apply')).toHaveLength(1)
  })
})
