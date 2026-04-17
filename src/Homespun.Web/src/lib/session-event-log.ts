/**
 * Client-side batcher for session event pipeline logs.
 *
 * Buffers {@link SessionEventLogEntry} entries and flushes them to
 * `POST /api/log/client` on one of:
 * - buffer size reaches {@link FLUSH_SIZE}
 * - oldest buffered entry is {@link FLUSH_AGE_MS} or older
 * - the browser emits `beforeunload` or `pagehide` (best-effort via
 *   `navigator.sendBeacon`)
 *
 * On HTTP failure the batcher drops the failed batch, does NOT re-queue, and
 * emits at most one `console.warn` per failure. It must never route its own
 * fetch failures through {@link sessionEventLog} — that would create a feedback
 * loop if the server is down.
 */

export interface SessionEventLogEntry {
  Timestamp: string
  Level: 'Information' | 'Warning' | 'Error'
  Message: string
  Hop: string
  SessionId: string
  TaskId?: string
  MessageId?: string
  ArtifactId?: string
  StatusTimestamp?: string
  EventId?: string
  Seq?: number
  A2AKind?: string
  AGUIType?: string
  AGUICustomName?: string
  ContentPreview?: string
}

export type SessionEventLogFields = Omit<
  SessionEventLogEntry,
  'Timestamp' | 'Level' | 'Message' | 'Hop'
> & {
  Level?: SessionEventLogEntry['Level']
  Message?: string
}

export const FLUSH_SIZE = 50
export const FLUSH_AGE_MS = 500
export const ENDPOINT = '/api/log/client'

/**
 * Pluggable dependencies for {@link SessionEventLogBatcher}. Production uses
 * `performance.now`, `setTimeout`, `fetch`, and `navigator.sendBeacon`; tests
 * inject deterministic stubs.
 */
export interface BatcherDeps {
  now(): number
  setTimeout(fn: () => void, ms: number): ReturnType<typeof globalThis.setTimeout>
  clearTimeout(handle: ReturnType<typeof globalThis.setTimeout>): void
  fetch(url: string, init: RequestInit): Promise<Response>
  sendBeacon?(url: string, body: Blob): boolean
  warn(message: string): void
}

const defaultDeps: BatcherDeps = {
  now: () => Date.now(),
  setTimeout: (fn, ms) => globalThis.setTimeout(fn, ms),
  clearTimeout: (h) => globalThis.clearTimeout(h),
  fetch: (url, init) => globalThis.fetch(url, init),
  sendBeacon:
    typeof navigator !== 'undefined' && 'sendBeacon' in navigator
      ? (url, body) => navigator.sendBeacon(url, body)
      : undefined,
  warn: (m) => console.warn(m),
}

interface BufferedEntry {
  entry: SessionEventLogEntry
  age: number
}

export class SessionEventLogBatcher {
  private readonly deps: BatcherDeps
  private buffer: BufferedEntry[] = []
  private flushTimer: ReturnType<typeof globalThis.setTimeout> | null = null
  private warnedOnLastFailure = false

  constructor(deps: BatcherDeps = defaultDeps) {
    this.deps = deps
  }

  enqueue(entry: SessionEventLogEntry): void {
    this.buffer.push({ entry, age: this.deps.now() })
    if (this.buffer.length >= FLUSH_SIZE) {
      void this.flush()
      return
    }
    this.ensureFlushTimer()
  }

  private ensureFlushTimer(): void {
    if (this.flushTimer !== null) return
    this.flushTimer = this.deps.setTimeout(() => {
      this.flushTimer = null
      void this.flush()
    }, FLUSH_AGE_MS)
  }

