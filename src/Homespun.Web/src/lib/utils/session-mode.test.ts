import { describe, it, expect } from 'vitest'
import { toApiSessionMode, fromApiSessionMode, normalizeSessionMode } from './session-mode'
import { SessionMode as ApiSessionMode } from '@/api'

describe('Session Mode Utilities', () => {
  describe('toApiSessionMode', () => {
    it('returns plan when mode is Plan', () => {
      expect(toApiSessionMode('Plan')).toBe(ApiSessionMode.PLAN)
    })

    it('returns build when mode is Build', () => {
      expect(toApiSessionMode('Build')).toBe(ApiSessionMode.BUILD)
    })
  })

  describe('fromApiSessionMode', () => {
    it('returns Plan when API mode is plan', () => {
      expect(fromApiSessionMode(ApiSessionMode.PLAN)).toBe('Plan')
    })

    it('returns Build when API mode is build', () => {
      expect(fromApiSessionMode(ApiSessionMode.BUILD)).toBe('Build')
    })
  })

  describe('normalizeSessionMode', () => {
    it('converts plan string to Plan', () => {
      expect(normalizeSessionMode('plan')).toBe('Plan')
    })

    it('converts build string to Build', () => {
      expect(normalizeSessionMode('build')).toBe('Build')
    })

    it('converts ApiSessionMode.PLAN to Plan', () => {
      expect(normalizeSessionMode(ApiSessionMode.PLAN)).toBe('Plan')
    })

    it('converts ApiSessionMode.BUILD to Build', () => {
      expect(normalizeSessionMode(ApiSessionMode.BUILD)).toBe('Build')
    })

    it('passes through Plan string', () => {
      expect(normalizeSessionMode('Plan')).toBe('Plan')
    })

    it('passes through Build string', () => {
      expect(normalizeSessionMode('Build')).toBe('Build')
    })

    it('defaults to Build for undefined', () => {
      expect(normalizeSessionMode(undefined)).toBe('Build')
    })

    it('defaults to Build for unknown values', () => {
      expect(normalizeSessionMode('Unknown')).toBe('Build')
    })

    it('handles legacy numeric values for backwards compatibility', () => {
      expect(normalizeSessionMode(0)).toBe('Plan')
      expect(normalizeSessionMode(1)).toBe('Build')
    })
  })
})
