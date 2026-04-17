import type { ClientTelemetryEvent, ClientTelemetryBatch } from '@/api/generated'
import { getSessionId } from './session-manager'

export interface TelemetryBatcherConfig {
  sendBatch: (batch: ClientTelemetryBatch) => Promise<void>
  batchSize?: number
  flushInterval?: number
  maxRetries?: number
  endpoint?: string
}

/**
 * Manages batching and sending of telemetry events
 */
export class TelemetryBatcher {
  private buffer: ClientTelemetryEvent[] = []
  private timer: number | null = null
  private stopped = false
  private readonly config: Required<TelemetryBatcherConfig>

  constructor(config: TelemetryBatcherConfig) {
    this.config = {
      batchSize: 50,
      flushInterval: 10000, // 10 seconds
      maxRetries: 3,
      endpoint: '/api/client-telemetry',
      ...config,
    }

    // Set up page unload handler
    this.handleUnload = this.handleUnload.bind(this)
    window.addEventListener('beforeunload', this.handleUnload)
  }

  /**
   * Adds an event to the buffer and triggers flush if needed
   */
  addEvent(event: ClientTelemetryEvent): void {
    if (this.stopped) {
      return
    }

    this.buffer.push(event)

    // Flush immediately if buffer is full
    if (this.buffer.length >= this.config.batchSize) {
      this.flush()
    } else {
      // Reset timer
      this.scheduleFlush()
    }
  }

  /**
   * Flushes all pending events immediately
   */
  async flush(): Promise<void> {
    if (this.buffer.length === 0 || this.stopped) {
      return
    }

    // Cancel any pending timer
    this.clearTimer()

    // Get events to send
    const events = [...this.buffer]
    this.buffer = []

    const batch: ClientTelemetryBatch = {
      sessionId: getSessionId(),
      events,
    }

    // Send with retry
    await this.sendWithRetry(batch)

    // Schedule next flush if not stopped
    if (!this.stopped && this.buffer.length > 0) {
      this.scheduleFlush()
    }
  }

  /**
   * Stops the batcher, preventing new events and clearing timers
   */
  stop(): void {
    this.stopped = true
    this.clearTimer()
    window.removeEventListener('beforeunload', this.handleUnload)
  }

  private async sendWithRetry(batch: ClientTelemetryBatch, attempt = 0): Promise<void> {
    try {
      await this.config.sendBatch(batch)
    } catch (error) {
      if (attempt < this.config.maxRetries) {
        // Exponential backoff: 1s, 2s, 4s
        const delay = Math.pow(2, attempt) * 1000
        await new Promise((resolve) => setTimeout(resolve, delay))
        return this.sendWithRetry(batch, attempt + 1)
      }
      // Give up after max retries
      console.error('Failed to send telemetry after retries:', error)
    }
  }

  private scheduleFlush(): void {
    this.clearTimer()

    if (!this.stopped) {
      this.timer = window.setTimeout(() => {
        this.flush()
      }, this.config.flushInterval)
    }
  }

  private clearTimer(): void {
    if (this.timer !== null) {
      clearTimeout(this.timer)
      this.timer = null
    }
  }

  private handleUnload(): void {
    if (this.buffer.length === 0) {
      return
    }

    const batch: ClientTelemetryBatch = {
      sessionId: getSessionId(),
      events: [...this.buffer],
    }

    // Use sendBeacon if available for reliable delivery on page unload
    if (navigator.sendBeacon) {
      try {
        navigator.sendBeacon(this.config.endpoint, JSON.stringify(batch))
        this.buffer = []
      } catch (error) {
        console.error('Failed to send telemetry via sendBeacon:', error)
      }
    } else {
      // Fall back to regular async send (may not complete)
      this.flush()
    }
  }
}
