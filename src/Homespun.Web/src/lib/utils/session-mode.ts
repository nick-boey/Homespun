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
