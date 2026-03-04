import { describe, it, expect } from 'vitest'
import { computeMidpoint } from './lex-order'

describe('computeMidpoint', () => {
  describe('when both bounds are null/undefined', () => {
    it('returns "V" as the default midpoint', () => {
      expect(computeMidpoint()).toBe('V')
      expect(computeMidpoint(null, null)).toBe('V')
      expect(computeMidpoint(undefined, undefined)).toBe('V')
    })
  })

  describe('when inserting after all (after is null)', () => {
    it('appends "V" to the before string', () => {
      expect(computeMidpoint('a', null)).toBe('aV')
      expect(computeMidpoint('0', null)).toBe('0V')
      expect(computeMidpoint('V', null)).toBe('VV')
    })
  })

  describe('when inserting before all (before is null)', () => {
    it('finds a character before the first char of after', () => {
      const result = computeMidpoint(null, 'V')
      expect(result < 'V').toBe(true)
    })

    it('handles edge cases with low ASCII characters', () => {
      const result = computeMidpoint(null, '"') // ASCII 34, just after '!' (33)
      expect(result < '"').toBe(true)
    })
  })

  describe('when inserting between two values', () => {
    it('finds a midpoint between adjacent characters', () => {
      const result = computeMidpoint('a', 'z')
      expect(result > 'a').toBe(true)
      expect(result < 'z').toBe(true)
    })

    it('finds a midpoint between "0" and "1"', () => {
      const result = computeMidpoint('0', '1')
      expect(result > '0').toBe(true)
      expect(result < '1').toBe(true)
    })

    it('handles inserting between values with same prefix', () => {
      const result = computeMidpoint('aa', 'az')
      expect(result > 'aa').toBe(true)
      expect(result < 'az').toBe(true)
    })

    it('handles inserting between very close values', () => {
      const result = computeMidpoint('ab', 'ac')
      expect(result > 'ab').toBe(true)
      expect(result < 'ac').toBe(true)
    })
  })

  describe('lexicographic ordering guarantees', () => {
    it('produces results that maintain correct ordering', () => {
      // Insert multiple values and verify ordering is preserved
      const a = 'a'
      const c = 'c'
      const b = computeMidpoint(a, c)

      expect(a < b).toBe(true)
      expect(b < c).toBe(true)

      // Now insert between a and b
      const ab = computeMidpoint(a, b)
      expect(a < ab).toBe(true)
      expect(ab < b).toBe(true)

      // And between b and c
      const bc = computeMidpoint(b, c)
      expect(b < bc).toBe(true)
      expect(bc < c).toBe(true)
    })

    it('can handle many successive insertions after a value', () => {
      let prev = 'a'

      // Insert 10 items after 'a', each after the previous
      for (let i = 0; i < 10; i++) {
        const mid = computeMidpoint(prev, null)
        expect(mid > prev).toBe(true)
        prev = mid
      }
    })

    it('can handle insertions between two values', () => {
      // Test inserting between 'a' and 'z'
      const a = 'a'
      const z = 'z'
      const mid = computeMidpoint(a, z)
      expect(mid > a).toBe(true)
      expect(mid < z).toBe(true)
    })
  })
})
