import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest'
import { getSessionId, resetSession, SESSION_DURATION_MS } from './session-manager'

describe('session-manager', () => {
  beforeEach(() => {
    // Clear sessionStorage before each test
    sessionStorage.clear()
    vi.clearAllMocks()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  describe('getSessionId', () => {
    it('generates a new session ID when none exists', () => {
      const sessionId = getSessionId()

      expect(sessionId).toBeDefined()
      expect(sessionId).toMatch(/^[a-f0-9-]{36}$/) // UUID format
      expect(sessionStorage.getItem('telemetry_session_id')).toBe(sessionId)
    })

    it('returns the same session ID on subsequent calls', () => {
      const firstId = getSessionId()
      const secondId = getSessionId()

      expect(secondId).toBe(firstId)
    })

    it('generates a new session ID when the previous one has expired', () => {
      vi.useFakeTimers()

      const firstId = getSessionId()
      expect(sessionStorage.getItem('telemetry_session_expiry')).toBeDefined()

      // Move time forward past session expiration (24 hours + 1 minute)
      vi.advanceTimersByTime(SESSION_DURATION_MS + 60000)

      const secondId = getSessionId()

      expect(secondId).not.toBe(firstId)
      expect(secondId).toMatch(/^[a-f0-9-]{36}$/)
    })

    it('stores session expiry time when creating a new session', () => {
      vi.useFakeTimers()
      const now = Date.now()
      vi.setSystemTime(now)

      getSessionId()

      const expiryStr = sessionStorage.getItem('telemetry_session_expiry')
      expect(expiryStr).toBeDefined()

      const expiry = parseInt(expiryStr!, 10)
      expect(expiry).toBe(now + SESSION_DURATION_MS)
    })

    it('handles corrupted session data gracefully', () => {
      sessionStorage.setItem('telemetry_session_id', 'invalid-uuid')
      sessionStorage.setItem('telemetry_session_expiry', 'not-a-number')

      const sessionId = getSessionId()

      expect(sessionId).toMatch(/^[a-f0-9-]{36}$/)
      expect(sessionStorage.getItem('telemetry_session_expiry')).toBeDefined()
    })
  })

  describe('resetSession', () => {
    it('clears session data from storage', () => {
      getSessionId() // Create a session first
      expect(sessionStorage.getItem('telemetry_session_id')).toBeDefined()
      expect(sessionStorage.getItem('telemetry_session_expiry')).toBeDefined()

      resetSession()

      expect(sessionStorage.getItem('telemetry_session_id')).toBeNull()
      expect(sessionStorage.getItem('telemetry_session_expiry')).toBeNull()
    })

    it('generates a new session ID after reset', () => {
      const firstId = getSessionId()
      resetSession()
      const secondId = getSessionId()

      expect(secondId).not.toBe(firstId)
    })
  })
})
