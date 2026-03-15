import { SessionMode as ApiSessionMode } from '@/api'

/**
 * Convert frontend session mode to API enum value
 * @param mode - Frontend mode string ('Plan' or 'Build')
 * @returns API SessionMode enum value ('plan' or 'build')
 */
export function toApiSessionMode(mode: 'Plan' | 'Build'): ApiSessionMode {
  return mode === 'Plan' ? ApiSessionMode.PLAN : ApiSessionMode.BUILD
}

/**
 * Convert API session mode enum to frontend string
 * @param mode - API SessionMode enum value ('plan' or 'build')
 * @returns Frontend mode string ('Plan' or 'Build')
 */
export function fromApiSessionMode(mode: ApiSessionMode): 'Plan' | 'Build' {
  return mode === ApiSessionMode.PLAN ? 'Plan' : 'Build'
}

/**
 * Normalize session mode to frontend string.
 * With string enum serialization, this is simpler - just map camelCase to PascalCase.
 *
 * @param mode - Session mode as string ('plan' | 'build' | 'Plan' | 'Build')
 * @returns Normalized frontend mode string ('Plan' or 'Build'), defaults to 'Build' for undefined
 */
export function normalizeSessionMode(mode: string | number | undefined): 'Plan' | 'Build' {
  // Handle string enum values (new format: 'plan' | 'build')
  if (mode === 'plan' || mode === ApiSessionMode.PLAN) return 'Plan'
  if (mode === 'build' || mode === ApiSessionMode.BUILD) return 'Build'
  // Also handle legacy PascalCase for backwards compatibility
  if (mode === 'Plan') return 'Plan'
  if (mode === 'Build') return 'Build'
  // Handle legacy numeric format for backwards compatibility with stored data
  if (mode === 0) return 'Plan'
  if (mode === 1) return 'Build'
  return 'Build' // default
}
