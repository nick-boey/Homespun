import { SessionMode as ApiSessionMode } from '@/api'
import type { SessionMode } from '@/types/signalr'

/**
 * Convert frontend session mode to API enum value
 * @param mode - Frontend mode string ('plan' or 'build')
 * @returns API SessionMode enum value ('plan' or 'build')
 */
export function toApiSessionMode(mode: SessionMode): ApiSessionMode {
  return mode === 'plan' ? ApiSessionMode.PLAN : ApiSessionMode.BUILD
}

/**
 * Convert API session mode enum to frontend string
 * @param mode - API SessionMode enum value ('plan' or 'build')
 * @returns Frontend mode string ('plan' or 'build')
 */
export function fromApiSessionMode(mode: ApiSessionMode): SessionMode {
  return mode === ApiSessionMode.PLAN ? 'plan' : 'build'
}

/**
 * Normalize session mode to frontend string.
 * With string enum serialization, this is simpler - just map various formats to camelCase.
 *
 * @param mode - Session mode as string ('plan' | 'build' | 'Plan' | 'Build')
 * @returns Normalized frontend mode string ('plan' or 'build'), defaults to 'build' for undefined
 */
export function normalizeSessionMode(mode: string | number | undefined): SessionMode {
  // Handle string enum values (new format: 'plan' | 'build')
  if (mode === 'plan' || mode === ApiSessionMode.PLAN) return 'plan'
  if (mode === 'build' || mode === ApiSessionMode.BUILD) return 'build'
  // Also handle legacy PascalCase for backwards compatibility
  if (mode === 'Plan') return 'plan'
  if (mode === 'Build') return 'build'
  // Handle legacy numeric format for backwards compatibility with stored data
  if (mode === 0) return 'plan'
  if (mode === 1) return 'build'
  return 'build' // default
}
