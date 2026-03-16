import { describe, it, expect } from 'vitest'
import { parseFilterQuery, applyFilter, type ParsedFilter } from './filter-query-parser'
import type { IssueResponse } from '@/api'
import { IssueStatus, IssueType } from '@/api/generated'

// Helper to create a mock issue
function createIssue(overrides: Partial<IssueResponse> = {}): IssueResponse {
  return {
    id: 'abc123',
    title: 'Test Issue',
    description: 'Test description',
    status: IssueStatus.OPEN,
    type: IssueType.TASK,
    priority: 3,
    linkedPRs: [],
    linkedIssues: [],
    parentIssues: [],
    tags: [],
    assignedTo: null,
    ...overrides,
  }
}

describe('parseFilterQuery', () => {
  describe('empty and whitespace inputs', () => {
    it('returns empty filter for empty string', () => {
      const result = parseFilterQuery('')
      expect(result.status).toBeUndefined()
      expect(result.type).toBeUndefined()
      expect(result.freeText).toEqual([])
      expect(result.errors).toEqual([])
    })

    it('returns empty filter for whitespace only', () => {
      const result = parseFilterQuery('   ')
      expect(result.freeText).toEqual([])
      expect(result.errors).toEqual([])
    })
  })

  describe('status filter parsing', () => {
    it('parses status:open', () => {
      const result = parseFilterQuery('status:open')
      expect(result.status).toEqual([IssueStatus.OPEN])
    })

    it('parses status:progress', () => {
      const result = parseFilterQuery('status:progress')
      expect(result.status).toEqual([IssueStatus.PROGRESS])
    })

    it('parses status:inprogress alias', () => {
      const result = parseFilterQuery('status:inprogress')
      expect(result.status).toEqual([IssueStatus.PROGRESS])
    })

    it('parses status:review', () => {
      const result = parseFilterQuery('status:review')
      expect(result.status).toEqual([IssueStatus.REVIEW])
    })

    it('parses status:complete', () => {
      const result = parseFilterQuery('status:complete')
      expect(result.status).toEqual([IssueStatus.COMPLETE])
    })

    it('parses status:done alias', () => {
      const result = parseFilterQuery('status:done')
      expect(result.status).toEqual([IssueStatus.COMPLETE])
    })

    it('parses status:archived', () => {
      const result = parseFilterQuery('status:archived')
      expect(result.status).toEqual([IssueStatus.ARCHIVED])
    })

    it('parses status:closed', () => {
      const result = parseFilterQuery('status:closed')
      expect(result.status).toEqual([IssueStatus.CLOSED])
    })

    it('parses status:draft', () => {
      const result = parseFilterQuery('status:draft')
      expect(result.status).toEqual([IssueStatus.DRAFT])
    })

    it('is case insensitive for status values', () => {
      const result = parseFilterQuery('status:OPEN')
      expect(result.status).toEqual([IssueStatus.OPEN])
    })
  })

  describe('type filter parsing', () => {
    it('parses type:task', () => {
      const result = parseFilterQuery('type:task')
      expect(result.type).toEqual([IssueType.TASK])
    })

    it('parses type:bug', () => {
      const result = parseFilterQuery('type:bug')
      expect(result.type).toEqual([IssueType.BUG])
    })

    it('parses type:fix alias', () => {
      const result = parseFilterQuery('type:fix')
      expect(result.type).toEqual([IssueType.BUG])
    })

    it('parses type:chore', () => {
      const result = parseFilterQuery('type:chore')
      expect(result.type).toEqual([IssueType.CHORE])
    })

    it('parses type:feature', () => {
      const result = parseFilterQuery('type:feature')
      expect(result.type).toEqual([IssueType.FEATURE])
    })

    it('parses type:feat alias', () => {
      const result = parseFilterQuery('type:feat')
      expect(result.type).toEqual([IssueType.FEATURE])
    })

    it('parses type:idea', () => {
      const result = parseFilterQuery('type:idea')
      expect(result.type).toEqual([IssueType.IDEA])
    })

    it('parses type:verify', () => {
      const result = parseFilterQuery('type:verify')
      expect(result.type).toEqual([IssueType.VERIFY])
    })
  })

  describe('priority filter parsing', () => {
    it('parses priority:1', () => {
      const result = parseFilterQuery('priority:1')
      expect(result.priority).toEqual([1])
    })

    it('parses priority:5', () => {
      const result = parseFilterQuery('priority:5')
      expect(result.priority).toEqual([5])
    })

    it('parses priority with p alias', () => {
      const result = parseFilterQuery('p:3')
      expect(result.priority).toEqual([3])
    })
  })

  describe('assignee filter parsing', () => {
    it('parses assigned:john', () => {
      const result = parseFilterQuery('assigned:john')
      expect(result.assignee).toEqual(['john'])
    })

    it('parses assignee alias', () => {
      const result = parseFilterQuery('assignee:alice')
      expect(result.assignee).toEqual(['alice'])
    })
  })

  describe('tag filter parsing', () => {
    it('parses tag:frontend', () => {
      const result = parseFilterQuery('tag:frontend')
      expect(result.tags).toEqual(['frontend'])
    })

    it('parses tags alias', () => {
      const result = parseFilterQuery('tags:backend')
      expect(result.tags).toEqual(['backend'])
    })

    it('parses keyed tags with project=value format', () => {
      const result = parseFilterQuery('project:frontend')
      expect(result.tags).toEqual(['project=frontend'])
    })
  })

  describe('linked PR filter parsing', () => {
    it('parses pr:123', () => {
      const result = parseFilterQuery('pr:123')
      expect(result.linkedPr).toEqual([123])
    })
  })

  describe('id filter parsing', () => {
    it('parses id:abc123', () => {
      const result = parseFilterQuery('id:abc123')
      expect(result.id).toEqual(['abc123'])
    })
  })

  describe('negation handling', () => {
    it('parses -status:open as negated status', () => {
      const result = parseFilterQuery('-status:open')
      expect(result.statusNegated).toEqual([IssueStatus.OPEN])
      expect(result.status).toBeUndefined()
    })

    it('parses -type:bug as negated type', () => {
      const result = parseFilterQuery('-type:bug')
      expect(result.typeNegated).toEqual([IssueType.BUG])
      expect(result.type).toBeUndefined()
    })
  })

  describe('multi-value parsing', () => {
    it('parses multiple status values with comma-semicolon', () => {
      const result = parseFilterQuery('status:open,progress;')
      expect(result.status).toEqual([IssueStatus.OPEN, IssueStatus.PROGRESS])
    })

    it('parses multiple type values', () => {
      const result = parseFilterQuery('type:bug,feature;')
      expect(result.type).toEqual([IssueType.BUG, IssueType.FEATURE])
    })

    it('parses multiple values without trailing semicolon', () => {
      const result = parseFilterQuery('status:open,progress')
      expect(result.status).toEqual([IssueStatus.OPEN, IssueStatus.PROGRESS])
    })
  })

  describe('free text extraction', () => {
    it('extracts unrecognized words as free text', () => {
      const result = parseFilterQuery('login button')
      expect(result.freeText).toEqual(['login', 'button'])
    })

    it('extracts free text alongside filters', () => {
      const result = parseFilterQuery('status:open login')
      expect(result.status).toEqual([IssueStatus.OPEN])
      expect(result.freeText).toEqual(['login'])
    })
  })

  describe('combination filters', () => {
    it('parses status and type together', () => {
      const result = parseFilterQuery('status:open type:bug')
      expect(result.status).toEqual([IssueStatus.OPEN])
      expect(result.type).toEqual([IssueType.BUG])
    })

    it('parses multiple different filters', () => {
      const result = parseFilterQuery('status:open type:bug priority:1 assigned:john')
      expect(result.status).toEqual([IssueStatus.OPEN])
      expect(result.type).toEqual([IssueType.BUG])
      expect(result.priority).toEqual([1])
      expect(result.assignee).toEqual(['john'])
    })

    it('parses mixed negated and positive filters', () => {
      const result = parseFilterQuery('status:open -type:bug')
      expect(result.status).toEqual([IssueStatus.OPEN])
      expect(result.typeNegated).toEqual([IssueType.BUG])
    })
  })

  describe('error handling', () => {
    it('adds error for unknown status value', () => {
      const result = parseFilterQuery('status:unknown')
      expect(result.errors).toContain("Unknown status value: 'unknown'")
    })

    it('adds error for unknown type value', () => {
      const result = parseFilterQuery('type:unknown')
      expect(result.errors).toContain("Unknown type value: 'unknown'")
    })

    it('adds error for invalid priority value', () => {
      const result = parseFilterQuery('priority:abc')
      expect(result.errors).toContain("Invalid priority value: 'abc'")
    })
  })
})

