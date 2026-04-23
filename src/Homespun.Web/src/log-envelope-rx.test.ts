import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest'
import { logs } from '@opentelemetry/api-logs'

describe('logEnvelopeRx', () => {
  const emitSpy = vi.fn()
  const originalFlag = import.meta.env.VITE_HOMESPUN_DEBUG_FULL_MESSAGES

  beforeEach(() => {
    emitSpy.mockClear()
    vi.spyOn(logs, 'getLogger').mockReturnValue({
      emit: emitSpy,
    } as unknown as ReturnType<typeof logs.getLogger>)
  })

  afterEach(() => {
    vi.restoreAllMocks()
    // Restore the original env flag so subsequent tests see production value.
    import.meta.env.VITE_HOMESPUN_DEBUG_FULL_MESSAGES = originalFlag
    vi.resetModules()
  })

  it('is a no-op when VITE_HOMESPUN_DEBUG_FULL_MESSAGES is unset at build time', async () => {
    import.meta.env.VITE_HOMESPUN_DEBUG_FULL_MESSAGES = undefined
    vi.resetModules()
    const { logEnvelopeRx, IS_FULL_MESSAGES_DEBUG } = await import('./instrumentation')

    expect(IS_FULL_MESSAGES_DEBUG).toBe(false)

    logEnvelopeRx({ seq: 1, sessionId: 's', event: { type: 'RUN_STARTED' } })
    expect(emitSpy).not.toHaveBeenCalled()
  })

  it('emits an OTel INFO log record when VITE_HOMESPUN_DEBUG_FULL_MESSAGES=true', async () => {
    import.meta.env.VITE_HOMESPUN_DEBUG_FULL_MESSAGES = 'true'
    vi.resetModules()
    const { logEnvelopeRx, IS_FULL_MESSAGES_DEBUG } = await import('./instrumentation')

    expect(IS_FULL_MESSAGES_DEBUG).toBe(true)

    const envelope = {
      seq: 42,
      sessionId: 'sess-42',
      eventId: 'evt-42',
      traceparent: '00-abc-def-01',
      event: { type: 'TEXT_MESSAGE_CONTENT' },
    }
    logEnvelopeRx(envelope)

    expect(emitSpy).toHaveBeenCalledTimes(1)
    const record = emitSpy.mock.calls[0][0] as {
      body: string
      attributes: Record<string, unknown>
    }
    expect(record.body).toContain('homespun.envelope.rx')
    expect(record.body).toContain('seq=42')
    expect(record.body).toContain('sessionId=sess-42')
    expect(record.body).toContain('type=TEXT_MESSAGE_CONTENT')
    expect(record.attributes['homespun.session.id']).toBe('sess-42')
    expect(record.attributes['homespun.seq']).toBe(42)
    expect(record.attributes['homespun.agui.type']).toBe('TEXT_MESSAGE_CONTENT')
    expect(record.attributes['homespun.traceparent']).toBe('00-abc-def-01')
  })
})
