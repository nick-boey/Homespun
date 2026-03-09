import { describe, it, expect } from 'vitest'
import { toApiSessionMode, fromApiSessionMode } from './session-mode'
import { SessionMode as ApiSessionMode } from '@/api'

describe('Session Mode Utilities', () => {
  describe('toApiSessionMode', () => {
    it('returns 0 when mode is Plan', () => {
      expect(toApiSessionMode('Plan')).toBe(0)
    })

    it('returns 1 when mode is Build', () => {
      expect(toApiSessionMode('Build')).toBe(1)
    })
  })

  describe('fromApiSessionMode', () => {
    it('returns Plan when API mode is 0', () => {
      expect(fromApiSessionMode(0 as ApiSessionMode)).toBe('Plan')
    })

    it('returns Build when API mode is 1', () => {
      expect(fromApiSessionMode(1 as ApiSessionMode)).toBe('Build')
    })
  })
})
