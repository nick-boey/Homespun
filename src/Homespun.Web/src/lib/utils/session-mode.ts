import { SessionMode as ApiSessionMode } from '@/api'

/**
 * Convert frontend session mode to API enum value
 * @param mode - Frontend mode string ('Plan' or 'Build')
 * @returns API SessionMode enum value (0 for Plan, 1 for Build)
 */
export function toApiSessionMode(mode: 'Plan' | 'Build'): ApiSessionMode {
  return mode === 'Plan' ? 0 : 1
}

/**
 * Convert API session mode enum to frontend string
 * @param mode - API SessionMode enum value
 * @returns Frontend mode string ('Plan' or 'Build')
 */
export function fromApiSessionMode(mode: ApiSessionMode): 'Plan' | 'Build' {
  return mode === 0 ? 'Plan' : 'Build'
}

/**
 * Normalize session mode from either numeric (SignalR) or string format to frontend string.
 * Handles the type mismatch between backend C# enum (serialized as integer) and frontend TypeScript strings.
 *
 * @param mode - Session mode as number (0 = Plan, 1 = Build) or string ('Plan' | 'Build')
 * @returns Normalized frontend mode string ('Plan' or 'Build'), defaults to 'Build' for undefined
 */
export function normalizeSessionMode(mode: string | number | undefined): 'Plan' | 'Build' {
  if (mode === 0 || mode === 'Plan') return 'Plan'
  if (mode === 1 || mode === 'Build') return 'Build'
  return 'Build' // default
}
