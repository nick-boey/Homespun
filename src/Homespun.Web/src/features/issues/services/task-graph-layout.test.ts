import { describe, it, expect } from 'vitest'
import {
  computeLayout,
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
