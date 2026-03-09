import { useContext } from 'react'
import { TelemetryContext } from '@/providers/telemetry-context'

/**
 * Hook to access telemetry tracking methods
 *
 * @example
 * ```tsx
 * const telemetry = useTelemetry()
 *
 * // Track page view
 * telemetry.trackPageView('/dashboard', 'Dashboard')
 *
 * // Track custom event
 * telemetry.trackEvent('button_click', { button: 'create-project' })
 *
 * // Track exception
 * telemetry.trackException(error, { component: 'ProjectList' })
 *
 * // Track API dependency
 * telemetry.trackDependency('GET /api/projects', duration, success, status)
 * ```
 */
export function useTelemetry() {
  const context = useContext(TelemetryContext)

  if (!context) {
    throw new Error('useTelemetry must be used within a TelemetryProvider')
  }

  return context
}
