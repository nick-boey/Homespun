import { vi } from 'vitest'
import type { TelemetryContextValue } from '@/providers/telemetry-context'

/**
 * Mock telemetry functions for testing
 */
export const mockTelemetry: TelemetryContextValue = {
  trackPageView: vi.fn(),
  trackEvent: vi.fn(),
  trackException: vi.fn(),
  trackDependency: vi.fn(),
}

/**
 * Mock the useTelemetry hook
 */
vi.mock('@/hooks/use-telemetry', () => ({
  useTelemetry: () => mockTelemetry,
}))
