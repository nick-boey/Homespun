import { describe, it, expect } from 'vitest'
import { ClaudeSessionStatus } from '@/api'
import {
  getSessionStatusColor,
  getSessionStatusColorName,
  getSessionStatusTextColor,
} from './session-status-color'

const ALL_STATUSES = Object.values(ClaudeSessionStatus) as ClaudeSessionStatus[]
const NON_STOPPED_STATUSES = ALL_STATUSES.filter((s) => s !== ClaudeSessionStatus.STOPPED)

describe('getSessionStatusColor', () => {
  it('returns a Tailwind bg class for every non-STOPPED enum value', () => {
    for (const status of NON_STOPPED_STATUSES) {
      const cls = getSessionStatusColor(status)
      expect(cls, `status ${status}`).not.toBeNull()
      expect(cls).toMatch(/^bg-(green|yellow|purple|orange|red)-500$/)
    }
  })

  it('returns null for STOPPED', () => {
    expect(getSessionStatusColor(ClaudeSessionStatus.STOPPED)).toBeNull()
  })

  it('returns null for undefined', () => {
    expect(getSessionStatusColor(undefined)).toBeNull()
  })

  it('maps STARTING / RUNNING_HOOKS / RUNNING to green', () => {
    expect(getSessionStatusColor(ClaudeSessionStatus.STARTING)).toBe('bg-green-500')
    expect(getSessionStatusColor(ClaudeSessionStatus.RUNNING_HOOKS)).toBe('bg-green-500')
    expect(getSessionStatusColor(ClaudeSessionStatus.RUNNING)).toBe('bg-green-500')
  })

  it('maps WAITING_FOR_INPUT to yellow', () => {
    expect(getSessionStatusColor(ClaudeSessionStatus.WAITING_FOR_INPUT)).toBe('bg-yellow-500')
  })

  it('maps WAITING_FOR_QUESTION_ANSWER to purple', () => {
    expect(getSessionStatusColor(ClaudeSessionStatus.WAITING_FOR_QUESTION_ANSWER)).toBe(
      'bg-purple-500'
    )
  })

  it('maps WAITING_FOR_PLAN_EXECUTION to orange', () => {
    expect(getSessionStatusColor(ClaudeSessionStatus.WAITING_FOR_PLAN_EXECUTION)).toBe(
      'bg-orange-500'
    )
  })

  it('maps ERROR to red', () => {
    expect(getSessionStatusColor(ClaudeSessionStatus.ERROR)).toBe('bg-red-500')
  })
})

describe('getSessionStatusColorName', () => {
  it('returns null for STOPPED', () => {
    expect(getSessionStatusColorName(ClaudeSessionStatus.STOPPED)).toBeNull()
  })

  it('returns null for undefined', () => {
    expect(getSessionStatusColorName(undefined)).toBeNull()
  })

  it('returns one of the five colour names for every non-STOPPED status', () => {
    for (const status of NON_STOPPED_STATUSES) {
      const name = getSessionStatusColorName(status)
      expect(['green', 'yellow', 'purple', 'orange', 'red']).toContain(name)
    }
  })
})

describe('getSessionStatusTextColor', () => {
  it('returns Tailwind text class matching the bg class colour family', () => {
    expect(getSessionStatusTextColor(ClaudeSessionStatus.RUNNING)).toBe('text-green-500')
    expect(getSessionStatusTextColor(ClaudeSessionStatus.WAITING_FOR_INPUT)).toBe('text-yellow-500')
    expect(getSessionStatusTextColor(ClaudeSessionStatus.ERROR)).toBe('text-red-500')
  })

  it('returns null for STOPPED', () => {
    expect(getSessionStatusTextColor(ClaudeSessionStatus.STOPPED)).toBeNull()
  })
})
