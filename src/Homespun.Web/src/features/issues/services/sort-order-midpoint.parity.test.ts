import { describe, it, expect } from 'vitest'
import { midpoint } from './sort-order-midpoint'
import parityFixture from './sort-order-midpoint.parity.json'

describe('sort-order-midpoint parity', () => {
  for (const entry of parityFixture) {
    const { prev, next } = entry as { prev: string; next: string; expected?: string }
    const label = `midpoint(${JSON.stringify(prev)}, ${JSON.stringify(next)})`

    it(`${label} satisfies ordering invariant`, () => {
      const m = midpoint(prev, next)
      if (next === '') {
        expect(m > prev, `${label}: result should be > prev`).toBe(true)
      } else {
        expect(m > prev, `${label}: result should be > prev`).toBe(true)
        expect(m < next, `${label}: result should be < next`).toBe(true)
      }
    })

    if ((entry as { expected?: string }).expected !== undefined) {
      it(`${label} matches C# reference output`, () => {
        const m = midpoint(prev, next)
        expect(m).toBe((entry as { expected: string }).expected)
      })
    }
  }
})
