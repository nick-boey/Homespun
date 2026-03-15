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
import { IssueStatus, IssueType } from '@/api'

describe('Issue Status Constants', () => {
  describe('ISSUE_STATUS', () => {
    it('should have correct string enum values matching C# Fleece.Core.Models.IssueStatus', () => {
      // These values MUST match the C# enum to ensure React displays match Blazor
      expect(ISSUE_STATUS.DRAFT).toBe('draft')
      expect(ISSUE_STATUS.OPEN).toBe('open')
      expect(ISSUE_STATUS.PROGRESS).toBe('progress')
      expect(ISSUE_STATUS.REVIEW).toBe('review')
      expect(ISSUE_STATUS.COMPLETE).toBe('complete')
      expect(ISSUE_STATUS.ARCHIVED).toBe('archived')
      expect(ISSUE_STATUS.CLOSED).toBe('closed')
      expect(ISSUE_STATUS.DELETED).toBe('deleted')
    })
  })

  describe('ISSUE_STATUS_LABELS', () => {
    it('should have labels for all status values', () => {
      expect(ISSUE_STATUS_LABELS[IssueStatus.DRAFT]).toBe('Draft')
      expect(ISSUE_STATUS_LABELS[IssueStatus.OPEN]).toBe('Open')
      expect(ISSUE_STATUS_LABELS[IssueStatus.PROGRESS]).toBe('In Progress')
      expect(ISSUE_STATUS_LABELS[IssueStatus.REVIEW]).toBe('Review')
      expect(ISSUE_STATUS_LABELS[IssueStatus.COMPLETE]).toBe('Complete')
      expect(ISSUE_STATUS_LABELS[IssueStatus.ARCHIVED]).toBe('Archived')
      expect(ISSUE_STATUS_LABELS[IssueStatus.CLOSED]).toBe('Closed')
      expect(ISSUE_STATUS_LABELS[IssueStatus.DELETED]).toBe('Deleted')
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
      const draftOption = ISSUE_STATUS_OPTIONS.find((o) => o.value === IssueStatus.DRAFT)
      expect(draftOption?.label).toBe('Draft')

      const openOption = ISSUE_STATUS_OPTIONS.find((o) => o.value === IssueStatus.OPEN)
      expect(openOption?.label).toBe('Open')

      const progressOption = ISSUE_STATUS_OPTIONS.find((o) => o.value === IssueStatus.PROGRESS)
      expect(progressOption?.label).toBe('In Progress')

      const reviewOption = ISSUE_STATUS_OPTIONS.find((o) => o.value === IssueStatus.REVIEW)
      expect(reviewOption?.label).toBe('Review')

      const completeOption = ISSUE_STATUS_OPTIONS.find((o) => o.value === IssueStatus.COMPLETE)
      expect(completeOption?.label).toBe('Complete')
    })

    it('should not include Deleted status in dropdown options', () => {
      // Cast to string since ISSUE_STATUS_OPTIONS is a typed array that excludes DELETED
      const statusValues = ISSUE_STATUS_OPTIONS.map((o) => o.value as string)
      expect(statusValues).not.toContain(IssueStatus.DELETED)
    })
  })

  describe('getStatusLabel', () => {
    it('should return correct labels for valid statuses', () => {
      expect(getStatusLabel(IssueStatus.DRAFT)).toBe('Draft')
      expect(getStatusLabel(IssueStatus.OPEN)).toBe('Open')
      expect(getStatusLabel(IssueStatus.PROGRESS)).toBe('In Progress')
      expect(getStatusLabel(IssueStatus.REVIEW)).toBe('Review')
      expect(getStatusLabel(IssueStatus.COMPLETE)).toBe('Complete')
      expect(getStatusLabel(IssueStatus.ARCHIVED)).toBe('Archived')
      expect(getStatusLabel(IssueStatus.CLOSED)).toBe('Closed')
      expect(getStatusLabel(IssueStatus.DELETED)).toBe('Deleted')
    })

    it('should return Unknown for undefined or null', () => {
      expect(getStatusLabel(undefined)).toBe('Unknown')
      expect(getStatusLabel(null)).toBe('Unknown')
    })

    it('should return Unknown for invalid status values', () => {
      expect(getStatusLabel('invalid' as IssueStatus)).toBe('Unknown')
    })
  })

  describe('getStatusColorClass', () => {
    it('should return color classes for valid statuses', () => {
      expect(getStatusColorClass(IssueStatus.DRAFT)).toContain('gray') // Draft
      expect(getStatusColorClass(IssueStatus.OPEN)).toContain('blue') // Open
      expect(getStatusColorClass(IssueStatus.PROGRESS)).toContain('yellow') // Progress
      expect(getStatusColorClass(IssueStatus.REVIEW)).toContain('purple') // Review
      expect(getStatusColorClass(IssueStatus.COMPLETE)).toContain('green') // Complete
    })

    it('should return default color for undefined or null', () => {
      const defaultColor = getStatusColorClass(undefined)
      expect(defaultColor).toBe(ISSUE_STATUS_COLORS[ISSUE_STATUS.DRAFT])
      expect(getStatusColorClass(null)).toBe(ISSUE_STATUS_COLORS[ISSUE_STATUS.DRAFT])
    })
  })
})

