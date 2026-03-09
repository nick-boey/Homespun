import { vi } from 'vitest'

export class TelemetryService {
  trackPageView = vi.fn()
  trackEvent = vi.fn()
  trackException = vi.fn()
  trackDependency = vi.fn()
  flush = vi.fn().mockResolvedValue(undefined)
  stop = vi.fn()

  constructor() {
    // Mock constructor
  }
}

export const mockTelemetryService = new TelemetryService()
