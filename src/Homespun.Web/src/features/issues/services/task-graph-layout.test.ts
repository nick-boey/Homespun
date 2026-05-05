import { describe, it, expect } from 'vitest'
import {
  computeLayout,
  computeLayoutFromIssues,
  getRenderKey,
  TaskGraphMarkerType,
  type TaskGraphIssueRenderLine,
  type TaskGraphPhaseRenderLine,
  isIssueRenderLine,
  isPhaseRenderLine,
  isPrRenderLine,
  isSeparatorRenderLine,
  isLoadMoreRenderLine,
} from './task-graph-layout'
import type {
  TaskGraphResponse,
  TaskGraphNodeResponse,
  TaskGraphEdgeResponse,
  IssueOpenSpecState,
  IssueResponse,
} from '@/api'
import { IssueStatus, IssueType, ExecutionMode } from '@/api'
import { ViewMode } from '../types'

function createIssue(overrides: Partial<IssueResponse> = {}): IssueResponse {
  return {
    id: 'test-123',
    title: 'Test Issue',
    description: 'Test description',
    status: IssueStatus.OPEN,
    type: IssueType.TASK,
    parentIssues: [],
    executionMode: ExecutionMode.SERIES,
    ...overrides,
  }
}

function createNode(
  issue: IssueResponse,
  lane: number,
  row: number,
  overrides: Partial<TaskGraphNodeResponse> = {}
): TaskGraphNodeResponse {
  return {
    issue,
    lane,
    row,
    isActionable: false,
    appearanceIndex: 1,
    totalAppearances: 1,
    ...overrides,
  }
}

function createGraph(
  nodes: TaskGraphNodeResponse[],
  overrides: Partial<TaskGraphResponse> = {}
): TaskGraphResponse {
  const totalLanes = nodes.length === 0 ? 0 : Math.max(...nodes.map((n) => (n.lane ?? 0) + 1))
  const totalRows = nodes.length === 0 ? 0 : Math.max(...nodes.map((n) => (n.row ?? 0) + 1))
  return {
    nodes,
    edges: [],
    totalLanes,
    totalRows,
    mergedPrs: [],
    hasMorePastPrs: false,
    totalPastPrsShown: 0,
    agentStatuses: {},
    linkedPrs: {},
    openSpecStates: {},
    mainOrphanChanges: [],
    ...overrides,
  }
}

