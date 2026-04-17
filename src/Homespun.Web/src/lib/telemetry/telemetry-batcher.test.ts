import { describe, it, expect, beforeEach, vi, afterEach, type Mock } from 'vitest'
import { TelemetryBatcher } from './telemetry-batcher'
import { TelemetryEventType, type ClientTelemetryEvent } from '@/api/generated'

// Mock the session manager
vi.mock('./session-manager', () => ({
  getSessionId: vi.fn().mockReturnValue('test-session-id'),
}))

describe('TelemetryBatcher', () => {
  let batcher: TelemetryBatcher
  let sendFn: Mock

  beforeEach(() => {
    vi.useFakeTimers()
    sendFn = vi.fn().mockResolvedValue(undefined)
    batcher = new TelemetryBatcher({
      sendBatch: sendFn,
      batchSize: 5,
      flushInterval: 1000, // 1 second for tests
    })
  })

  afterEach(() => {
    batcher.stop()
    vi.clearAllMocks()
    vi.useRealTimers()
  })

  describe('addEvent', () => {
    it('adds events to the buffer', () => {
      const event: ClientTelemetryEvent = {
        type: TelemetryEventType.EVENT, // Event
        name: 'test-event',
        timestamp: new Date().toISOString(),
      }

      batcher.addEvent(event)

      // Should not send immediately
      expect(sendFn).not.toHaveBeenCalled()
    })

    it('sends batch when buffer reaches batchSize', async () => {
      const events: ClientTelemetryEvent[] = []

      for (let i = 0; i < 5; i++) {
        events.push({
          type: TelemetryEventType.EVENT,
          name: `event-${i}`,
          timestamp: new Date().toISOString(),
        })
        batcher.addEvent(events[i])
      }

      // Should trigger immediate send
      await vi.waitFor(() => expect(sendFn).toHaveBeenCalledTimes(1))

      expect(sendFn).toHaveBeenCalledWith({
        sessionId: 'test-session-id',
        events,
      })
    })

    it('does not send when stopped', () => {
      batcher.stop()

      const event: ClientTelemetryEvent = {
        type: TelemetryEventType.EVENT,
        name: 'test-event',
        timestamp: new Date().toISOString(),
      }

      batcher.addEvent(event)

      // Advance time past flush interval
      vi.advanceTimersByTime(2000)

      expect(sendFn).not.toHaveBeenCalled()
    })
  })

  describe('flush', () => {
    it('sends all pending events immediately', async () => {
      const events: ClientTelemetryEvent[] = [
        { type: TelemetryEventType.EVENT, name: 'event-1', timestamp: new Date().toISOString() },
        { type: TelemetryEventType.EVENT, name: 'event-2', timestamp: new Date().toISOString() },
      ]

      events.forEach((e) => batcher.addEvent(e))

      await batcher.flush()

      expect(sendFn).toHaveBeenCalledTimes(1)
      expect(sendFn).toHaveBeenCalledWith({
        sessionId: 'test-session-id',
        events,
      })
    })

    it('does nothing when buffer is empty', async () => {
      await batcher.flush()

      expect(sendFn).not.toHaveBeenCalled()
    })

    it('clears buffer after successful flush', async () => {
      const event: ClientTelemetryEvent = {
        type: TelemetryEventType.EVENT,
        name: 'test-event',
        timestamp: new Date().toISOString(),
      }

      batcher.addEvent(event)
      await batcher.flush()

      // First flush should send
      expect(sendFn).toHaveBeenCalledTimes(1)

      // Second flush should not send (buffer empty)
      await batcher.flush()
      expect(sendFn).toHaveBeenCalledTimes(1)
    })

    it('retries on failure with exponential backoff', async () => {
      sendFn
        .mockRejectedValueOnce(new Error('Network error'))
        .mockRejectedValueOnce(new Error('Network error'))
        .mockResolvedValueOnce(undefined)

      const event: ClientTelemetryEvent = {
        type: TelemetryEventType.EVENT,
        name: 'test-event',
        timestamp: new Date().toISOString(),
      }

      batcher.addEvent(event)
      const flushPromise = batcher.flush()

      // Wait for first attempt
      await vi.advanceTimersByTimeAsync(0)
      expect(sendFn).toHaveBeenCalledTimes(1)

      // Wait for first retry (1 second)
      await vi.advanceTimersByTimeAsync(1000)
      expect(sendFn).toHaveBeenCalledTimes(2)

      // Wait for second retry (2 seconds)
      await vi.advanceTimersByTimeAsync(2000)
      expect(sendFn).toHaveBeenCalledTimes(3)

      await flushPromise
    })

    it('gives up after max retries', async () => {
      sendFn.mockRejectedValue(new Error('Network error'))

      const event: ClientTelemetryEvent = {
        type: TelemetryEventType.EVENT,
        name: 'test-event',
        timestamp: new Date().toISOString(),
      }

      batcher.addEvent(event)
      const flushPromise = batcher.flush()

      // Wait for all retry attempts
      await vi.advanceTimersByTimeAsync(0) // Initial attempt
      await vi.advanceTimersByTimeAsync(1000) // First retry
      await vi.advanceTimersByTimeAsync(2000) // Second retry
      await vi.advanceTimersByTimeAsync(4000) // Third retry (max)

      await flushPromise

      expect(sendFn).toHaveBeenCalledTimes(4) // Initial + 3 retries
    })
  })

  describe('automatic flushing', () => {
    it('flushes automatically after flushInterval', async () => {
      const event: ClientTelemetryEvent = {
        type: TelemetryEventType.EVENT,
        name: 'test-event',
        timestamp: new Date().toISOString(),
      }

      batcher.addEvent(event)

      // Advance time to trigger interval flush
      await vi.advanceTimersByTimeAsync(1000)

      expect(sendFn).toHaveBeenCalledTimes(1)
      expect(sendFn).toHaveBeenCalledWith({
        sessionId: 'test-session-id',
        events: [event],
      })
    })

    it('resets timer when batch is sent due to size', async () => {
      // Fill buffer to trigger immediate send
      for (let i = 0; i < 5; i++) {
        batcher.addEvent({
          type: TelemetryEventType.EVENT,
          name: `event-${i}`,
          timestamp: new Date().toISOString(),
        })
      }

      await vi.waitFor(() => expect(sendFn).toHaveBeenCalledTimes(1))

      // Add another event
      batcher.addEvent({
        type: TelemetryEventType.EVENT,
        name: 'event-after',
        timestamp: new Date().toISOString(),
      })

      // Advance time less than flush interval
      await vi.advanceTimersByTimeAsync(500)

      // Should not have sent again yet
      expect(sendFn).toHaveBeenCalledTimes(1)

      // Advance time to complete the interval
      await vi.advanceTimersByTimeAsync(500)

      // Now should have sent
      expect(sendFn).toHaveBeenCalledTimes(2)
    })
  })

  describe('stop', () => {
    it('prevents new events from being added', () => {
      const event: ClientTelemetryEvent = {
        type: TelemetryEventType.EVENT,
        name: 'test-event',
        timestamp: new Date().toISOString(),
      }

      batcher.stop()
      batcher.addEvent(event)

      // Should not accept new events
      vi.advanceTimersByTime(2000)
      expect(sendFn).not.toHaveBeenCalled()
    })

    it('clears pending timer', () => {
      const event: ClientTelemetryEvent = {
        type: TelemetryEventType.EVENT,
        name: 'test-event',
        timestamp: new Date().toISOString(),
      }

      batcher.addEvent(event)
      batcher.stop()

      // Advance time past flush interval
      vi.advanceTimersByTime(2000)

      // Should not send
      expect(sendFn).not.toHaveBeenCalled()
    })
  })

  describe('navigator.sendBeacon fallback', () => {
    it('uses sendBeacon on page unload if available', () => {
      const mockSendBeacon = vi.fn().mockReturnValue(true)
      globalThis.navigator = {
        ...globalThis.navigator,
        sendBeacon: mockSendBeacon,
      } as Navigator

      const event: ClientTelemetryEvent = {
        type: TelemetryEventType.EVENT,
        name: 'test-event',
        timestamp: new Date().toISOString(),
      }

      batcher.addEvent(event)

      // Simulate page unload
      window.dispatchEvent(new Event('beforeunload'))

      expect(mockSendBeacon).toHaveBeenCalledWith(
        '/api/client-telemetry',
        JSON.stringify({
          sessionId: 'test-session-id',
          events: [event],
        })
      )

      // Clean up
      delete (globalThis.navigator as { sendBeacon?: typeof navigator.sendBeacon }).sendBeacon
    })

    it('falls back to regular send when sendBeacon is not available', async () => {
      // Ensure sendBeacon is not available
      const originalSendBeacon = globalThis.navigator?.sendBeacon
      if (originalSendBeacon !== undefined) {
        delete (globalThis.navigator as { sendBeacon?: typeof navigator.sendBeacon }).sendBeacon
      }

      const event: ClientTelemetryEvent = {
        type: TelemetryEventType.EVENT,
        name: 'test-event',
        timestamp: new Date().toISOString(),
      }

      batcher.addEvent(event)

      // Simulate page unload
      window.dispatchEvent(new Event('beforeunload'))

      // Should use regular send
      await vi.waitFor(() => expect(sendFn).toHaveBeenCalled())

      // Restore if it existed
      if (originalSendBeacon) {
        ;(globalThis.navigator as { sendBeacon?: typeof navigator.sendBeacon }).sendBeacon =
          originalSendBeacon
      }
    })
  })
})