describe('Issue Type Constants', () => {
  describe('ISSUE_TYPE', () => {
    it('should have correct string enum values matching C# Fleece.Core.Models.IssueType', () => {
      // These values MUST match the C# enum to ensure React displays match Blazor
      expect(ISSUE_TYPE.TASK).toBe('task')
      expect(ISSUE_TYPE.BUG).toBe('bug')
      expect(ISSUE_TYPE.CHORE).toBe('chore')
      expect(ISSUE_TYPE.FEATURE).toBe('feature')
      expect(ISSUE_TYPE.IDEA).toBe('idea')
      expect(ISSUE_TYPE.VERIFY).toBe('verify')
    })
  })

  describe('ISSUE_TYPE_LABELS', () => {
    it('should have labels for all type values', () => {
      expect(ISSUE_TYPE_LABELS[IssueType.TASK]).toBe('Task')
      expect(ISSUE_TYPE_LABELS[IssueType.BUG]).toBe('Bug')
      expect(ISSUE_TYPE_LABELS[IssueType.CHORE]).toBe('Chore')
      expect(ISSUE_TYPE_LABELS[IssueType.FEATURE]).toBe('Feature')
      expect(ISSUE_TYPE_LABELS[IssueType.IDEA]).toBe('Idea')
      expect(ISSUE_TYPE_LABELS[IssueType.VERIFY]).toBe('Verify')
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
      const taskOption = ISSUE_TYPE_OPTIONS.find((o) => o.value === IssueType.TASK)
      expect(taskOption?.label).toBe('Task')

      const bugOption = ISSUE_TYPE_OPTIONS.find((o) => o.value === IssueType.BUG)
      expect(bugOption?.label).toBe('Bug')

      const choreOption = ISSUE_TYPE_OPTIONS.find((o) => o.value === IssueType.CHORE)
      expect(choreOption?.label).toBe('Chore')

      const featureOption = ISSUE_TYPE_OPTIONS.find((o) => o.value === IssueType.FEATURE)
      expect(featureOption?.label).toBe('Feature')

      const verifyOption = ISSUE_TYPE_OPTIONS.find((o) => o.value === IssueType.VERIFY)
      expect(verifyOption?.label).toBe('Verify')
    })
  })

  describe('getTypeLabel', () => {
    it('should return correct labels for valid types', () => {
      expect(getTypeLabel(IssueType.TASK)).toBe('Task')
      expect(getTypeLabel(IssueType.BUG)).toBe('Bug')
      expect(getTypeLabel(IssueType.CHORE)).toBe('Chore')
      expect(getTypeLabel(IssueType.FEATURE)).toBe('Feature')
      expect(getTypeLabel(IssueType.IDEA)).toBe('Idea')
      expect(getTypeLabel(IssueType.VERIFY)).toBe('Verify')
    })

    it('should return Task for undefined or null', () => {
      expect(getTypeLabel(undefined)).toBe('Task')
      expect(getTypeLabel(null)).toBe('Task')
    })

    it('should return Task for invalid type values', () => {
      expect(getTypeLabel('invalid' as IssueType)).toBe('Task')
    })
  })
})
