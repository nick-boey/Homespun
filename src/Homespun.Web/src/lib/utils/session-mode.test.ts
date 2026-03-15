import { describe, it, expect } from 'vitest'
import { toApiSessionMode, fromApiSessionMode, normalizeSessionMode } from './session-mode'
import { SessionMode as ApiSessionMode } from '@/api'

describe('Session Mode Utilities', () => {
  describe('toApiSessionMode', () => {
    it('returns plan when mode is plan', () => {
      expect(toApiSessionMode('plan')).toBe(ApiSessionMode.PLAN)
    })

    it('returns build when mode is build', () => {
      expect(toApiSessionMode('build')).toBe(ApiSessionMode.BUILD)
    })
  })

  describe('fromApiSessionMode', () => {
    it('returns plan when API mode is plan', () => {
      expect(fromApiSessionMode(ApiSessionMode.PLAN)).toBe('plan')
    })

    it('returns build when API mode is build', () => {
      expect(fromApiSessionMode(ApiSessionMode.BUILD)).toBe('build')
    })
  })

  describe('normalizeSessionMode', () => {
    it('converts plan string to plan', () => {
      expect(normalizeSessionMode('plan')).toBe('plan')
    })

    it('converts build string to build', () => {
      expect(normalizeSessionMode('build')).toBe('build')
    })

    it('converts ApiSessionMode.PLAN to plan', () => {
      expect(normalizeSessionMode(ApiSessionMode.PLAN)).toBe('plan')
    })

    it('converts ApiSessionMode.BUILD to build', () => {
      expect(normalizeSessionMode(ApiSessionMode.BUILD)).toBe('build')
    })

    it('converts legacy Plan string to plan', () => {
      expect(normalizeSessionMode('Plan')).toBe('plan')
    })

    it('converts legacy Build string to build', () => {
      expect(normalizeSessionMode('Build')).toBe('build')
    })

    it('defaults to build for undefined', () => {
      expect(normalizeSessionMode(undefined)).toBe('build')
    })

    it('defaults to build for unknown values', () => {
      expect(normalizeSessionMode('Unknown')).toBe('build')
    })

    it('handles legacy numeric values for backwards compatibility', () => {
      expect(normalizeSessionMode(0)).toBe('plan')
      expect(normalizeSessionMode(1)).toBe('build')
    })
  })
})
