/**
 * Session manager for client telemetry
 * Handles generation and persistence of session IDs with 24-hour expiration
 */

// Session duration: 24 hours in milliseconds
export const SESSION_DURATION_MS = 24 * 60 * 60 * 1000

const SESSION_ID_KEY = 'telemetry_session_id'
const SESSION_EXPIRY_KEY = 'telemetry_session_expiry'

/**
 * Generates a UUID v4
 */
function generateUUID(): string {
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0
    const v = c === 'x' ? r : (r & 0x3) | 0x8
    return v.toString(16)
  })
}

/**
 * Checks if a string is a valid UUID format
 */
function isValidUUID(uuid: string): boolean {
  const uuidRegex = /^[a-f0-9]{8}-[a-f0-9]{4}-4[a-f0-9]{3}-[89ab][a-f0-9]{3}-[a-f0-9]{12}$/i
  return uuidRegex.test(uuid)
}

/**
 * Gets the current session ID, creating a new one if necessary
 * Sessions expire after 24 hours
 */
export function getSessionId(): string {
  try {
    const storedId = sessionStorage.getItem(SESSION_ID_KEY)
    const storedExpiry = sessionStorage.getItem(SESSION_EXPIRY_KEY)

    // Check if we have a valid session
    if (storedId && storedExpiry) {
      const expiry = parseInt(storedExpiry, 10)
      const now = Date.now()

      // Session is valid and not expired
      if (!isNaN(expiry) && expiry > now && isValidUUID(storedId)) {
        return storedId
      }
    }

    // Create new session
    const newSessionId = generateUUID()
    const newExpiry = Date.now() + SESSION_DURATION_MS

    sessionStorage.setItem(SESSION_ID_KEY, newSessionId)
    sessionStorage.setItem(SESSION_EXPIRY_KEY, newExpiry.toString())

    return newSessionId
  } catch (error) {
    // If sessionStorage is not available or throws an error,
    // return a new UUID without persistence
    console.warn('Failed to access sessionStorage for telemetry session:', error)
    return generateUUID()
  }
}

/**
 * Resets the current session, clearing stored data
 */
export function resetSession(): void {
  try {
    sessionStorage.removeItem(SESSION_ID_KEY)
    sessionStorage.removeItem(SESSION_EXPIRY_KEY)
  } catch (error) {
    console.warn('Failed to clear telemetry session:', error)
  }
}