  async flush(): Promise<void> {
    if (this.buffer.length === 0) return
    const batch = this.buffer.map((b) => b.entry)
    this.buffer = []
    if (this.flushTimer !== null) {
      this.deps.clearTimeout(this.flushTimer)
      this.flushTimer = null
    }

    try {
      const response = await this.deps.fetch(ENDPOINT, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(batch),
        keepalive: true,
      })
      if (!response.ok) {
        this.reportFailure(
          `session-event-log batcher: server returned ${response.status}; dropping ${batch.length} entries`
        )
      } else {
        this.warnedOnLastFailure = false
      }
    } catch (err) {
      this.reportFailure(
        `session-event-log batcher: flush failed (${err instanceof Error ? err.message : String(err)}); dropping ${batch.length} entries`
      )
    }
  }

  /**
   * Best-effort synchronous flush via `navigator.sendBeacon` for use on
   * `beforeunload` / `pagehide`. Falls back to no-op when sendBeacon is
   * unavailable — the browser is navigating away and a fetch would be
   * cancelled anyway.
   */
  flushBeacon(): void {
    if (this.buffer.length === 0) return
    const batch = this.buffer.map((b) => b.entry)
    this.buffer = []
    if (this.flushTimer !== null) {
      this.deps.clearTimeout(this.flushTimer)
      this.flushTimer = null
    }
    if (!this.deps.sendBeacon) return
    const blob = new Blob([JSON.stringify(batch)], { type: 'application/json' })
    const ok = this.deps.sendBeacon(ENDPOINT, blob)
    if (!ok) {
      // Do not re-queue — we are unloading.
      this.reportFailure(
        `session-event-log batcher: sendBeacon queue refused ${batch.length} entries`
      )
    }
  }

  private reportFailure(message: string): void {
    // NEVER route failures through sessionEventLog — console.warn only, and at
    // most once per consecutive failure to avoid log spam during an outage.
    if (this.warnedOnLastFailure) return
    this.warnedOnLastFailure = true
    this.deps.warn(message)
  }

  get bufferedCount(): number {
    return this.buffer.length
  }
}

let singleton: SessionEventLogBatcher | null = null

function getSingleton(): SessionEventLogBatcher {
  if (singleton) return singleton
  singleton = new SessionEventLogBatcher()
  if (typeof window !== 'undefined') {
    window.addEventListener('beforeunload', () => singleton?.flushBeacon())
    window.addEventListener('pagehide', () => singleton?.flushBeacon())
  }
  return singleton
}

function shortId(id: string | undefined): string {
  if (!id) return ''
  return id.length <= 8 ? id : id.slice(0, 8)
}

function buildMessage(hop: string, fields: SessionEventLogFields): string {
  if (fields.Message) return fields.Message
  const parts = [hop]
  if (fields.Seq !== undefined) parts.push(`seq=${fields.Seq}`)
  if (fields.AGUIType) parts.push(`aguiType=${fields.AGUIType}`)
  if (fields.MessageId) parts.push(`msg=${shortId(fields.MessageId)}`)
  return parts.join(' ')
}

/**
 * Public API: queue a session-event log entry for batched forwarding to the
 * server. Non-blocking. Callers pass a hop name and correlation fields.
 */
export function sessionEventLog(hop: string, fields: SessionEventLogFields): void {
  const entry: SessionEventLogEntry = {
    Timestamp: new Date().toISOString(),
    Level: fields.Level ?? 'Information',
    Message: buildMessage(hop, fields),
    Hop: hop,
    SessionId: fields.SessionId,
    TaskId: fields.TaskId,
    MessageId: fields.MessageId,
    ArtifactId: fields.ArtifactId,
    StatusTimestamp: fields.StatusTimestamp,
    EventId: fields.EventId,
    Seq: fields.Seq,
    A2AKind: fields.A2AKind,
    AGUIType: fields.AGUIType,
    AGUICustomName: fields.AGUICustomName,
    ContentPreview: fields.ContentPreview,
  }
  getSingleton().enqueue(entry)
}

/** Test-only: drop the singleton so a fresh batcher is constructed on next use. */
export function __resetSessionEventLogForTest(): void {
  singleton = null
}