describe('computeLayout', () => {
  describe('basic shape', () => {
    it('returns empty lines + edges for null taskGraph', () => {
      expect(computeLayout(null)).toEqual({ lines: [], edges: [] })
      expect(computeLayout(undefined)).toEqual({ lines: [], edges: [] })
    })

    it('returns empty lines + edges when both nodes and PRs are empty', () => {
      expect(computeLayout(createGraph([]))).toEqual({ lines: [], edges: [] })
    })

    it('emits exactly one issue line per PositionedNode', () => {
      const a = createNode(createIssue({ id: 'a', title: 'A' }), 0, 0)
      const b = createNode(createIssue({ id: 'b', title: 'B' }), 1, 1)
      const c = createNode(createIssue({ id: 'c', title: 'C' }), 1, 2)
      const { lines } = computeLayout(createGraph([a, b, c]))
      const issueLines = lines.filter(isIssueRenderLine)
      expect(issueLines).toHaveLength(3)
      expect(issueLines.map((l) => l.issueId)).toEqual(['a', 'b', 'c'])
    })

    it('orders lines by row regardless of node array order', () => {
      const second = createNode(createIssue({ id: 'second' }), 0, 1)
      const first = createNode(createIssue({ id: 'first' }), 0, 0)
      const { lines } = computeLayout(createGraph([second, first]))
      const issueLines = lines.filter(isIssueRenderLine)
      expect(issueLines.map((l) => l.issueId)).toEqual(['first', 'second'])
    })
  })

  describe('issue line population', () => {
    it('copies lane, executionMode, parentIssueId, appearance fields from the node', () => {
      const issue = createIssue({
        id: 'a',
        title: 'A',
        executionMode: ExecutionMode.PARALLEL,
        parentIssues: [{ parentIssue: 'parent-id', sortOrder: 'aaa' }],
      })
      const node = createNode(issue, 2, 5, {
        appearanceIndex: 1,
        totalAppearances: 3,
        isActionable: true,
      })
      const { lines } = computeLayout(createGraph([node]))
      const [line] = lines.filter(isIssueRenderLine)
      expect(line.lane).toBe(2)
      expect(line.executionMode).toBe(ExecutionMode.PARALLEL)
      expect(line.parentIssueId).toBe('parent-id')
      expect(line.parentIssues).toEqual([{ parentIssue: 'parent-id', sortOrder: 'aaa' }])
      expect(line.appearanceIndex).toBe(1)
      expect(line.totalAppearances).toBe(3)
      expect(line.marker).toBe(TaskGraphMarkerType.Actionable)
    })

    it('maps status to marker', () => {
      const cases: Array<[IssueStatus, TaskGraphMarkerType]> = [
        [IssueStatus.COMPLETE, TaskGraphMarkerType.Complete],
        [IssueStatus.CLOSED, TaskGraphMarkerType.Closed],
        [IssueStatus.ARCHIVED, TaskGraphMarkerType.Closed],
        [IssueStatus.OPEN, TaskGraphMarkerType.Open],
      ]
      for (const [status, expected] of cases) {
        const node = createNode(createIssue({ id: status, status }), 0, 0)
        const { lines } = computeLayout(createGraph([node]))
        const [line] = lines.filter(isIssueRenderLine)
        expect(line.marker, status).toBe(expected)
      }
    })

    it('treats null parentIssues as no parent', () => {
      const node = createNode(createIssue({ id: 'a', parentIssues: [] }), 0, 0)
      const { lines } = computeLayout(createGraph([node]))
      const [line] = lines.filter(isIssueRenderLine)
      expect(line.parentIssueId).toBeNull()
      expect(line.parentIssues).toBeNull()
    })
  })

  describe('edges', () => {
    it('passes the response edges through unchanged', () => {
      const edge: TaskGraphEdgeResponse = {
        from: 'a',
        to: 'b',
        kind: 'SeriesSibling',
        startRow: 0,
        startLane: 0,
        endRow: 1,
        endLane: 0,
        pivotLane: null,
        sourceAttach: 'Bottom',
        targetAttach: 'Top',
      }
      const node = createNode(createIssue({ id: 'a' }), 0, 0)
      const { edges } = computeLayout(createGraph([node], { edges: [edge] }))
      expect(edges).toHaveLength(1)
      expect(edges[0]).toMatchObject({
        from: 'a',
        to: 'b',
        kind: 'SeriesSibling',
        sourceAttach: 'Bottom',
        targetAttach: 'Top',
      })
    })

    it('returns empty edges when none are supplied', () => {
      const node = createNode(createIssue({ id: 'a' }), 0, 0)
      const { edges } = computeLayout(createGraph([node]))
      expect(edges).toEqual([])
    })
  })

  describe('PR / separator / load-more synthesis (Next view)', () => {
    it('emits PRs above issues with separator between', () => {
      const node = createNode(createIssue({ id: 'a' }), 0, 0)
      const graph = createGraph([node], {
        mergedPrs: [
          {
            number: 1,
            title: 'PR 1',
            url: null,
            isMerged: true,
            hasDescription: false,
          },
          {
            number: 2,
            title: 'PR 2',
            url: null,
            isMerged: true,
            hasDescription: false,
          },
        ],
      })
      const { lines } = computeLayout(graph, Infinity, ViewMode.Next)
      expect(lines.filter(isPrRenderLine)).toHaveLength(2)
      expect(lines.filter(isSeparatorRenderLine)).toHaveLength(1)
      const issueIdx = lines.findIndex(isIssueRenderLine)
      const sepIdx = lines.findIndex(isSeparatorRenderLine)
      expect(sepIdx).toBeLessThan(issueIdx)
    })

    it('emits a load-more entry when hasMorePastPrs is true', () => {
      const graph = createGraph([], { mergedPrs: [], hasMorePastPrs: true })
      // Empty result fallback — empty graph with PRs absent returns empty
      // To reach the loadMore branch we need PRs; simulate one PR.
      const graph2 = createGraph([], {
        mergedPrs: [
          {
            number: 1,
            title: 'PR',
            url: null,
            isMerged: true,
            hasDescription: false,
          },
        ],
        hasMorePastPrs: true,
      })
      expect(computeLayout(graph, Infinity, ViewMode.Next).lines).toEqual([])
      const { lines } = computeLayout(graph2, Infinity, ViewMode.Next)
      expect(lines.filter(isLoadMoreRenderLine)).toHaveLength(1)
    })

    it('omits PR / separator / load-more entries in tree view', () => {
      const node = createNode(createIssue({ id: 'a' }), 0, 0)
      const graph = createGraph([node], {
        mergedPrs: [
          {
            number: 1,
            title: 'PR',
            url: null,
            isMerged: true,
            hasDescription: false,
          },
        ],
        hasMorePastPrs: true,
      })
      const { lines } = computeLayout(graph, Infinity, ViewMode.Tree)
      expect(lines.some(isPrRenderLine)).toBe(false)
      expect(lines.some(isSeparatorRenderLine)).toBe(false)
      expect(lines.some(isLoadMoreRenderLine)).toBe(false)
    })
  })

  describe('linkedPrs / agentStatuses lookup', () => {
    it('resolves by id, lowercase, or uppercase', () => {
      const node = createNode(createIssue({ id: 'AbC' }), 0, 0)
      const graph = createGraph([node], {
        linkedPrs: { abc: { number: 1, url: null, status: 'open' } },
      })
      const { lines } = computeLayout(graph)
      const [line] = lines.filter(isIssueRenderLine)
      expect(line.linkedPr?.number).toBe(1)
    })
  })
})

