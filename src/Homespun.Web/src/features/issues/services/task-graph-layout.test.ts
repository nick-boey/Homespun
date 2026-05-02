import { describe, it, expect } from 'vitest'
import {
  computeLayout,
  computeLayoutFromIssues,
  getRenderKey,
  TaskGraphMarkerType,
  type TaskGraphIssueRenderLine,
  isIssueRenderLine,
  isPrRenderLine,
  isSeparatorRenderLine,
  isLoadMoreRenderLine,
} from './task-graph-layout'
import type {
  TaskGraphResponse,
  TaskGraphNodeResponse,
  TaskGraphEdgeResponse,
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
    expect(result.edges[0].from).toBe('child')
    expect(result.edges[0].to).toBe('parent')
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

  it('next mode seeds layoutForNext from the actionable leaves and walks up ancestors', () => {
    // `parent` is series-mode with three open children A/B/C in sortOrder. Per
    // Fleece.Core IsActionable: only `childA` is actionable (leaf, all-previous
    // done). `childB`/`childC` are blocked by series ordering. `parent` has
    // incomplete children. Tree mode shows everything; Next mode seeds {childA}
    // and walks ancestors → {childA, parent}, omitting the blocked siblings.
    const issues = [
      createIssue({
        id: 'parent',
        status: IssueStatus.OPEN,
        executionMode: ExecutionMode.SERIES,
      }),
      createIssue({
        id: 'childA',
        status: IssueStatus.OPEN,
        parentIssues: [{ parentIssue: 'parent', sortOrder: 'a' }],
      }),
      createIssue({
        id: 'childB',
        status: IssueStatus.OPEN,
        parentIssues: [{ parentIssue: 'parent', sortOrder: 'b' }],
      }),
      createIssue({
        id: 'childC',
        status: IssueStatus.OPEN,
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
    expect(treeIds).toEqual(['childA', 'childB', 'childC', 'parent'])

    const next = computeLayoutFromIssues({ issues, viewMode: ViewMode.Next })
    expect(next.ok).toBe(true)
    if (!next.ok) return
    const nextIds = next.lines
      .filter(isIssueRenderLine)
      .map((l) => l.issueId)
      .sort()
    expect(nextIds).toEqual(['childA', 'parent'])
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
    // Default Next would seed {leaf, other} (the actionable set under the
    // Fleece.Core IsActionable definition: open + no incomplete children +
    // all-previous-done). An explicit matchedIds override of {leaf} pulls
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
