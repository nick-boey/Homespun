import type { ClientTelemetryEvent, ClientTelemetryBatch } from '@/api/generated'
import { TelemetryEventType } from '@/api'
import { TelemetryBatcher } from './telemetry-batcher'

export interface TelemetryConfig {
  enabled?: boolean
  endpoint?: string
  batchSize?: number
  flushInterval?: number
}

/**
 * Main telemetry service for tracking client-side events
 */
export class TelemetryService {
  private readonly config: Required<TelemetryConfig>
  private readonly batcher: TelemetryBatcher

  constructor(config: TelemetryConfig = {}) {
    this.config = {
      enabled: true,
      endpoint: '/api/client-telemetry',
      batchSize: 50,
      flushInterval: 10000,
      ...config,
    }

    this.batcher = new TelemetryBatcher({
      sendBatch: this.sendBatch.bind(this),
      batchSize: this.config.batchSize,
      flushInterval: this.config.flushInterval,
      endpoint: this.config.endpoint,
    })
  }

  /**
   * Tracks a page view event
   */
  trackPageView(url: string, title?: string, properties?: Record<string, string>): void {
    if (!this.config.enabled) return

    const event: ClientTelemetryEvent = {
      type: TelemetryEventType.PAGE_VIEW,
      name: url,
      timestamp: new Date().toISOString(),
      properties:
        title || properties
          ? {
              ...(title && { title }),
              ...properties,
            }
          : undefined,
    }

    this.batcher.addEvent(event)
  }

  /**
   * Tracks a custom event
   */
  trackEvent(name: string, properties?: Record<string, string>): void {
    if (!this.config.enabled) return

    const event: ClientTelemetryEvent = {
      type: TelemetryEventType.EVENT,
      name,
      timestamp: new Date().toISOString(),
      properties,
    }

    this.batcher.addEvent(event)
  }

  /**
   * Tracks an exception/error
   */
  trackException(error: Error | string | unknown, properties?: Record<string, string>): void {
    if (!this.config.enabled) return

    let errorName: string
    const errorProperties: Record<string, string> = { ...properties }

    if (error instanceof Error) {
      errorName = `${error.name}: ${error.message}`
      errorProperties.message = error.message
      if (error.stack) {
        errorProperties.stack = error.stack
      }
    } else if (typeof error === 'string') {
      errorName = error
      errorProperties.message = error
    } else {
      errorName = 'Unknown error'
      errorProperties.error = JSON.stringify(error)
    }

    const event: ClientTelemetryEvent = {
      type: TelemetryEventType.EXCEPTION,
      name: errorName,
      timestamp: new Date().toISOString(),
      properties: errorProperties,
    }

    this.batcher.addEvent(event)

    // Flush immediately for exceptions
    this.flush()
  }

  /**
   * Tracks a dependency call (e.g., API request)
   */
  trackDependency(
    name: string,
    duration: number,
    success: boolean,
    statusCode?: number,
    properties?: Record<string, string>
  ): void {
    if (!this.config.enabled) return

    const event: ClientTelemetryEvent = {
      type: TelemetryEventType.DEPENDENCY,
      name,
      timestamp: new Date().toISOString(),
      durationMs: duration,
      success,
      statusCode,
      properties,
    }

    this.batcher.addEvent(event)
  }

  /**
   * Flushes all pending events
   */
  async flush(): Promise<void> {
    return this.batcher.flush()
  }

  /**
   * Stops the telemetry service
   */
  stop(): void {
    this.batcher.stop()
  }

  /**
   * Sends a batch of events to the server
   */
  private async sendBatch(batch: ClientTelemetryBatch): Promise<void> {
    const response = await fetch(this.config.endpoint, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json',
      },
      body: JSON.stringify(batch),
    })

    if (!response.ok) {
      throw new Error(`Failed to send telemetry: ${response.status} ${response.statusText}`)
    }
  }
}
