import { describe, it, expect } from 'vitest'
import {
  InvalidGraphError,
  isDone,
  isTerminal,
  layoutForNext,
  layoutForTree,
} from './issue-layout-service'
import type { LayoutIssue } from './issue-layout-service'
import { isIssueNode, isPhaseNode, phaseNodeId, type LayoutPhase } from './nodes'

const issue = (overrides: Partial<LayoutIssue> & Pick<LayoutIssue, 'id'>): LayoutIssue => ({
  title: overrides.id,
  status: 'open',
  executionMode: 'series',
  parentIssues: [],
  createdAt: '2026-01-01T00:00:00Z',
  ...overrides,
})

const parent = (id: string, sortOrder = 'aaa', active = true) => ({
  parentIssue: id,
  sortOrder,
  active,
})

describe('IssueLayoutService.layoutForTree', () => {
  it('returns empty layout for empty input', () => {
    const r = layoutForTree([])
    expect(r.ok).toBe(true)
    if (!r.ok) return
    expect(r.layout.nodes).toEqual([])
  })

  it('emits open issues; suppresses terminal-only issues by default', () => {
    const r = layoutForTree([
      issue({ id: 'A', status: 'open' }),
      issue({ id: 'B', status: 'closed' }),
    ])
    expect(r.ok).toBe(true)
    if (!r.ok) return
    expect(r.layout.nodes.map((n) => n.node.id)).toEqual(['A'])
  })

  it('pulls in terminal ancestors of open issues so the chain stays connected', () => {
    const r = layoutForTree([
      issue({ id: 'GP', status: 'closed' }),
      issue({ id: 'P', status: 'closed', parentIssues: [parent('GP')] }),
      issue({ id: 'C', status: 'open', parentIssues: [parent('P')] }),
    ])
    expect(r.ok).toBe(true)
    if (!r.ok) return
    const ids = r.layout.nodes.map((n) => n.node.id).sort()
    expect(ids).toEqual(['C', 'GP', 'P'])
  })

  it('orders series siblings by their SortOrder under that parent', () => {
    const r = layoutForTree([
      issue({ id: 'P', executionMode: 'series' }),
      issue({ id: 'X', parentIssues: [parent('P', 'c')] }),
      issue({ id: 'Y', parentIssues: [parent('P', 'a')] }),
      issue({ id: 'Z', parentIssues: [parent('P', 'b')] }),
    ])
    expect(r.ok).toBe(true)
    if (!r.ok) return
    // Children are emitted bottom-up in series-sort order: Y (a), Z (b), X (c), then P.
    expect(r.layout.nodes.map((n) => n.node.id)).toEqual(['Y', 'Z', 'X', 'P'])
  })

  it('throws InvalidGraphError on cycles in active parent chain', () => {
    expect(() =>
      layoutForTree([
        issue({ id: 'A', parentIssues: [parent('B')] }),
        issue({ id: 'B', parentIssues: [parent('A')] }),
      ])
    ).toThrow(InvalidGraphError)
  })

  it('skips inactive parent refs when collapsing the parent chain', () => {
    const r = layoutForTree([
      issue({ id: 'P', status: 'open' }),
      issue({
        id: 'C',
        status: 'open',
        parentIssues: [parent('P', 'aaa', false)],
      }),
    ])
    expect(r.ok).toBe(true)
    if (!r.ok) return
    // C's only parent is inactive → C becomes a root (P also a root).
    expect(r.layout.nodes.map((n) => n.node.id).sort()).toEqual(['C', 'P'])
    // No edges since they aren't linked.
    expect(r.layout.edges).toEqual([])
  })

  it('filters by assignee when provided', () => {
    const r = layoutForTree(
      [
        issue({ id: 'A', assignedTo: 'alice@example.com' }),
        issue({ id: 'B', assignedTo: 'bob@example.com' }),
      ],
      { assignedTo: 'ALICE@example.com' }
    )
    expect(r.ok).toBe(true)
    if (!r.ok) return
    expect(r.layout.nodes.map((n) => n.node.id)).toEqual(['A'])
  })

  it('collapses done parent if its children are also done (incomplete-children trim)', () => {
    // P (open, parallel) → C (done, no kids) — C should be hidden because the
    // tree mode trims children that have no active descendants.
    const r = layoutForTree([
      issue({ id: 'P', status: 'open', executionMode: 'parallel' }),
      issue({ id: 'C', status: 'closed', parentIssues: [parent('P')] }),
    ])
    expect(r.ok).toBe(true)
    if (!r.ok) return
    expect(r.layout.nodes.map((n) => n.node.id)).toEqual(['P'])
  })
})

describe('IssueLayoutService.layoutForNext', () => {
  it('falls back to layoutForTree when matchedIds is null', () => {
    const r = layoutForNext(
      [issue({ id: 'A', status: 'open' }), issue({ id: 'B', status: 'closed' })],
      null
    )
    expect(r.ok).toBe(true)
    if (!r.ok) return
    expect(r.layout.nodes.map((n) => n.node.id)).toEqual(['A'])
  })

  it('starts from matchedIds and pulls in their ancestors', () => {
    const r = layoutForNext(
      [
        issue({ id: 'Root' }),
        issue({ id: 'Mid', parentIssues: [parent('Root')] }),
        issue({ id: 'Leaf', parentIssues: [parent('Mid')] }),
        issue({ id: 'Other' }),
      ],
      new Set(['Leaf'])
    )
    expect(r.ok).toBe(true)
    if (!r.ok) return
    const ids = r.layout.nodes.map((n) => n.node.id).sort()
    expect(ids).toEqual(['Leaf', 'Mid', 'Root'])
    // 'Other' should not appear.
    expect(ids.includes('Other')).toBe(false)
  })

  it('returns empty when matchedIds is empty', () => {
    const r = layoutForNext([issue({ id: 'A', status: 'open' })], new Set())
    expect(r.ok).toBe(true)
    if (!r.ok) return
    expect(r.layout.nodes).toEqual([])
  })
})

