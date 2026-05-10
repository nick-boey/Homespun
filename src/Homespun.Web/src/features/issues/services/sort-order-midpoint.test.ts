import { describe, it, expect } from 'vitest'
import { midpoint, SORT_FIRST, SORT_LAST } from './sort-order-midpoint'

describe('midpoint', () => {
  describe('ordering invariant', () => {
    it('result is strictly between prev and next', () => {
      const m = midpoint('a', 'c')
      expect(m > 'a').toBe(true)
      expect(m < 'c').toBe(true)
    })

    it('result is strictly between prev and next for long strings', () => {
      const m = midpoint('aab', 'aad')
      expect(m > 'aab').toBe(true)
      expect(m < 'aad').toBe(true)
    })

    it('empty prev: result is less than next', () => {
      const m = midpoint('', 'b')
      expect(m < 'b').toBe(true)
      expect(m.length).toBeGreaterThan(0)
    })

    it('empty next sentinel: result is greater than prev', () => {
      const m = midpoint('a', '')
      expect(m > 'a').toBe(true)
    })
  })

  describe('length extension when codepoints are adjacent', () => {
    it('adjacent codepoints produce a longer result', () => {
      const m = midpoint('a', 'b') // 'a'=97, 'b'=98
      expect(m.length).toBeGreaterThanOrEqual(2)
      expect(m > 'a').toBe(true)
      expect(m < 'b').toBe(true)
    })

    it('adjacent strings extend length correctly', () => {
      const m = midpoint('aa', 'ab')
      expect(m > 'aa').toBe(true)
      expect(m < 'ab').toBe(true)
    })
  })

  describe('boundary cases', () => {
    it('throws on equal non-empty inputs', () => {
      expect(() => midpoint('a', 'a')).toThrow()
      expect(() => midpoint('z', 'z')).toThrow()
    })

    it('both empty sentinels returns midpoint of full range', () => {
      // prev="" && next="" → fully unconstrained → 'n' (middle of the a-z alphabet)
      expect(midpoint('', '')).toBe('n')
    })

    it('empty prev and first alphabet char produces a result between them', () => {
      // prev="" maps to getRankBefore(next). SORT_FIRST='a' → getRankBefore('a')
      // returns "an" (since 'a' has no lower half in the alphabet, fallback = "a"+"n").
      const m = midpoint('', String.fromCharCode(SORT_FIRST))
      expect(m.length).toBeGreaterThan(0)
    })

    it('prev at end of alphabet produces a result > prev', () => {
      // SORT_LAST='z' → getRankAfter('z') = "zn" (extends with 'n')
      const m = midpoint(String.fromCharCode(SORT_LAST), '')
      expect(m > String.fromCharCode(SORT_LAST)).toBe(true)
    })
  })

  describe('multiple pairs - ordering invariant holds', () => {
    const pairs: Array<[string, string]> = [
      ['', 'z'],
      ['a', 'z'],
      ['a', 'c'],
      ['aa', 'ac'],
      ['aaa', 'aac'],
      ['ab', 'b'],
      ['a', 'b'],
      ['aab', 'aac'],
    ]

    for (const [p, n] of pairs) {
      it(`midpoint(${JSON.stringify(p)}, ${JSON.stringify(n)}) is between them`, () => {
        const m = midpoint(p, n)
        if (n === '') {
          expect(m > p).toBe(true)
        } else {
          expect(m > p).toBe(true)
          expect(m < n).toBe(true)
        }
      })
    }
  })
})
