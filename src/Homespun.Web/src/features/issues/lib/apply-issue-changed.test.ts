import { describe, it, expect } from 'vitest'
import { IssueStatus, IssueType, type IssueResponse } from '@/api'
import { applyIssueChanged } from './apply-issue-changed'

function issue(id: string, overrides: Partial<IssueResponse> = {}): IssueResponse {
  return {
    id,
    title: id,
    description: null,
    status: IssueStatus.OPEN,
    type: IssueType.TASK,
    priority: null,
    linkedPRs: null,
    linkedIssues: null,
    parentIssues: null,
    tags: null,
    workingBranchId: null,
    createdBy: null,
    assignedTo: null,
    ...overrides,
  }
}

describe('applyIssueChanged', () => {
  it('inserts a created issue when not in cache', () => {
    const next = applyIssueChanged([issue('a')], {
      kind: 'created',
      issueId: 'b',
      issue: issue('b'),
    })
    expect(next.map((i) => i.id)).toEqual(['a', 'b'])
  })

  it('replaces an updated issue by id', () => {
    const next = applyIssueChanged([issue('a'), issue('b')], {
      kind: 'updated',
      issueId: 'b',
      issue: issue('b', { title: 'updated title' }),
    })
    expect(next).toHaveLength(2)
    expect(next.find((i) => i.id === 'b')?.title).toBe('updated title')
  })

  it('drops a deleted issue', () => {
    const next = applyIssueChanged([issue('a'), issue('b')], {
      kind: 'deleted',
      issueId: 'b',
      issue: null,
    })
    expect(next.map((i) => i.id)).toEqual(['a'])
  })

  it('is idempotent: applying the same created event twice is a no-op', () => {
    const e: Parameters<typeof applyIssueChanged>[1] = {
      kind: 'created',
      issueId: 'b',
      issue: issue('b'),
    }
    const once = applyIssueChanged([issue('a')], e)
    const twice = applyIssueChanged(once, e)
    expect(twice).toEqual(once)
  })

  it('is idempotent: applying the same delete twice is a no-op', () => {
    const e: Parameters<typeof applyIssueChanged>[1] = {
      kind: 'deleted',
      issueId: 'b',
      issue: null,
    }
    const once = applyIssueChanged([issue('a'), issue('b')], e)
    const twice = applyIssueChanged(once, e)
    expect(twice).toEqual(once)
  })

  it('treats updated-with-missing-id like a no-op insert when issue body provided', () => {
    const next = applyIssueChanged([issue('a')], {
      kind: 'updated',
      issueId: 'c',
      issue: issue('c'),
    })
    expect(next.map((i) => i.id)).toEqual(['a', 'c'])
  })

  it('returns a copy when cache is undefined', () => {
    const next = applyIssueChanged(undefined, {
      kind: 'created',
      issueId: 'a',
      issue: issue('a'),
    })
    expect(next).toEqual([issue('a')])
  })

  it('does not mutate the input cache array', () => {
    const cache = [issue('a'), issue('b')]
    const before = cache.slice()
    applyIssueChanged(cache, { kind: 'updated', issueId: 'a', issue: issue('a', { title: 'x' }) })
    expect(cache).toEqual(before)
  })
})