describe('applyFilter', () => {
  describe('empty filter', () => {
    it('returns true for any issue when filter is empty', () => {
      const issue = createIssue()
      const filter: ParsedFilter = { freeText: [], errors: [] }
      expect(applyFilter(issue, filter)).toBe(true)
    })
  })

  describe('status filtering', () => {
    it('matches issue with matching status', () => {
      const issue = createIssue({ status: IssueStatus.OPEN })
      const filter: ParsedFilter = { status: [IssueStatus.OPEN], freeText: [], errors: [] }
      expect(applyFilter(issue, filter)).toBe(true)
    })

    it('does not match issue with different status', () => {
      const issue = createIssue({ status: IssueStatus.PROGRESS })
      const filter: ParsedFilter = { status: [IssueStatus.OPEN], freeText: [], errors: [] }
      expect(applyFilter(issue, filter)).toBe(false)
    })

    it('matches issue when status is in multi-value list', () => {
      const issue = createIssue({ status: IssueStatus.PROGRESS })
      const filter: ParsedFilter = {
        status: [IssueStatus.OPEN, IssueStatus.PROGRESS],
        freeText: [],
        errors: [],
      }
      expect(applyFilter(issue, filter)).toBe(true)
    })

    it('excludes issue with negated status', () => {
      const issue = createIssue({ status: IssueStatus.OPEN })
      const filter: ParsedFilter = { statusNegated: [IssueStatus.OPEN], freeText: [], errors: [] }
      expect(applyFilter(issue, filter)).toBe(false)
    })

    it('includes issue when not in negated status list', () => {
      const issue = createIssue({ status: IssueStatus.PROGRESS })
      const filter: ParsedFilter = { statusNegated: [IssueStatus.OPEN], freeText: [], errors: [] }
      expect(applyFilter(issue, filter)).toBe(true)
    })
  })

  describe('type filtering', () => {
    it('matches issue with matching type', () => {
      const issue = createIssue({ type: IssueType.BUG })
      const filter: ParsedFilter = { type: [IssueType.BUG], freeText: [], errors: [] }
      expect(applyFilter(issue, filter)).toBe(true)
    })

    it('does not match issue with different type', () => {
      const issue = createIssue({ type: IssueType.TASK })
      const filter: ParsedFilter = { type: [IssueType.BUG], freeText: [], errors: [] }
      expect(applyFilter(issue, filter)).toBe(false)
    })

    it('excludes issue with negated type', () => {
      const issue = createIssue({ type: IssueType.BUG })
      const filter: ParsedFilter = { typeNegated: [IssueType.BUG], freeText: [], errors: [] }
      expect(applyFilter(issue, filter)).toBe(false)
    })
  })

  describe('priority filtering', () => {
    it('matches issue with matching priority', () => {
      const issue = createIssue({ priority: 3 })
      const filter: ParsedFilter = { priority: [3], freeText: [], errors: [] }
      expect(applyFilter(issue, filter)).toBe(true)
    })

    it('does not match issue with different priority', () => {
      const issue = createIssue({ priority: 1 })
      const filter: ParsedFilter = { priority: [3], freeText: [], errors: [] }
      expect(applyFilter(issue, filter)).toBe(false)
    })

    it('handles null priority', () => {
      const issue = createIssue({ priority: null })
      const filter: ParsedFilter = { priority: [3], freeText: [], errors: [] }
      expect(applyFilter(issue, filter)).toBe(false)
    })
  })

  describe('assignee filtering', () => {
    it('matches issue with matching assignee', () => {
      const issue = createIssue({ assignedTo: 'john@example.com' })
      const filter: ParsedFilter = { assignee: ['john'], freeText: [], errors: [] }
      expect(applyFilter(issue, filter)).toBe(true)
    })

    it('does not match issue with different assignee', () => {
      const issue = createIssue({ assignedTo: 'alice@example.com' })
      const filter: ParsedFilter = { assignee: ['john'], freeText: [], errors: [] }
      expect(applyFilter(issue, filter)).toBe(false)
    })

    it('does not match issue with no assignee', () => {
      const issue = createIssue({ assignedTo: null })
      const filter: ParsedFilter = { assignee: ['john'], freeText: [], errors: [] }
      expect(applyFilter(issue, filter)).toBe(false)
    })
  })

  describe('tag filtering', () => {
    it('matches issue with matching tag', () => {
      const issue = createIssue({ tags: ['frontend', 'urgent'] })
      const filter: ParsedFilter = { tags: ['frontend'], freeText: [], errors: [] }
      expect(applyFilter(issue, filter)).toBe(true)
    })

    it('does not match issue without matching tag', () => {
      const issue = createIssue({ tags: ['backend'] })
      const filter: ParsedFilter = { tags: ['frontend'], freeText: [], errors: [] }
      expect(applyFilter(issue, filter)).toBe(false)
    })

    it('matches keyed tag with exact format', () => {
      const issue = createIssue({ tags: ['project=frontend', 'priority=high'] })
      const filter: ParsedFilter = { tags: ['project=frontend'], freeText: [], errors: [] }
      expect(applyFilter(issue, filter)).toBe(true)
    })
  })

  describe('linked PR filtering', () => {
    it('matches issue with matching PR', () => {
      const issue = createIssue({ linkedPRs: [123, 456] })
      const filter: ParsedFilter = { linkedPr: [123], freeText: [], errors: [] }
      expect(applyFilter(issue, filter)).toBe(true)
    })

    it('does not match issue without matching PR', () => {
      const issue = createIssue({ linkedPRs: [789] })
      const filter: ParsedFilter = { linkedPr: [123], freeText: [], errors: [] }
      expect(applyFilter(issue, filter)).toBe(false)
    })
  })

  describe('id filtering', () => {
    it('matches issue with matching id prefix', () => {
      const issue = createIssue({ id: 'abc123def' })
      const filter: ParsedFilter = { id: ['abc123'], freeText: [], errors: [] }
      expect(applyFilter(issue, filter)).toBe(true)
    })

    it('does not match issue with different id', () => {
      const issue = createIssue({ id: 'xyz789' })
      const filter: ParsedFilter = { id: ['abc123'], freeText: [], errors: [] }
      expect(applyFilter(issue, filter)).toBe(false)
    })
  })

  describe('free text filtering', () => {
    it('matches issue with free text in title', () => {
      const issue = createIssue({ title: 'Fix login button issue' })
      const filter: ParsedFilter = { freeText: ['login'], errors: [] }
      expect(applyFilter(issue, filter)).toBe(true)
    })

    it('matches issue with free text in description', () => {
      const issue = createIssue({ description: 'This affects the login flow' })
      const filter: ParsedFilter = { freeText: ['login'], errors: [] }
      expect(applyFilter(issue, filter)).toBe(true)
    })

    it('matches issue with free text in id', () => {
      const issue = createIssue({ id: 'abc123' })
      const filter: ParsedFilter = { freeText: ['abc'], errors: [] }
      expect(applyFilter(issue, filter)).toBe(true)
    })

    it('matches issue with free text in tags', () => {
      const issue = createIssue({ tags: ['frontend-login'] })
      const filter: ParsedFilter = { freeText: ['login'], errors: [] }
      expect(applyFilter(issue, filter)).toBe(true)
    })

    it('is case insensitive', () => {
      const issue = createIssue({ title: 'Fix LOGIN Button' })
      const filter: ParsedFilter = { freeText: ['login'], errors: [] }
      expect(applyFilter(issue, filter)).toBe(true)
    })

    it('requires all free text words to match (AND logic)', () => {
      const issue = createIssue({ title: 'Fix login button' })
      const filter: ParsedFilter = { freeText: ['login', 'submit'], errors: [] }
      expect(applyFilter(issue, filter)).toBe(false)
    })

    it('matches when all free text words are found', () => {
      const issue = createIssue({ title: 'Fix login button' })
      const filter: ParsedFilter = { freeText: ['login', 'button'], errors: [] }
      expect(applyFilter(issue, filter)).toBe(true)
    })
  })

  describe('combined filters', () => {
    it('applies AND logic between different filter types', () => {
      const issue = createIssue({
        status: IssueStatus.OPEN,
        type: IssueType.BUG,
        title: 'Fix login bug',
      })
      const filter: ParsedFilter = {
        status: [IssueStatus.OPEN],
        type: [IssueType.BUG],
        freeText: ['login'],
        errors: [],
      }
      expect(applyFilter(issue, filter)).toBe(true)
    })

    it('fails when any filter does not match', () => {
      const issue = createIssue({
        status: IssueStatus.OPEN,
        type: IssueType.TASK,
        title: 'Fix login',
      })
      const filter: ParsedFilter = {
        status: [IssueStatus.OPEN],
        type: [IssueType.BUG], // Expects Bug
        freeText: [],
        errors: [],
      }
      expect(applyFilter(issue, filter)).toBe(false)
    })
  })
})