describe('IssueLayoutService phase nodes', () => {
  const phase = (name: string, done = 0, total = 1): LayoutPhase => ({
    name,
    done,
    total,
    tasks: [],
  })

  it('tree mode: phase nodes are emitted at parent.lane + 1 in a series chain right after the parent', () => {
    // normalTree mode emits parent first, then each child at parent.lane + 1.
    // With phases as series-leaf children of an issue with no real children,
    // the phases land in a single column directly below the parent.
    const phases = new Map<string, readonly LayoutPhase[]>([
      ['p1', [phase('Alpha'), phase('Beta'), phase('Gamma')]],
    ])
    const r = layoutForTree([issue({ id: 'P1', status: 'open' })], {
      mode: 'normalTree',
      phases,
    })
    expect(r.ok).toBe(true)
    if (!r.ok) return

    const positioned = r.layout.nodes
    expect(positioned).toHaveLength(4)
    expect(positioned[0].node.id).toBe('P1')
    expect(isIssueNode(positioned[0].node)).toBe(true)
    const issueLane = positioned[0].lane

    const phaseRows = positioned.slice(1)
    expect(phaseRows.map((p) => isPhaseNode(p.node))).toEqual([true, true, true])
    expect(phaseRows.map((p) => p.lane)).toEqual([issueLane + 1, issueLane + 1, issueLane + 1])
    expect(phaseRows.map((p) => p.node.id)).toEqual([
      phaseNodeId('P1', 'Alpha'),
      phaseNodeId('P1', 'Beta'),
      phaseNodeId('P1', 'Gamma'),
    ])
  })

  it('next mode: phases are emitted bottom-up before the parent in a single column', () => {
    // issueGraph mode: leaf siblings of a series parent stack at startLane and
    // the parent emits at startLane + 1. With no real children, the phases
    // occupy lane 0 and the issue lands at lane 1.
    const phases = new Map<string, readonly LayoutPhase[]>([
      ['p1', [phase('Alpha'), phase('Beta'), phase('Gamma')]],
    ])
    const r = layoutForNext([issue({ id: 'P1', status: 'open' })], new Set(['P1']), {
      mode: 'issueGraph',
      phases,
    })
    expect(r.ok).toBe(true)
    if (!r.ok) return

    const positioned = r.layout.nodes
    expect(positioned).toHaveLength(4)
    // Phases emitted first (bottom-up DFS), parent last.
    expect(positioned.slice(0, 3).every((p) => isPhaseNode(p.node))).toBe(true)
    expect(isIssueNode(positioned[3].node)).toBe(true)
    expect(positioned[3].node.id).toBe('P1')

    // All phases share a single lane.
    const phaseLanes = new Set(positioned.slice(0, 3).map((p) => p.lane))
    expect(phaseLanes.size).toBe(1)

    // Parent sits one lane above the phase column.
    expect(positioned[3].lane).toBe(positioned[0].lane + 1)
  })

  it('next mode: phase column never collides with sibling subtree column', () => {
    // Two sibling issues, both with phases, no parent linking them. The
    // engine assigns each subtree its own lane range so the second subtree's
    // column doesn't overlap with the first's phase column.
    const phases = new Map<string, readonly LayoutPhase[]>([
      ['a', [phase('a1'), phase('a2')]],
      ['b', [phase('b1'), phase('b2')]],
    ])
    const r = layoutForNext(
      [issue({ id: 'A', status: 'open' }), issue({ id: 'B', status: 'open' })],
      new Set(['A', 'B']),
      { mode: 'issueGraph', phases }
    )
    expect(r.ok).toBe(true)
    if (!r.ok) return

    // Each (row, lane) cell must be unique — no two emitted nodes share a slot.
    const cells = new Set<string>()
    for (const p of r.layout.nodes) {
      const key = `${p.row}|${p.lane}`
      expect(cells.has(key)).toBe(false)
      cells.add(key)
    }
  })

  it('next mode: edges from phases to parent use engine-emitted attach geometry', () => {
    const phases = new Map<string, readonly LayoutPhase[]>([
      ['p1', [phase('Alpha'), phase('Beta')]],
    ])
    const r = layoutForNext([issue({ id: 'P1', status: 'open' })], new Set(['P1']), {
      mode: 'issueGraph',
      phases,
    })
    expect(r.ok).toBe(true)
    if (!r.ok) return

    const cornerEdges = r.layout.edges.filter((e) => e.kind === 'seriesCornerToParent')
    expect(cornerEdges).toHaveLength(1)
    expect(cornerEdges[0].to.id).toBe('P1')
    expect(cornerEdges[0].sourceAttach).toBe('bottom')
    expect(cornerEdges[0].targetAttach).toBe('left')
  })
})

describe('isDone / isTerminal', () => {
  it('marks complete/archived/closed as done; deleted as terminal-only', () => {
    expect(isDone('complete')).toBe(true)
    expect(isDone('archived')).toBe(true)
    expect(isDone('closed')).toBe(true)
    expect(isDone('deleted')).toBe(false)
    expect(isTerminal('deleted')).toBe(true)
    expect(isTerminal('open')).toBe(false)
    expect(isTerminal('progress')).toBe(false)
  })
})
