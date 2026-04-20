import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { render } from '@testing-library/react'
import { trace, type Tracer } from '@opentelemetry/api'
import { SignalRProvider } from './signalr-provider'

// ---------------------------------------------------------------------------
// Mock @/lib/signalr/connection so nothing opens a real WebSocket. We keep
// the onStatusChange callback so the provider's transition handler runs and
// we can assert the span-event shape.
// ---------------------------------------------------------------------------
type StatusHandler = (status: string, error?: string) => void

const statusHandlers: StatusHandler[] = []

vi.mock('@/lib/signalr/connection', () => ({
  createHubConnection: vi.fn((opts: { onStatusChange?: StatusHandler }) => {
    if (opts.onStatusChange) statusHandlers.push(opts.onStatusChange)
    return { fake: true }
  }),
  startConnection: vi.fn(),
  stopConnection: vi.fn(),
}))

vi.mock('@/lib/signalr/claude-code-hub', () => ({
  createClaudeCodeHubMethods: vi.fn(() => ({})),
}))

vi.mock('@/lib/signalr/notification-hub', () => ({
  createNotificationHubMethods: vi.fn(() => ({})),
}))

describe('SignalRProvider — homespun.signalr.client.connect span', () => {
  const spans: Array<{ name: string; events: Array<{ name: string }>; ended: boolean }> = []
  let tracer: Tracer

  beforeEach(() => {
    spans.length = 0
    statusHandlers.length = 0

    const fakeTracer: Tracer = {
      startSpan: (name: string) => {
        const record = { name, events: [] as Array<{ name: string }>, ended: false }
        spans.push(record)
        return {
          setAttribute: () => {},
          setAttributes: () => {},
          addEvent: (evName: string) => {
            record.events.push({ name: evName })
          },
          setStatus: () => {},
          updateName: () => {},
          end: () => {
            record.ended = true
          },
          isRecording: () => true,
          recordException: () => {},
          spanContext: () => ({ traceId: '0', spanId: '0', traceFlags: 0 }),
        } as never
      },
      startActiveSpan: () => {
        throw new Error('not used in this test')
      },
    } as unknown as Tracer
    tracer = fakeTracer
    vi.spyOn(trace, 'getTracer').mockReturnValue(tracer)
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('starts exactly one long-lived span on mount and ends it on unmount', () => {
    const { unmount } = render(
      <SignalRProvider autoConnect={false}>
        <div data-testid="child">x</div>
      </SignalRProvider>
    )

    // Exactly one client connect span started.
    const connectSpans = spans.filter((s) => s.name === 'homespun.signalr.client.connect')
    expect(connectSpans).toHaveLength(1)
    const span = connectSpans[0]
    expect(span.ended).toBe(false)

    // Drive a connect → reconnect → disconnect sequence through the status callback.
    const onStatusChange = statusHandlers[0]
    expect(onStatusChange).toBeDefined()
    onStatusChange!('connected')
    onStatusChange!('reconnecting', 'flaky network')
    onStatusChange!('connected')
    onStatusChange!('disconnected')

    const eventNames = span.events.map((e) => e.name)
    expect(eventNames).toContain('signalr.connected')
    expect(eventNames).toContain('signalr.reconnecting')
    expect(eventNames).toContain('signalr.disconnected')

    unmount()

    expect(span.ended).toBe(true)
    expect(span.events.map((e) => e.name)).toContain('signalr.teardown')
  })
})
