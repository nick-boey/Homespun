import { describe, it, expect } from 'vitest'
import type {
  IssueResponse,
  TaskGraphNodeResponse,
  TaskGraphResponse,
} from '@/api/generated/types.gen'
import { aggregateOrphans } from './orphan-aggregation'

function issue(id: string, overrides: Partial<IssueResponse> = {}): IssueResponse {
  return {
    id,
    title: id,
    status: 'open',
    type: 'task',
    workingBranchId: id,
    executionMode: 'series',
    ...overrides,
  }
}

function node(id: string, overrides: Partial<IssueResponse> = {}): TaskGraphNodeResponse {
  return { issue: issue(id, overrides), lane: 0, row: 0, isActionable: false }
}

function graph(overrides: Partial<TaskGraphResponse> = {}): TaskGraphResponse {
  return {
    nodes: null,
    mergedPrs: null,
    mainOrphanChanges: null,
    openSpecStates: null,
    ...overrides,
  }
}

describe('aggregateOrphans', () => {
  it('T004.1 returns empty list for no orphans', () => {
    expect(aggregateOrphans(graph())).toEqual([])
    expect(aggregateOrphans(graph({ mainOrphanChanges: [], openSpecStates: {} }))).toEqual([])
  })

  it('T004.2 main-only orphan -> single row with branch=null, no containing issue', () => {
    const result = aggregateOrphans(
      graph({ mainOrphanChanges: [{ name: 'add-foo', createdOnBranch: false }] })
    )
    expect(result).toEqual([
      {
        name: 'add-foo',
        occurrences: [{ branch: null, changeName: 'add-foo' }],
        containingIssueIds: [],
      },
    ])
  })

  it('T004.3 branch-only orphan under issue X -> single row with the branch + issue id', () => {
    const result = aggregateOrphans(
      graph({
        openSpecStates: {
          X: {
            branchState: 'exists',
            changeState: 'none',
            orphans: [{ name: 'add-foo', createdOnBranch: true }],
          },
        },
        nodes: [node('X')],
      })
    )
    expect(result).toHaveLength(1)
    expect(result[0].name).toBe('add-foo')
    expect(result[0].occurrences).toEqual([{ branch: 'task/X+X', changeName: 'add-foo' }])
    expect(result[0].containingIssueIds).toEqual(['X'])
  })

  it('T004.4 same name on main + one branch -> single deduped row with two occurrences', () => {
    const result = aggregateOrphans(
      graph({
        mainOrphanChanges: [{ name: 'add-foo', createdOnBranch: false }],
        openSpecStates: {
          X: {
            branchState: 'exists',
            changeState: 'none',
            orphans: [{ name: 'add-foo', createdOnBranch: true }],
          },
        },
        nodes: [node('X')],
      })
    )
    expect(result).toHaveLength(1)
    expect(result[0].name).toBe('add-foo')
    expect(result[0].occurrences).toEqual(
      expect.arrayContaining([
        { branch: null, changeName: 'add-foo' },
        { branch: 'task/X+X', changeName: 'add-foo' },
      ])
    )
    expect(result[0].occurrences).toHaveLength(2)
    expect(result[0].containingIssueIds).toEqual(['X'])
  })

  it('T004.5 same name on two branches -> one row with two occurrences, order stable', () => {
    const base = graph({
      openSpecStates: {
        X: {
          branchState: 'exists',
          changeState: 'none',
          orphans: [{ name: 'add-foo', createdOnBranch: true }],
        },
        Y: {
          branchState: 'exists',
          changeState: 'none',
          orphans: [{ name: 'add-foo', createdOnBranch: true }],
        },
      },
      nodes: [node('X'), node('Y')],
    })
    const first = aggregateOrphans(base)
    const second = aggregateOrphans(base)
    expect(first).toHaveLength(1)
    expect(first[0].occurrences).toHaveLength(2)
    expect(first[0].containingIssueIds).toHaveLength(2)
    expect(first[0].containingIssueIds).toEqual(expect.arrayContaining(['X', 'Y']))
    // Determinism: repeated runs produce identical order.
    expect(first).toEqual(second)
  })

  it('T004.6 filters orphans with null/empty name', () => {
    const result = aggregateOrphans(
      graph({
        mainOrphanChanges: [
          { name: null, createdOnBranch: false },
          { name: '', createdOnBranch: false },
          { name: 'keep', createdOnBranch: false },
        ],
      })
    )
    expect(result.map((r) => r.name)).toEqual(['keep'])
  })

  it('returns rows sorted by name', () => {
    const result = aggregateOrphans(
      graph({
        mainOrphanChanges: [
          { name: 'zeta', createdOnBranch: false },
          { name: 'alpha', createdOnBranch: false },
          { name: 'mid', createdOnBranch: false },
        ],
      })
    )
    expect(result.map((r) => r.name)).toEqual(['alpha', 'mid', 'zeta'])
  })
})