describe('getRenderKey', () => {
  function makeIssue(overrides: Partial<TaskGraphIssueRenderLine> = {}): TaskGraphIssueRenderLine {
    return {
      type: 'issue',
      issueId: 'a',
      title: 'A',
      description: null,
      branchName: null,
      lane: 0,
      marker: TaskGraphMarkerType.Open,
      issueType: IssueType.TASK,
      status: IssueStatus.OPEN,
      hasDescription: false,
      linkedPr: null,
      agentStatus: null,
      assignedTo: null,
      executionMode: ExecutionMode.SERIES,
      parentIssues: null,
      parentIssueId: null,
      appearanceIndex: 1,
      totalAppearances: 1,
      ...overrides,
    }
  }

  it('returns the bare issueId for a single appearance', () => {
    expect(getRenderKey(makeIssue({ totalAppearances: 1 }))).toBe('a')
  })

  it('disambiguates multi-parent appearances by index', () => {
    expect(getRenderKey(makeIssue({ appearanceIndex: 1, totalAppearances: 2 }))).toBe('a:1')
  })
})

describe('computeLayoutFromIssues', () => {
  it('returns ok=true with empty lines/edges for empty input', () => {
    const result = computeLayoutFromIssues({ issues: [] })
    expect(result.ok).toBe(true)
    if (result.ok) {
      expect(result.lines).toEqual([])
      expect(result.edges).toEqual([])
    }
  })

  it('emits one issue render line per issue in tree mode', () => {
    const result = computeLayoutFromIssues({
      issues: [createIssue({ id: 'a' }), createIssue({ id: 'b' })],
      viewMode: ViewMode.Tree,
    })
    expect(result.ok).toBe(true)
    if (!result.ok) return
    const issueLines = result.lines.filter(isIssueRenderLine)
    expect(issueLines).toHaveLength(2)
    expect(issueLines.map((l) => l.issueId).sort()).toEqual(['a', 'b'])
  })

  it('emits an edge between a parent and its child', () => {
    const child = createIssue({
      id: 'child',
      parentIssues: [{ parentIssue: 'parent' }],
    })
    const parent = createIssue({ id: 'parent' })
    const result = computeLayoutFromIssues({
      issues: [child, parent],
      viewMode: ViewMode.Tree,
    })
    expect(result.ok).toBe(true)
    if (!result.ok) return
    expect(result.edges).toHaveLength(1)
    // Tree view runs normalTree: parent emitted first, edge points down to child.
    expect(result.edges[0].from).toBe('parent')
    expect(result.edges[0].to).toBe('child')
  })

  it('returns ok=false with a degraded flat list when the graph contains a cycle', () => {
    const a = createIssue({ id: 'a', parentIssues: [{ parentIssue: 'b' }] })
    const b = createIssue({ id: 'b', parentIssues: [{ parentIssue: 'a' }] })
    const result = computeLayoutFromIssues({ issues: [a, b], viewMode: ViewMode.Tree })
    expect(result.ok).toBe(false)
    if (result.ok) return
    expect(result.cycle.length).toBeGreaterThan(0)
    // Degraded mode: every issue still appears as a row, no edges.
    expect(result.lines.filter(isIssueRenderLine)).toHaveLength(2)
    expect(result.edges).toEqual([])
  })

  it('joins linked-PR and agent-status decorations onto the issue render lines', () => {
    const result = computeLayoutFromIssues({
      issues: [createIssue({ id: 'a' })],
      linkedPrs: { a: { number: 7, url: 'https://example.com/7', status: 'open' } },
      agentStatuses: {
        a: { isActive: true, status: 'running', sessionId: 'sess-1' } as never,
      },
      viewMode: ViewMode.Tree,
    })
    expect(result.ok).toBe(true)
    if (!result.ok) return
    const line = result.lines.find(isIssueRenderLine)!
    expect(line.linkedPr?.number).toBe(7)
    expect(line.agentStatus?.sessionId).toBe('sess-1')
  })

  it('emits PR rows + separator before issues in next mode', () => {
    const result = computeLayoutFromIssues({
      issues: [createIssue({ id: 'a' })],
      mergedPrs: [
        {
          pullRequest: {
            number: 100,
            title: 'Past PR',
            body: 'description body',
            mergedAt: '2026-01-01T00:00:00Z',
            htmlUrl: 'https://example.com/pr/100',
            status: 'merged',
          },
          time: 1,
        } as never,
      ],
      hasMorePastPrs: true,
      viewMode: ViewMode.Next,
    })
    expect(result.ok).toBe(true)
    if (!result.ok) return
    expect(result.lines[0].type).toBe('loadMore')
    expect(result.lines[1].type).toBe('pr')
    expect(result.lines[2].type).toBe('separator')
    expect(result.lines[3].type).toBe('issue')
  })

  it('marks an issue with no open parent as actionable', () => {
    const root = createIssue({ id: 'root', status: IssueStatus.OPEN })
    const result = computeLayoutFromIssues({ issues: [root] })
    expect(result.ok).toBe(true)
    if (!result.ok) return
    const line = result.lines.find(isIssueRenderLine)!
    expect(line.marker).toBe(TaskGraphMarkerType.Actionable)
  })

  it('next mode seeds layoutForNext from non-terminal leaves and walks up ancestors', () => {
    // Terminal-status descendants are excluded from the seed. The active leaves
    // (`childA` open, `childB` review) reach `parent` via the ancestor walk,
    // so Next renders the full non-terminal subgraph but framed leaves-up.
    // `terminalChild` (closed) is hidden from both Tree and Next.
    const issues = [
      createIssue({
        id: 'parent',
        status: IssueStatus.OPEN,
        executionMode: ExecutionMode.PARALLEL,
      }),
      createIssue({
        id: 'childA',
        status: IssueStatus.OPEN,
        parentIssues: [{ parentIssue: 'parent', sortOrder: 'a' }],
      }),
      createIssue({
        id: 'childB',
        status: IssueStatus.REVIEW,
        parentIssues: [{ parentIssue: 'parent', sortOrder: 'b' }],
      }),
      createIssue({
        id: 'terminalChild',
        status: IssueStatus.CLOSED,
        parentIssues: [{ parentIssue: 'parent', sortOrder: 'c' }],
      }),
    ]

    const tree = computeLayoutFromIssues({ issues, viewMode: ViewMode.Tree })
    expect(tree.ok).toBe(true)
    if (!tree.ok) return
    const treeIds = tree.lines
      .filter(isIssueRenderLine)
      .map((l) => l.issueId)
      .sort()
    expect(treeIds).toEqual(['childA', 'childB', 'parent'])

    const next = computeLayoutFromIssues({ issues, viewMode: ViewMode.Next })
    expect(next.ok).toBe(true)
    if (!next.ok) return
    const nextIds = next.lines
      .filter(isIssueRenderLine)
      .map((l) => l.issueId)
      .sort()
    expect(nextIds).toEqual(['childA', 'childB', 'parent'])
  })

  it('next mode falls back to the full tree when no issues are actionable', () => {
    // All issues are terminal — no actionables to seed layoutForNext.
    // Tree fallback keeps the view non-empty (terminal-with-active-descendants
    // visibility still hides them, but the result must be consistent with
    // the tree-mode result for the same input).
    const issues = [
      createIssue({ id: 'a', status: IssueStatus.COMPLETE }),
      createIssue({ id: 'b', status: IssueStatus.CLOSED }),
    ]
    const next = computeLayoutFromIssues({ issues, viewMode: ViewMode.Next })
    const tree = computeLayoutFromIssues({ issues, viewMode: ViewMode.Tree })
    expect(next.ok).toBe(true)
    expect(tree.ok).toBe(true)
    if (!next.ok || !tree.ok) return
    const nextIssueIds = next.lines.filter(isIssueRenderLine).map((l) => l.issueId)
    const treeIssueIds = tree.lines.filter(isIssueRenderLine).map((l) => l.issueId)
    expect(nextIssueIds).toEqual(treeIssueIds)
  })

  it('next mode honours an explicit matchedIds override over the actionable default', () => {
    const issues = [
      createIssue({ id: 'root', status: IssueStatus.OPEN }),
      createIssue({
        id: 'mid',
        status: IssueStatus.OPEN,
        parentIssues: [{ parentIssue: 'root' }],
      }),
      createIssue({
        id: 'leaf',
        status: IssueStatus.OPEN,
        parentIssues: [{ parentIssue: 'mid' }],
      }),
      createIssue({ id: 'other', status: IssueStatus.OPEN }),
    ]
    // Default Next seeds from non-terminal leaves = {leaf, other}. An explicit
    // matchedIds override of {leaf} replaces the default seed → walk pulls
    // leaf + mid + root and excludes `other`.
    const result = computeLayoutFromIssues({
      issues,
      viewMode: ViewMode.Next,
      matchedIds: new Set(['leaf']),
    })
    expect(result.ok).toBe(true)
    if (!result.ok) return
    const ids = result.lines
      .filter(isIssueRenderLine)
      .map((l) => l.issueId)
      .sort()
    expect(ids).toEqual(['leaf', 'mid', 'root'])
  })
})

