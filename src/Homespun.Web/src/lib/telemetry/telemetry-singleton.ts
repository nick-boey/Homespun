import { TelemetryService } from './telemetry-service'

// Global telemetry service instance
let telemetryService: TelemetryService | null = null

/**
 * Set the global telemetry service instance.
 * This is called by the TelemetryProvider during initialization.
 */
export function setGlobalTelemetryService(service: TelemetryService): void {
  telemetryService = service
}

/**
 * Get the global telemetry service instance.
 * Returns null if telemetry has not been initialized yet.
 */
export function getGlobalTelemetryService(): TelemetryService | null {
  return telemetryService
}
