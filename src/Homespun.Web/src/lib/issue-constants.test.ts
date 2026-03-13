import { describe, it, expect } from 'vitest'
import {
  ISSUE_STATUS,
  ISSUE_STATUS_LABELS,
  ISSUE_STATUS_COLORS,
  ISSUE_STATUS_OPTIONS,
  ISSUE_TYPE,
  ISSUE_TYPE_LABELS,
  ISSUE_TYPE_OPTIONS,
  getStatusLabel,
  getStatusColorClass,
  getTypeLabel,
} from './issue-constants'

describe('Issue Status Constants', () => {
  describe('ISSUE_STATUS', () => {
    it('should have correct enum values matching C# Fleece.Core.Models.IssueStatus', () => {
      // These values MUST match the C# enum to ensure React displays match Blazor
      expect(ISSUE_STATUS.Open).toBe(0)
      expect(ISSUE_STATUS.Progress).toBe(1)
      expect(ISSUE_STATUS.Review).toBe(2)
      expect(ISSUE_STATUS.Complete).toBe(3)
      expect(ISSUE_STATUS.Archived).toBe(4)
      expect(ISSUE_STATUS.Closed).toBe(5)
      expect(ISSUE_STATUS.Deleted).toBe(6)
    })
  })

  describe('ISSUE_STATUS_LABELS', () => {
    it('should have labels for all status values', () => {
      expect(ISSUE_STATUS_LABELS[0]).toBe('Open')
      expect(ISSUE_STATUS_LABELS[1]).toBe('In Progress')
      expect(ISSUE_STATUS_LABELS[2]).toBe('Review')
      expect(ISSUE_STATUS_LABELS[3]).toBe('Complete')
      expect(ISSUE_STATUS_LABELS[4]).toBe('Archived')
      expect(ISSUE_STATUS_LABELS[5]).toBe('Closed')
      expect(ISSUE_STATUS_LABELS[6]).toBe('Deleted')
    })

    it('should have labels for all defined statuses', () => {
      Object.values(ISSUE_STATUS).forEach((value) => {
        expect(ISSUE_STATUS_LABELS[value]).toBeDefined()
        expect(typeof ISSUE_STATUS_LABELS[value]).toBe('string')
      })
    })
  })

  describe('ISSUE_STATUS_COLORS', () => {
    it('should have color classes for all status values', () => {
      Object.values(ISSUE_STATUS).forEach((value) => {
        expect(ISSUE_STATUS_COLORS[value]).toBeDefined()
        expect(typeof ISSUE_STATUS_COLORS[value]).toBe('string')
      })
    })
  })

  describe('ISSUE_STATUS_OPTIONS', () => {
    it('should have correct value-label pairs', () => {
      const openOption = ISSUE_STATUS_OPTIONS.find((o) => o.value === '0')
      expect(openOption?.label).toBe('Open')

      const progressOption = ISSUE_STATUS_OPTIONS.find((o) => o.value === '1')
      expect(progressOption?.label).toBe('In Progress')

      const reviewOption = ISSUE_STATUS_OPTIONS.find((o) => o.value === '2')
      expect(reviewOption?.label).toBe('Review')

      const completeOption = ISSUE_STATUS_OPTIONS.find((o) => o.value === '3')
      expect(completeOption?.label).toBe('Complete')
    })

    it('should not include Deleted status in dropdown options', () => {
      const deletedOption = ISSUE_STATUS_OPTIONS.find((o) => o.value === '6')
      expect(deletedOption).toBeUndefined()
    })
  })

  describe('getStatusLabel', () => {
    it('should return correct labels for valid statuses', () => {
      expect(getStatusLabel(0)).toBe('Open')
      expect(getStatusLabel(1)).toBe('In Progress')
      expect(getStatusLabel(2)).toBe('Review')
      expect(getStatusLabel(3)).toBe('Complete')
      expect(getStatusLabel(4)).toBe('Archived')
      expect(getStatusLabel(5)).toBe('Closed')
      expect(getStatusLabel(6)).toBe('Deleted')
    })

    it('should return Unknown for undefined or null', () => {
      expect(getStatusLabel(undefined)).toBe('Unknown')
      expect(getStatusLabel(null)).toBe('Unknown')
    })

    it('should return Unknown for invalid status values', () => {
      expect(getStatusLabel(99)).toBe('Unknown')
      expect(getStatusLabel(-1)).toBe('Unknown')
    })
  })

  describe('getStatusColorClass', () => {
    it('should return color classes for valid statuses', () => {
      expect(getStatusColorClass(0)).toContain('blue')
      expect(getStatusColorClass(1)).toContain('yellow')
      expect(getStatusColorClass(2)).toContain('purple')
      expect(getStatusColorClass(3)).toContain('green')
    })

    it('should return default color for undefined or null', () => {
      const defaultColor = getStatusColorClass(undefined)
      expect(defaultColor).toBe(ISSUE_STATUS_COLORS[0])
      expect(getStatusColorClass(null)).toBe(ISSUE_STATUS_COLORS[0])
    })
  })
})