// =====================================================================
// Phase row synthesis — engine-driven (siblings model)
// =====================================================================

const threePhaseState: IssueOpenSpecState = {
  branchState: 'present' as IssueOpenSpecState['branchState'],
  changeState: 'inProgress' as IssueOpenSpecState['changeState'],
  phases: [
    {
      name: 'Alpha',
      done: 1,
      total: 3,
      tasks: [
        { description: 'A1', done: true },
        { description: 'A2', done: false },
        { description: 'A3', done: false },
      ],
    },
    {
      name: 'Beta',
      done: 0,
      total: 2,
      tasks: [
        { description: 'B1', done: false },
        { description: 'B2', done: false },
      ],
    },
    {
      name: 'Gamma',
      done: 2,
      total: 2,
      tasks: [
        { description: 'G1', done: true },
        { description: 'G2', done: true },
      ],
    },
  ],
}

describe('phase rows in computeLayoutFromIssues', () => {
  it('accepts null, undefined, {} for openSpecStates without throwing', () => {
    const issue = createIssue({ id: 'i1' })
    expect(() => computeLayoutFromIssues({ issues: [issue], openSpecStates: null })).not.toThrow()
    expect(() =>
      computeLayoutFromIssues({ issues: [issue], openSpecStates: undefined })
    ).not.toThrow()
    expect(() => computeLayoutFromIssues({ issues: [issue], openSpecStates: {} })).not.toThrow()
  })

  it('tree view: phases land at parent.lane + 1 in a single column right after the parent', () => {
    // Top-down (normalTree): the issue is emitted first, then phases, then any
    // real children. With no real children, all phases share parent.lane + 1.
    const issue = createIssue({ id: 'i1' })
    const result = computeLayoutFromIssues({
      issues: [issue],
      openSpecStates: { i1: threePhaseState },
      viewMode: ViewMode.Tree,
    })
    expect(result.ok).toBe(true)
    if (!result.ok) return

    const issueLine = result.lines.find(isIssueRenderLine)!
    const phaseLines = result.lines.filter(isPhaseRenderLine)
    expect(phaseLines.map((p) => p.phaseName)).toEqual(['Alpha', 'Beta', 'Gamma'])
    expect(phaseLines.every((p) => p.lane === issueLine.lane + 1)).toBe(true)
    expect(phaseLines.every((p) => p.parentIssueId === 'i1')).toBe(true)
    expect(phaseLines[0].phaseId).toBe('i1::phase::Alpha')

    // The issue line precedes every phase line.
    const issueIdx = result.lines.findIndex(isIssueRenderLine)
    const phaseIdxs = result.lines
      .map((l, i) => (isPhaseRenderLine(l) ? i : -1))
      .filter((i) => i >= 0)
    expect(phaseIdxs.every((i) => i > issueIdx)).toBe(true)
  })

  it('next view: phases stack in a single column before the parent (bottom-up DFS)', () => {
    // issueGraph mode: leaf siblings of a series parent stack at startLane.
    // For an issue with no real children, lane 0 is the phases' column and
    // the issue itself sits at lane 1.
    const issue = createIssue({ id: 'i1' })
    const result = computeLayoutFromIssues({
      issues: [issue],
      openSpecStates: { i1: threePhaseState },
      viewMode: ViewMode.Next,
      matchedIds: new Set(['i1']),
    })
    expect(result.ok).toBe(true)
    if (!result.ok) return

    const issueLine = result.lines.find(isIssueRenderLine)!
    const phaseLines = result.lines.filter(isPhaseRenderLine)
    expect(phaseLines).toHaveLength(3)
    // All phases share a single lane.
    const phaseLanes = new Set(phaseLines.map((p) => p.lane))
    expect(phaseLanes.size).toBe(1)
    // Phases are emitted before the issue (DFS bottom-up).
    const issueIdx = result.lines.findIndex(isIssueRenderLine)
    const phaseIdxs = result.lines
      .map((l, i) => (isPhaseRenderLine(l) ? i : -1))
      .filter((i) => i >= 0)
    expect(phaseIdxs.every((i) => i < issueIdx)).toBe(true)
    // Issue lane is at least one greater than the phase lane (parent above).
    expect(issueLine.lane).toBeGreaterThan(phaseLines[0].lane)
  })

  it('next view: edges have engine-emitted attach sides (not hard-coded Top/Bottom)', () => {
    // The bug fix: phase → issue must carry the engine's seriesCornerToParent
    // attach geometry (bottom → left) instead of the splice's bottom → top.
    const issue = createIssue({ id: 'i1' })
    const result = computeLayoutFromIssues({
      issues: [issue],
      openSpecStates: { i1: threePhaseState },
      viewMode: ViewMode.Next,
      matchedIds: new Set(['i1']),
    })
    expect(result.ok).toBe(true)
    if (!result.ok) return

    const phaseToIssue = result.edges.find((e) => e.to === 'i1' && e.from.includes('::phase::'))
    expect(phaseToIssue).toBeDefined()
    expect(phaseToIssue!.kind).toBe('SeriesCornerToParent')
    expect(phaseToIssue!.sourceAttach).toBe('Bottom')
    expect(phaseToIssue!.targetAttach).toBe('Left')

    const phaseSiblings = result.edges.filter(
      (e) => e.kind === 'SeriesSibling' && e.from.includes('::phase::')
    )
    expect(phaseSiblings).toHaveLength(2)
    for (const e of phaseSiblings) {
      expect(e.sourceAttach).toBe('Bottom')
      expect(e.targetAttach).toBe('Top')
    }
  })

  it('next view: phase column does not collide with sibling subtree column', () => {
    // Reproduction of the user-reported bug: with two unrelated issues both
    // having phases, the phase column for one must not land on the same lane
    // as the other's column. The engine accounts for this; the splice did not.
    const a = createIssue({ id: 'a' })
    const b = createIssue({ id: 'b' })
    const result = computeLayoutFromIssues({
      issues: [a, b],
      openSpecStates: {
        a: {
          branchState: 'present' as IssueOpenSpecState['branchState'],
          changeState: 'inProgress' as IssueOpenSpecState['changeState'],
          phases: [
            { name: 'a1', done: 0, total: 1, tasks: [] },
            { name: 'a2', done: 0, total: 1, tasks: [] },
          ],
        },
        b: {
          branchState: 'present' as IssueOpenSpecState['branchState'],
          changeState: 'inProgress' as IssueOpenSpecState['changeState'],
          phases: [
            { name: 'b1', done: 0, total: 1, tasks: [] },
            { name: 'b2', done: 0, total: 1, tasks: [] },
          ],
        },
      },
      viewMode: ViewMode.Next,
      matchedIds: new Set(['a', 'b']),
    })
    expect(result.ok).toBe(true)
    if (!result.ok) return

    // Each issue + its phases occupy distinct (row, lane) cells. No two render
    // lines may share the same (row, lane). The render-line ordering reflects
    // emission row order, so we use the index as the row.
    const cells = new Set<string>()
    for (let row = 0; row < result.lines.length; row++) {
      const line = result.lines[row]
      const lane = isIssueRenderLine(line) || isPhaseRenderLine(line) ? line.lane : null
      if (lane === null) continue
      const key = `${row}|${lane}`
      expect(cells.has(key)).toBe(false)
      cells.add(key)
    }
  })

  it('cycle-degraded branch emits flat issue lines without phase rows', () => {
    // Two issues that reference each other → cycle → ok: false. Phases are
    // dropped on the degraded path.
    const i1 = createIssue({ id: 'i1', parentIssues: [{ parentIssue: 'i2', sortOrder: 'a' }] })
    const i2 = createIssue({ id: 'i2', parentIssues: [{ parentIssue: 'i1', sortOrder: 'a' }] })
    const result = computeLayoutFromIssues({
      issues: [i1, i2],
      openSpecStates: {
        i1: {
          branchState: 'present' as IssueOpenSpecState['branchState'],
          changeState: 'inProgress' as IssueOpenSpecState['changeState'],
          phases: [{ name: 'P', done: 0, total: 1, tasks: [] }],
        },
      },
    })
    expect(result.ok).toBe(false)
    expect(result.lines.filter(isIssueRenderLine)).toHaveLength(2)
    expect(result.lines.filter(isPhaseRenderLine)).toHaveLength(0)
    expect(result.edges).toEqual([])
  })

  it('phase lines for a later issue do not interleave with earlier issues phases', () => {
    const i1 = createIssue({ id: 'i1' })
    const i2 = createIssue({ id: 'i2', parentIssues: [{ parentIssue: 'i1', sortOrder: 'a' }] })
    const result = computeLayoutFromIssues({
      issues: [i1, i2],
      openSpecStates: {
        i1: {
          branchState: 'present' as IssueOpenSpecState['branchState'],
          changeState: 'inProgress' as IssueOpenSpecState['changeState'],
          phases: [
            { name: 'A', done: 0, total: 1, tasks: [] },
            { name: 'B', done: 0, total: 1, tasks: [] },
          ],
        },
        i2: {
          branchState: 'present' as IssueOpenSpecState['branchState'],
          changeState: 'inProgress' as IssueOpenSpecState['changeState'],
          phases: [{ name: 'C', done: 0, total: 1, tasks: [] }],
        },
      },
      viewMode: ViewMode.Tree,
    })
    expect(result.ok).toBe(true)
    if (!result.ok) return

    const phases1 = result.lines.filter(
      (l) => isPhaseRenderLine(l) && (l as TaskGraphPhaseRenderLine).parentIssueId === 'i1'
    )
    const phases2 = result.lines.filter(
      (l) => isPhaseRenderLine(l) && (l as TaskGraphPhaseRenderLine).parentIssueId === 'i2'
    )
    expect(phases1).toHaveLength(2)
    expect(phases2).toHaveLength(1)
    // Each phase row carries the right parentIssueId.
    expect((phases1[0] as TaskGraphPhaseRenderLine).phaseName).toBe('A')
    expect((phases2[0] as TaskGraphPhaseRenderLine).phaseName).toBe('C')
  })

  it('empty phases array produces no phase render lines', () => {
    const issue = createIssue({ id: 'i1' })
    const emptyState: IssueOpenSpecState = {
      branchState: 'present' as IssueOpenSpecState['branchState'],
      changeState: 'inProgress' as IssueOpenSpecState['changeState'],
      phases: [],
    }
    const result = computeLayoutFromIssues({
      issues: [issue],
      openSpecStates: { i1: emptyState },
    })
    expect(result.ok).toBe(true)
    if (!result.ok) return
    expect(result.lines.filter(isPhaseRenderLine)).toHaveLength(0)
  })
})
