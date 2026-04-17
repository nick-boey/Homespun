import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import {
  SessionEventLogBatcher,
  type BatcherDeps,
  type SessionEventLogEntry,
  FLUSH_SIZE,
  FLUSH_AGE_MS,
  ENDPOINT,
} from './session-event-log'

interface FakeDeps extends BatcherDeps {
  flushTimers: () => void
  advance(ms: number): void
  fetchCalls: Array<{ url: string; init: RequestInit }>
  warnCalls: string[]
  beaconCalls: Array<{ url: string; body: Blob }>
}

function makeFakeDeps(
  responder: (url: string, init: RequestInit) => Promise<Response> = async () =>
    new Response(null, { status: 202 }),
  sendBeaconResponder: ((url: string, body: Blob) => boolean) | null = () => true
): FakeDeps {
  let now = 0
  const timers = new Map<number, { fn: () => void; dueAt: number }>()
  let nextHandle = 1

  const deps: FakeDeps = {
    now: () => now,
    setTimeout: (fn, ms) => {
      const handle = nextHandle++
      timers.set(handle, { fn, dueAt: now + ms })
      return handle as unknown as ReturnType<typeof globalThis.setTimeout>
    },
    clearTimeout: (h) => {
      timers.delete(h as unknown as number)
    },
    fetch: async (url, init) => {
      deps.fetchCalls.push({ url, init })
      return responder(url, init)
    },
    sendBeacon: sendBeaconResponder
      ? (url, body) => {
          deps.beaconCalls.push({ url, body })
          return sendBeaconResponder(url, body)
        }
      : undefined,
    warn: (m) => {
      deps.warnCalls.push(m)
    },
    advance: (ms) => {
      now += ms
      for (const [h, t] of [...timers.entries()]) {
        if (t.dueAt <= now) {
          timers.delete(h)
          t.fn()
        }
      }
    },
    flushTimers: () => {
      for (const [h, t] of [...timers.entries()]) {
        timers.delete(h)
        t.fn()
      }
    },
    fetchCalls: [],
    warnCalls: [],
    beaconCalls: [],
  }
  return deps
}

function entry(overrides: Partial<SessionEventLogEntry> = {}): SessionEventLogEntry {
  return {
    Timestamp: '2026-04-17T10:00:00.000Z',
    Level: 'Information',
    Message: 'x',
    Hop: 'client.signalr.rx',
    SessionId: 'S1',
    ...overrides,
  }
}

describe('SessionEventLogBatcher', () => {
  let deps: FakeDeps
  let batcher: SessionEventLogBatcher

  beforeEach(() => {
    deps = makeFakeDeps()
    batcher = new SessionEventLogBatcher(deps)
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('flushes when buffer reaches FLUSH_SIZE entries', async () => {
    for (let i = 0; i < FLUSH_SIZE; i++) {
      batcher.enqueue(entry({ Seq: i }))
    }
    // Flush scheduled synchronously; await the fetch promise to resolve.
    await Promise.resolve()
    await Promise.resolve()
    expect(deps.fetchCalls).toHaveLength(1)
    const body = JSON.parse(deps.fetchCalls[0].init.body as string)
    expect(body).toHaveLength(FLUSH_SIZE)
  })

  it('flushes after FLUSH_AGE_MS when buffer is under threshold', async () => {
    batcher.enqueue(entry())
    expect(deps.fetchCalls).toHaveLength(0)
    deps.advance(FLUSH_AGE_MS)
    await Promise.resolve()
    await Promise.resolve()
    expect(deps.fetchCalls).toHaveLength(1)
  })

  it('posts to ENDPOINT with JSON content type', async () => {
    batcher.enqueue(entry())
    await batcher.flush()
    expect(deps.fetchCalls[0].url).toBe(ENDPOINT)
    expect((deps.fetchCalls[0].init.headers as Record<string, string>)['Content-Type']).toBe(
      'application/json'
    )
  })

  it('does not cascade on HTTP 500: drops batch and warns once', async () => {
    deps = makeFakeDeps(async () => new Response(null, { status: 500 }))
    batcher = new SessionEventLogBatcher(deps)

    batcher.enqueue(entry())
    await batcher.flush()
    batcher.enqueue(entry())
    await batcher.flush()

    // Two fetch attempts, single warn (deduped).
    expect(deps.fetchCalls).toHaveLength(2)
    expect(deps.warnCalls).toHaveLength(1)
  })

  it('does not cascade on fetch rejection', async () => {
    deps = makeFakeDeps(async () => {
      throw new Error('network down')
    })
    batcher = new SessionEventLogBatcher(deps)

    batcher.enqueue(entry())
    await batcher.flush()
    expect(deps.warnCalls).toHaveLength(1)
  })

  it('resets warn-dedup after a successful flush', async () => {
    let failNext = true
    deps = makeFakeDeps(async () =>
      failNext ? new Response(null, { status: 500 }) : new Response(null, { status: 202 })
    )
    batcher = new SessionEventLogBatcher(deps)

    batcher.enqueue(entry())
    await batcher.flush() // fails → warn
    failNext = false
    batcher.enqueue(entry())
    await batcher.flush() // succeeds
    failNext = true
    batcher.enqueue(entry())
    await batcher.flush() // fails → new warn

    expect(deps.warnCalls).toHaveLength(2)
  })

  it('flushBeacon uses sendBeacon when available', () => {
    batcher.enqueue(entry())
    batcher.enqueue(entry())
    batcher.flushBeacon()
    expect(deps.beaconCalls).toHaveLength(1)
    expect(deps.beaconCalls[0].url).toBe(ENDPOINT)
    expect(deps.fetchCalls).toHaveLength(0)
  })

  it('flushBeacon is a no-op when sendBeacon is unavailable', () => {
    deps = makeFakeDeps(async () => new Response(null, { status: 202 }), null)
    batcher = new SessionEventLogBatcher(deps)

    batcher.enqueue(entry())
    batcher.flushBeacon()
    expect(deps.beaconCalls).toHaveLength(0)
  })
})