describe('Issue Type Constants', () => {
  describe('ISSUE_TYPE', () => {
    it('should have correct enum values matching C# Fleece.Core.Models.IssueType', () => {
      // These values MUST match the C# enum to ensure React displays match Blazor
      expect(ISSUE_TYPE.Task).toBe(0)
      expect(ISSUE_TYPE.Bug).toBe(1)
      expect(ISSUE_TYPE.Chore).toBe(2)
      expect(ISSUE_TYPE.Feature).toBe(3)
      expect(ISSUE_TYPE.Idea).toBe(4)
      expect(ISSUE_TYPE.Verify).toBe(5)
    })
  })

  describe('ISSUE_TYPE_LABELS', () => {
    it('should have labels for all type values', () => {
      expect(ISSUE_TYPE_LABELS[0]).toBe('Task')
      expect(ISSUE_TYPE_LABELS[1]).toBe('Bug')
      expect(ISSUE_TYPE_LABELS[2]).toBe('Chore')
      expect(ISSUE_TYPE_LABELS[3]).toBe('Feature')
      expect(ISSUE_TYPE_LABELS[4]).toBe('Idea')
      expect(ISSUE_TYPE_LABELS[5]).toBe('Verify')
    })

    it('should have labels for all defined types', () => {
      Object.values(ISSUE_TYPE).forEach((value) => {
        expect(ISSUE_TYPE_LABELS[value]).toBeDefined()
        expect(typeof ISSUE_TYPE_LABELS[value]).toBe('string')
      })
    })
  })

  describe('ISSUE_TYPE_OPTIONS', () => {
    it('should have correct value-label pairs', () => {
      const taskOption = ISSUE_TYPE_OPTIONS.find((o) => o.value === '0')
      expect(taskOption?.label).toBe('Task')

      const bugOption = ISSUE_TYPE_OPTIONS.find((o) => o.value === '1')
      expect(bugOption?.label).toBe('Bug')

      const choreOption = ISSUE_TYPE_OPTIONS.find((o) => o.value === '2')
      expect(choreOption?.label).toBe('Chore')

      const featureOption = ISSUE_TYPE_OPTIONS.find((o) => o.value === '3')
      expect(featureOption?.label).toBe('Feature')

      const verifyOption = ISSUE_TYPE_OPTIONS.find((o) => o.value === '5')
      expect(verifyOption?.label).toBe('Verify')
    })
  })

  describe('getTypeLabel', () => {
    it('should return correct labels for valid types', () => {
      expect(getTypeLabel(0)).toBe('Task')
      expect(getTypeLabel(1)).toBe('Bug')
      expect(getTypeLabel(2)).toBe('Chore')
      expect(getTypeLabel(3)).toBe('Feature')
      expect(getTypeLabel(4)).toBe('Idea')
      expect(getTypeLabel(5)).toBe('Verify')
    })

    it('should return Task for undefined or null', () => {
      expect(getTypeLabel(undefined)).toBe('Task')
      expect(getTypeLabel(null)).toBe('Task')
    })

    it('should return Task for invalid type values', () => {
      expect(getTypeLabel(99)).toBe('Task')
      expect(getTypeLabel(-1)).toBe('Task')
    })
  })
})
