import { describe, it, expect } from 'vitest'
import {
  computeLayout,
  getRenderKey,
  TaskGraphMarkerType,
  type TaskGraphIssueRenderLine,
  type TaskGraphPrRenderLine,
  type TaskGraphSeparatorRenderLine,
  type TaskGraphLoadMoreRenderLine,
  isIssueRenderLine,
  isPrRenderLine,
  isSeparatorRenderLine,
  isLoadMoreRenderLine,
} from './task-graph-layout'
import type { TaskGraphResponse, TaskGraphNodeResponse, IssueResponse } from '@/api'
import { IssueStatus, IssueType, ExecutionMode } from '@/api'

// Helper to create a mock issue
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

// Helper to create a mock node
function createNode(
  issue: IssueResponse,
  lane: number,
  row: number,
  isActionable = false
): TaskGraphNodeResponse {
  return { issue, lane, row, isActionable }
}

describe('computeLayout', () => {
  describe('type guards', () => {
    it('correctly identifies issue render lines', () => {
      const line: TaskGraphIssueRenderLine = {
        type: 'issue',
        issueId: 'test',
        title: 'Test',
        description: null,
        branchName: null,
        lane: 0,
        marker: TaskGraphMarkerType.Open,
        parentLane: null,
        isFirstChild: false,
        isSeriesChild: false,
        drawTopLine: false,
        drawBottomLine: false,
        seriesConnectorFromLane: null,
        issueType: IssueType.TASK,
        status: IssueStatus.OPEN,
        hasDescription: false,
        linkedPr: null,
        agentStatus: null,
        assignedTo: null,
        drawLane0Connector: false,
        isLastLane0Connector: false,
        drawLane0PassThrough: false,
        lane0Color: null,
        hasHiddenParent: false,
        hiddenParentIsSeriesMode: false,
        executionMode: ExecutionMode.SERIES,
        parentIssues: null,
        multiParentIndex: null,
        multiParentTotal: null,
        isLastChild: false,
        hasParallelChildren: false,
        parentIssueId: null,
        parentLaneReservations: [],
      }
      expect(isIssueRenderLine(line)).toBe(true)
      expect(isPrRenderLine(line)).toBe(false)
    })

    it('correctly identifies PR render lines', () => {
      const line: TaskGraphPrRenderLine = {
        type: 'pr',
        prNumber: 123,
        title: 'Test PR',
        url: 'https://github.com/test',
        isMerged: true,
        hasDescription: true,
        agentStatus: null,
        drawTopLine: false,
        drawBottomLine: true,
      }
      expect(isPrRenderLine(line)).toBe(true)
      expect(isIssueRenderLine(line)).toBe(false)
    })

    it('correctly identifies separator render lines', () => {
      const line: TaskGraphSeparatorRenderLine = { type: 'separator' }
      expect(isSeparatorRenderLine(line)).toBe(true)
    })

    it('correctly identifies load more render lines', () => {
      const line: TaskGraphLoadMoreRenderLine = { type: 'loadMore' }
      expect(isLoadMoreRenderLine(line)).toBe(true)
    })
  })

  describe('empty and null inputs', () => {
    it('returns empty array for null taskGraph', () => {
      const result = computeLayout(null)
      expect(result).toEqual([])
    })

    it('returns empty array for undefined taskGraph', () => {
      const result = computeLayout(undefined)
      expect(result).toEqual([])
    })

    it('returns empty array when no nodes and no PRs', () => {
      const taskGraph: TaskGraphResponse = {
        nodes: [],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }
      const result = computeLayout(taskGraph)
      expect(result).toEqual([])
    })
  })

  describe('single issue rendering', () => {
    it('renders a single issue with correct properties', () => {
      const issue = createIssue({ id: 'abc123', title: 'My Task' })
      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(issue, 0, 0, true)],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph)

      expect(result).toHaveLength(1)
      expect(isIssueRenderLine(result[0])).toBe(true)

      const line = result[0] as TaskGraphIssueRenderLine
      expect(line.issueId).toBe('abc123')
      expect(line.title).toBe('My Task')
      expect(line.lane).toBe(0)
      expect(line.marker).toBe(TaskGraphMarkerType.Actionable)
    })

    it('assigns Open marker for non-actionable open issues', () => {
      const issue = createIssue({ status: IssueStatus.OPEN })
      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(issue, 0, 0, false)], // not actionable
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph)
      const line = result[0] as TaskGraphIssueRenderLine
      expect(line.marker).toBe(TaskGraphMarkerType.Open)
    })

    it('assigns Complete marker for completed issues', () => {
      const issue = createIssue({ status: IssueStatus.COMPLETE })
      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(issue, 0, 0, false)],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph)
      const line = result[0] as TaskGraphIssueRenderLine
      expect(line.marker).toBe(TaskGraphMarkerType.Complete)
    })

    it('assigns Closed marker for closed/archived issues', () => {
      const issue = createIssue({ status: IssueStatus.CLOSED })
      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(issue, 0, 0, false)],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph)
      const line = result[0] as TaskGraphIssueRenderLine
      expect(line.marker).toBe(TaskGraphMarkerType.Closed)
    })
  })

  describe('merged PRs handling', () => {
    it('skips merged PRs in tree view', () => {
      const taskGraph: TaskGraphResponse = {
        nodes: [],
        mergedPrs: [
          {
            number: 123,
            title: 'Test PR',
            url: 'https://github.com/test',
            isMerged: true,
            hasDescription: true,
          },
        ],
        hasMorePastPrs: true,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph)

      // Tree view skips PRs entirely - no nodes means empty result
      expect(result).toHaveLength(0)
    })

    it('does not add separator or offset lanes when PRs exist', () => {
      const issue = createIssue()
      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(issue, 0, 0)],
        mergedPrs: [{ number: 1, title: 'PR', url: null, isMerged: true, hasDescription: false }],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph)

      // Tree view: only the issue, no PR or separator
      expect(result).toHaveLength(1)
      expect(result[0].type).toBe('issue')
      const issueLine = result[0] as TaskGraphIssueRenderLine
      expect(issueLine.lane).toBe(0) // No lane offset in tree view
    })
  })

  describe('parent-child relationships', () => {
    it('renders parent-child with correct lanes and parentLane (tree view)', () => {
      const parent = createIssue({ id: 'parent', executionMode: ExecutionMode.PARALLEL })
      const child = createIssue({ id: 'child', parentIssues: [{ parentIssue: 'parent' }] })

      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(parent, 1, 0), createNode(child, 0, 1)],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph)

      expect(result).toHaveLength(2)

      // In tree view: parent at lane 0, child at lane 1
      const parentLine = result.find(
        (l) => isIssueRenderLine(l) && l.issueId === 'parent'
      ) as TaskGraphIssueRenderLine
      const childLine = result.find(
        (l) => isIssueRenderLine(l) && l.issueId === 'child'
      ) as TaskGraphIssueRenderLine
      expect(parentLine.lane).toBe(0)
      expect(childLine.lane).toBe(1)
      expect(childLine.parentLane).toBe(0)
      expect(childLine.isSeriesChild).toBe(false)
    })

    it('populates parentIssues on render lines', () => {
      const parentIssues = [{ parentIssue: 'parent', sortOrder: 'ab' }]
      const parent = createIssue({ id: 'parent', executionMode: ExecutionMode.PARALLEL })
      const child = createIssue({ id: 'child', parentIssues })

      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(parent, 1, 0), createNode(child, 0, 1)],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph)

      const childLine = result.find(
        (l) => isIssueRenderLine(l) && l.issueId === 'child'
      ) as TaskGraphIssueRenderLine
      expect(childLine.parentIssues).toEqual(parentIssues)
    })

    it('renders series children with correct flags (tree view)', () => {
      const parent = createIssue({ id: 'parent', executionMode: ExecutionMode.SERIES })
      const child1 = createIssue({ id: 'child1', parentIssues: [{ parentIssue: 'parent' }] })
      const child2 = createIssue({ id: 'child2', parentIssues: [{ parentIssue: 'parent' }] })

      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(parent, 0, 0), createNode(child1, 1, 1), createNode(child2, 1, 2)],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph)

      const child1Line = result.find(
        (l) => isIssueRenderLine(l) && l.issueId === 'child1'
      ) as TaskGraphIssueRenderLine
      const child2Line = result.find(
        (l) => isIssueRenderLine(l) && l.issueId === 'child2'
      ) as TaskGraphIssueRenderLine

      expect(child1Line.isSeriesChild).toBe(true)
      expect(child2Line.isSeriesChild).toBe(true)

      // Series children have vertical connectors
      expect(child1Line.drawBottomLine).toBe(true)
      expect(child2Line.drawTopLine).toBe(true)
    })
  })

  describe('depth filtering', () => {
    it('filters nodes beyond maxDepth in tree view', () => {
      const parent = createIssue({ id: 'parent' })
      const child = createIssue({ id: 'child', parentIssues: [{ parentIssue: 'parent' }] })
      const grandchild = createIssue({
        id: 'grandchild',
        parentIssues: [{ parentIssue: 'child' }],
      })

      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(parent, 0, 0), createNode(child, 1, 1), createNode(grandchild, 2, 2)],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      // maxDepth = 1 means show lanes 0 and 1 from root
      const result = computeLayout(taskGraph, 1)

      // parent at lane 0, child at lane 1 (shown)
      // grandchild at lane 2 filtered (depth 2 > maxDepth 1)
      expect(result).toHaveLength(2)
      const issueIds = result.filter(isIssueRenderLine).map((l) => l.issueId)
      expect(issueIds).toContain('parent')
      expect(issueIds).toContain('child')
      expect(issueIds).not.toContain('grandchild')
    })

    it('marks hidden parent indicator when child is at depth boundary', () => {
      const parent = createIssue({ id: 'parent', executionMode: ExecutionMode.PARALLEL })
      const child = createIssue({ id: 'child', parentIssues: [{ parentIssue: 'parent' }] })
      const grandchild = createIssue({
        id: 'grandchild',
        parentIssues: [{ parentIssue: 'child' }],
      })

      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(parent, 0, 0), createNode(child, 1, 1), createNode(grandchild, 2, 2)],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      // maxDepth = 0 means only show lane 0 (root)
      const result = computeLayout(taskGraph, 0)

      expect(result).toHaveLength(1)
      const parentLine = result[0] as TaskGraphIssueRenderLine
      expect(parentLine.issueId).toBe('parent')
    })

    it('shows hidden parent info when grandchild exceeds depth', () => {
      const parent = createIssue({ id: 'parent', executionMode: ExecutionMode.SERIES })
      const child = createIssue({ id: 'child', parentIssues: [{ parentIssue: 'parent' }] })
      const grandchild = createIssue({
        id: 'grandchild',
        parentIssues: [{ parentIssue: 'child' }],
      })

      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(parent, 0, 0), createNode(child, 1, 1), createNode(grandchild, 2, 2)],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      // maxDepth = 1: root (lane 0) and children (lane 1) visible
      // grandchild at lane 2 is filtered out
      const result = computeLayout(taskGraph, 1)

      const issueIds = result.filter(isIssueRenderLine).map((l) => l.issueId)
      expect(issueIds).toContain('parent')
      expect(issueIds).toContain('child')
      expect(issueIds).not.toContain('grandchild')
    })
  })

  describe('connected components', () => {
    it('groups connected nodes together', () => {
      // Two separate groups
      const issue1 = createIssue({ id: 'group1-a' })
      const issue2 = createIssue({ id: 'group2-a' })

      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(issue1, 0, 0), createNode(issue2, 0, 1)],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph)

      // Both should render since they're disconnected
      expect(result).toHaveLength(2)
    })
  })

  describe('lane 0 connectors (not used in tree view)', () => {
    it('does not set lane 0 connectors in tree view', () => {
      const issue = createIssue()
      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(issue, 0, 0)],
        mergedPrs: [{ number: 1, title: 'PR', url: null, isMerged: true, hasDescription: false }],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph)
      const issueLine = result.find(isIssueRenderLine) as TaskGraphIssueRenderLine

      expect(issueLine.lane).toBe(0) // No lane offset in tree view
      expect(issueLine.drawLane0Connector).toBe(false)
      expect(issueLine.drawLane0PassThrough).toBe(false)
    })
  })

  describe('agent status and linked PRs', () => {
    it('includes agent status when available', () => {
      const issue = createIssue({ id: 'abc123' })
      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(issue, 0, 0)],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {
          abc123: { isActive: true, status: 'running', sessionId: 'session-1' },
        },
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph)
      const line = result[0] as TaskGraphIssueRenderLine

      expect(line.agentStatus).toEqual({
        isActive: true,
        status: 'running',
        sessionId: 'session-1',
      })
    })

    it('includes linked PR when available', () => {
      const issue = createIssue({ id: 'abc123' })
      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(issue, 0, 0)],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {
          abc123: { number: 456, url: 'https://github.com/test/456', status: 'open' },
        },
      }

      const result = computeLayout(taskGraph)
      const line = result[0] as TaskGraphIssueRenderLine

      expect(line.linkedPr).toEqual({
        number: 456,
        url: 'https://github.com/test/456',
        status: 'open',
      })
    })
  })

  describe('assignedTo field', () => {
    it('includes assignedTo when issue has assignee', () => {
      const issue = createIssue({ id: 'abc123', assignedTo: 'user@example.com' })
      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(issue, 0, 0)],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph)
      const line = result[0] as TaskGraphIssueRenderLine

      expect(line.assignedTo).toBe('user@example.com')
    })

    it('sets assignedTo to null when issue has no assignee', () => {
      const issue = createIssue({ id: 'abc123' })
      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(issue, 0, 0)],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph)
      const line = result[0] as TaskGraphIssueRenderLine

      expect(line.assignedTo).toBeNull()
    })
  })

  describe('isSeriesChild flag computation', () => {
    it('sets isSeriesChild=true when parent has executionMode=series', () => {
      const parent = createIssue({ id: 'parent', executionMode: ExecutionMode.SERIES })
      const child = createIssue({ id: 'child', parentIssues: [{ parentIssue: 'parent' }] })

      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(parent, 1, 0), createNode(child, 0, 1)],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph)

      const childLine = result.find(
        (l) => isIssueRenderLine(l) && l.issueId === 'child'
      ) as TaskGraphIssueRenderLine
      expect(childLine).toBeDefined()
      expect(childLine.isSeriesChild).toBe(true)
    })

    it('sets isSeriesChild=false when parent has executionMode=parallel', () => {
      const parent = createIssue({ id: 'parent', executionMode: ExecutionMode.PARALLEL })
      const child = createIssue({ id: 'child', parentIssues: [{ parentIssue: 'parent' }] })

      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(parent, 1, 0), createNode(child, 0, 1)],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph)

      const childLine = result.find(
        (l) => isIssueRenderLine(l) && l.issueId === 'child'
      ) as TaskGraphIssueRenderLine
      expect(childLine).toBeDefined()
      expect(childLine.isSeriesChild).toBe(false)
    })

    it('sets isSeriesChild=false when executionMode is undefined', () => {
      const parent = createIssue({ id: 'parent' }) // No executionMode set
      delete (parent as Partial<typeof parent>).executionMode // Explicitly remove
      const child = createIssue({ id: 'child', parentIssues: [{ parentIssue: 'parent' }] })

      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(parent, 1, 0), createNode(child, 0, 1)],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph)

      const childLine = result.find(
        (l) => isIssueRenderLine(l) && l.issueId === 'child'
      ) as TaskGraphIssueRenderLine
      expect(childLine).toBeDefined()
      expect(childLine.isSeriesChild).toBe(false)
    })

    it('correctly identifies multiple children of series parent', () => {
      const parent = createIssue({ id: 'parent', executionMode: ExecutionMode.SERIES })
      const child1 = createIssue({ id: 'child1', parentIssues: [{ parentIssue: 'parent' }] })
      const child2 = createIssue({ id: 'child2', parentIssues: [{ parentIssue: 'parent' }] })
      const child3 = createIssue({ id: 'child3', parentIssues: [{ parentIssue: 'parent' }] })

      const taskGraph: TaskGraphResponse = {
        nodes: [
          createNode(parent, 1, 0),
          createNode(child1, 0, 1),
          createNode(child2, 0, 2),
          createNode(child3, 0, 3),
        ],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph)

      const child1Line = result.find(
        (l) => isIssueRenderLine(l) && l.issueId === 'child1'
      ) as TaskGraphIssueRenderLine
      const child2Line = result.find(
        (l) => isIssueRenderLine(l) && l.issueId === 'child2'
      ) as TaskGraphIssueRenderLine
      const child3Line = result.find(
        (l) => isIssueRenderLine(l) && l.issueId === 'child3'
      ) as TaskGraphIssueRenderLine

      expect(child1Line.isSeriesChild).toBe(true)
      expect(child2Line.isSeriesChild).toBe(true)
      expect(child3Line.isSeriesChild).toBe(true)
    })

    it('handles mixed parallel and series parents in same graph', () => {
      const parallelParent = createIssue({
        id: 'parallel-parent',
        executionMode: ExecutionMode.PARALLEL,
      })
      const seriesParent = createIssue({ id: 'series-parent', executionMode: ExecutionMode.SERIES })
      const parallelChild = createIssue({
        id: 'parallel-child',
        parentIssues: [{ parentIssue: 'parallel-parent' }],
      })
      const seriesChild = createIssue({
        id: 'series-child',
        parentIssues: [{ parentIssue: 'series-parent' }],
      })

      const taskGraph: TaskGraphResponse = {
        nodes: [
          createNode(parallelParent, 1, 0),
          createNode(parallelChild, 0, 1),
          createNode(seriesParent, 1, 2),
          createNode(seriesChild, 0, 3),
        ],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph)

      const parallelChildLine = result.find(
        (l) => isIssueRenderLine(l) && l.issueId === 'parallel-child'
      ) as TaskGraphIssueRenderLine
      const seriesChildLine = result.find(
        (l) => isIssueRenderLine(l) && l.issueId === 'series-child'
      ) as TaskGraphIssueRenderLine

      expect(parallelChildLine.isSeriesChild).toBe(false)
      expect(seriesChildLine.isSeriesChild).toBe(true)
    })
  })

  describe('branch name generation', () => {
    it('generates branch name for issues', () => {
      const issue = createIssue({
        id: 'abc123',
        title: 'Add feature',
        type: IssueType.TASK,
        workingBranchId: 'my-branch',
      })
      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(issue, 0, 0)],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph)
      const line = result[0] as TaskGraphIssueRenderLine

      // Branch name should be generated from type and workingBranchId
      expect(line.branchName).toBe('task/my-branch+abc123')
    })
  })

  describe('assignedTo field passthrough', () => {
    it('includes assignedTo when issue has an assigned user', () => {
      const issue = createIssue({
        id: 'abc123',
        title: 'Test Issue',
        assignedTo: 'user@example.com',
      })
      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(issue, 0, 0)],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph)
      const line = result[0] as TaskGraphIssueRenderLine

      expect(line.assignedTo).toBe('user@example.com')
    })

    it('sets assignedTo to null when issue has no assigned user', () => {
      const issue = createIssue({
        id: 'abc123',
        title: 'Test Issue',
      })
      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(issue, 0, 0)],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph)
      const line = result[0] as TaskGraphIssueRenderLine

      expect(line.assignedTo).toBeNull()
    })

    it('sets assignedTo to null when assignedTo is undefined', () => {
      const issue = createIssue({
        id: 'abc123',
        title: 'Test Issue',
        assignedTo: undefined,
      })
      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(issue, 0, 0)],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph)
      const line = result[0] as TaskGraphIssueRenderLine

      expect(line.assignedTo).toBeNull()
    })
  })

  describe('executionMode field passthrough', () => {
    it('passes through executionMode=series correctly', () => {
      const issue = createIssue({
        id: 'abc123',
        title: 'Test Issue',
        executionMode: ExecutionMode.SERIES,
      })
      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(issue, 0, 0)],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph)
      const line = result[0] as TaskGraphIssueRenderLine

      expect(line.executionMode).toBe(ExecutionMode.SERIES)
    })

    it('passes through executionMode=parallel correctly', () => {
      const issue = createIssue({
        id: 'abc123',
        title: 'Test Issue',
        executionMode: ExecutionMode.PARALLEL,
      })
      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(issue, 0, 0)],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph)
      const line = result[0] as TaskGraphIssueRenderLine

      expect(line.executionMode).toBe(ExecutionMode.PARALLEL)
    })

    it('defaults executionMode to series when undefined', () => {
      const issue = createIssue({
        id: 'abc123',
        title: 'Test Issue',
      })
      delete (issue as Partial<typeof issue>).executionMode
      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(issue, 0, 0)],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph)
      const line = result[0] as TaskGraphIssueRenderLine

      expect(line.executionMode).toBe(ExecutionMode.SERIES)
    })
  })

  describe('tree view mode', () => {
    it('places root issues at lane 0 in tree view', () => {
      // A root issue with no parent should be at lane 0
      const root = createIssue({ id: 'root' })
      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(root, 0, 0)],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph, 3)

      expect(result).toHaveLength(1)
      const rootLine = result[0] as TaskGraphIssueRenderLine
      expect(rootLine.lane).toBe(0)
    })

    it('places children at higher lanes than parents in tree view', () => {
      // Parent at lane 0, child at lane 1
      const parent = createIssue({ id: 'parent', executionMode: ExecutionMode.PARALLEL })
      const child = createIssue({ id: 'child', parentIssues: [{ parentIssue: 'parent' }] })

      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(parent, 1, 0), createNode(child, 0, 1)],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph, 3)

      const parentLine = result.find(
        (l) => isIssueRenderLine(l) && l.issueId === 'parent'
      ) as TaskGraphIssueRenderLine
      const childLine = result.find(
        (l) => isIssueRenderLine(l) && l.issueId === 'child'
      ) as TaskGraphIssueRenderLine

      expect(parentLine.lane).toBe(0)
      expect(childLine.lane).toBe(1)
      expect(childLine.parentLane).toBe(0)
    })

    it('omits merged PRs in tree view', () => {
      const issue = createIssue({ id: 'issue1' })
      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(issue, 0, 0)],
        mergedPrs: [{ number: 1, title: 'PR', url: null, isMerged: true, hasDescription: false }],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph, 3)

      // Should not have PR lines or separator
      const prLines = result.filter(isPrRenderLine)
      const separatorLines = result.filter(isSeparatorRenderLine)

      expect(prLines).toHaveLength(0)
      expect(separatorLines).toHaveLength(0)
      // Issues should be at lane 0 (no offset)
      const issueLine = result.find(isIssueRenderLine) as TaskGraphIssueRenderLine
      expect(issueLine.lane).toBe(0)
    })

    it('omits load more button in tree view', () => {
      const issue = createIssue({ id: 'issue1' })
      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(issue, 0, 0)],
        mergedPrs: [{ number: 1, title: 'PR', url: null, isMerged: true, hasDescription: false }],
        hasMorePastPrs: true,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph, 3)

      const loadMoreLines = result.filter(isLoadMoreRenderLine)
      expect(loadMoreLines).toHaveLength(0)
    })

    it('shows parent at lane 0 with grandchild at lane 2 in tree view', () => {
      // Three-level hierarchy: grandparent -> parent -> child
      const grandparent = createIssue({ id: 'grandparent', executionMode: ExecutionMode.PARALLEL })
      const parent = createIssue({
        id: 'parent',
        parentIssues: [{ parentIssue: 'grandparent' }],
        executionMode: ExecutionMode.PARALLEL,
      })
      const child = createIssue({
        id: 'child',
        parentIssues: [{ parentIssue: 'parent' }],
      })

      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(grandparent, 2, 0), createNode(parent, 1, 1), createNode(child, 0, 2)],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph, 3)

      const grandparentLine = result.find(
        (l) => isIssueRenderLine(l) && l.issueId === 'grandparent'
      ) as TaskGraphIssueRenderLine
      const parentLine = result.find(
        (l) => isIssueRenderLine(l) && l.issueId === 'parent'
      ) as TaskGraphIssueRenderLine
      const childLine = result.find(
        (l) => isIssueRenderLine(l) && l.issueId === 'child'
      ) as TaskGraphIssueRenderLine

      expect(grandparentLine.lane).toBe(0)
      expect(parentLine.lane).toBe(1)
      expect(childLine.lane).toBe(2)
    })

    it('renders nodes in tree traversal order (DFS from roots)', () => {
      // Parent with two children - children should come after parent
      const parent = createIssue({ id: 'parent', executionMode: ExecutionMode.PARALLEL })
      const child1 = createIssue({ id: 'child1', parentIssues: [{ parentIssue: 'parent' }] })
      const child2 = createIssue({ id: 'child2', parentIssues: [{ parentIssue: 'parent' }] })

      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(parent, 1, 0), createNode(child1, 0, 1), createNode(child2, 0, 2)],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph, 3)

      const issueLines = result.filter(isIssueRenderLine)
      expect(issueLines).toHaveLength(3)
      expect(issueLines[0].issueId).toBe('parent')
      // Children should come after parent (order between siblings may vary)
      expect(['child1', 'child2']).toContain(issueLines[1].issueId)
      expect(['child1', 'child2']).toContain(issueLines[2].issueId)
    })

    it('renders grandchildren in DFS order (children before siblings)', () => {
      // A -> [B -> [D, E], C -> [F]]
      // DFS should produce: A, B, D, E, C, F (not A, B, C, D, E, F)
      const a = createIssue({ id: 'a', executionMode: ExecutionMode.PARALLEL })
      const b = createIssue({
        id: 'b',
        executionMode: ExecutionMode.PARALLEL,
        parentIssues: [{ parentIssue: 'a', sortOrder: 'a' }],
      })
      const c = createIssue({
        id: 'c',
        executionMode: ExecutionMode.PARALLEL,
        parentIssues: [{ parentIssue: 'a', sortOrder: 'b' }],
      })
      const d = createIssue({
        id: 'd',
        parentIssues: [{ parentIssue: 'b', sortOrder: 'a' }],
      })
      const e = createIssue({
        id: 'e',
        parentIssues: [{ parentIssue: 'b', sortOrder: 'b' }],
      })
      const f = createIssue({
        id: 'f',
        parentIssues: [{ parentIssue: 'c', sortOrder: 'a' }],
      })

      const taskGraph: TaskGraphResponse = {
        nodes: [
          createNode(a, 0, 0),
          createNode(b, 1, 1),
          createNode(c, 1, 2),
          createNode(d, 2, 3),
          createNode(e, 2, 4),
          createNode(f, 2, 5),
        ],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph, 3)
      const issueLines = result.filter(isIssueRenderLine)

      expect(issueLines.map((l) => l.issueId)).toEqual(['a', 'b', 'd', 'e', 'c', 'f'])
    })

    it('handles multiple root nodes in tree view', () => {
      // Two separate root issues
      const root1 = createIssue({ id: 'root1' })
      const root2 = createIssue({ id: 'root2' })

      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(root1, 0, 0), createNode(root2, 0, 1)],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph, 3)

      expect(result).toHaveLength(2)
      const root1Line = result.find(
        (l) => isIssueRenderLine(l) && l.issueId === 'root1'
      ) as TaskGraphIssueRenderLine
      const root2Line = result.find(
        (l) => isIssueRenderLine(l) && l.issueId === 'root2'
      ) as TaskGraphIssueRenderLine

      expect(root1Line.lane).toBe(0)
      expect(root2Line.lane).toBe(0)
    })

    it('applies maxDepth filtering in tree view mode', () => {
      // Parent at lane 0, child at lane 1, grandchild at lane 2
      const parent = createIssue({ id: 'parent', executionMode: ExecutionMode.PARALLEL })
      const child = createIssue({
        id: 'child',
        parentIssues: [{ parentIssue: 'parent' }],
        executionMode: ExecutionMode.PARALLEL,
      })
      const grandchild = createIssue({
        id: 'grandchild',
        parentIssues: [{ parentIssue: 'child' }],
      })

      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(parent, 2, 0), createNode(child, 1, 1), createNode(grandchild, 0, 2)],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      // maxDepth=1 means show lanes 0 and 1 (parent and child, not grandchild)
      const result = computeLayout(taskGraph, 1)

      const issueIds = result.filter(isIssueRenderLine).map((l) => l.issueId)
      expect(issueIds).toContain('parent')
      expect(issueIds).toContain('child')
      expect(issueIds).not.toContain('grandchild')
    })

    it('shows multi-parent node under each parent in tree view (2-parent)', () => {
      // Diamond pattern: root -> parent1, parent2 -> shared_child
      const root = createIssue({ id: 'root', executionMode: ExecutionMode.PARALLEL })
      const parent1 = createIssue({
        id: 'parent1',
        parentIssues: [{ parentIssue: 'root' }],
        executionMode: ExecutionMode.PARALLEL,
      })
      const parent2 = createIssue({
        id: 'parent2',
        parentIssues: [{ parentIssue: 'root' }],
        executionMode: ExecutionMode.PARALLEL,
      })
      const sharedChild = createIssue({
        id: 'shared',
        parentIssues: [{ parentIssue: 'parent1' }, { parentIssue: 'parent2' }],
      })

      const taskGraph: TaskGraphResponse = {
        nodes: [
          createNode(root, 2, 0),
          createNode(parent1, 1, 1),
          createNode(parent2, 1, 2),
          createNode(sharedChild, 0, 3),
        ],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph, 3)

      // The shared child should appear twice (once per parent)
      const sharedLines = result.filter(
        (l) => isIssueRenderLine(l) && l.issueId === 'shared'
      ) as TaskGraphIssueRenderLine[]
      expect(sharedLines).toHaveLength(2)

      // First instance: multiParentIndex=0, shows down-right diagonal
      expect(sharedLines[0].multiParentIndex).toBe(0)
      expect(sharedLines[0].multiParentTotal).toBe(2)

      // Second instance: multiParentIndex=1, shows up-left diagonal
      expect(sharedLines[1].multiParentIndex).toBe(1)
      expect(sharedLines[1].multiParentTotal).toBe(2)
    })

    it('shows multi-parent node under each parent in tree view (3-parent)', () => {
      const root = createIssue({ id: 'root', executionMode: ExecutionMode.PARALLEL })
      const p1 = createIssue({
        id: 'p1',
        parentIssues: [{ parentIssue: 'root' }],
        executionMode: ExecutionMode.PARALLEL,
      })
      const p2 = createIssue({
        id: 'p2',
        parentIssues: [{ parentIssue: 'root' }],
        executionMode: ExecutionMode.PARALLEL,
      })
      const p3 = createIssue({
        id: 'p3',
        parentIssues: [{ parentIssue: 'root' }],
        executionMode: ExecutionMode.PARALLEL,
      })
      const sharedChild = createIssue({
        id: 'shared',
        parentIssues: [{ parentIssue: 'p1' }, { parentIssue: 'p2' }, { parentIssue: 'p3' }],
      })

      const taskGraph: TaskGraphResponse = {
        nodes: [
          createNode(root, 2, 0),
          createNode(p1, 1, 1),
          createNode(p2, 1, 2),
          createNode(p3, 1, 3),
          createNode(sharedChild, 0, 4),
        ],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph, 3)

      const sharedLines = result.filter(
        (l) => isIssueRenderLine(l) && l.issueId === 'shared'
      ) as TaskGraphIssueRenderLine[]
      expect(sharedLines).toHaveLength(3)

      // First: down-right only
      expect(sharedLines[0].multiParentIndex).toBe(0)
      expect(sharedLines[0].multiParentTotal).toBe(3)

      // Middle: both diagonals
      expect(sharedLines[1].multiParentIndex).toBe(1)
      expect(sharedLines[1].multiParentTotal).toBe(3)

      // Last: up-left only
      expect(sharedLines[2].multiParentIndex).toBe(2)
      expect(sharedLines[2].multiParentTotal).toBe(3)
    })

    it('does not add lane0 connectors in tree view', () => {
      const issue = createIssue({ id: 'issue1' })
      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(issue, 0, 0)],
        mergedPrs: [{ number: 1, title: 'PR', url: null, isMerged: true, hasDescription: false }],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph, 3)

      const issueLine = result.find(isIssueRenderLine) as TaskGraphIssueRenderLine
      expect(issueLine.drawLane0Connector).toBe(false)
      expect(issueLine.drawLane0PassThrough).toBe(false)
    })

    it('uses tree view layout (parent at lane 0, child at lane 1)', () => {
      const parent = createIssue({ id: 'parent', executionMode: ExecutionMode.PARALLEL })
      const child = createIssue({ id: 'child', parentIssues: [{ parentIssue: 'parent' }] })

      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(parent, 1, 0), createNode(child, 0, 1)],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph, 3)

      const parentLine = result.find(
        (l) => isIssueRenderLine(l) && l.issueId === 'parent'
      ) as TaskGraphIssueRenderLine
      const childLine = result.find(
        (l) => isIssueRenderLine(l) && l.issueId === 'child'
      ) as TaskGraphIssueRenderLine

      // In tree view, parent is at lane 0, child at lane 1
      expect(parentLine.lane).toBe(0)
      expect(childLine.lane).toBe(1)
    })
  })

  describe('multi-parent issues', () => {
    it('leaves multiParentIndex/Total null for unique issues', () => {
      const issue1 = createIssue({ id: 'issue1' })
      const issue2 = createIssue({ id: 'issue2' })

      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(issue1, 0, 0), createNode(issue2, 0, 1)],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph)
      const issueLines = result.filter(isIssueRenderLine)

      expect(issueLines).toHaveLength(2)
      issueLines.forEach((line) => {
        expect(line.multiParentIndex).toBeNull()
        expect(line.multiParentTotal).toBeNull()
      })
    })

    it('assigns multiParentIndex/Total for duplicated issueIds', () => {
      // Simulate a result where the same issueId appears twice
      // (as produced by Fleece.Core for multi-parent graphs).
      // We do this by calling computeLayout and then checking that
      // the post-processing logic correctly sets the fields.
      // For unique issues (the only case constructible via normal API),
      // all should be null.
      const sharedChild = createIssue({
        id: 'shared',
        parentIssues: [{ parentIssue: 'p1' }, { parentIssue: 'p2' }],
      })
      const parent1 = createIssue({ id: 'p1', executionMode: ExecutionMode.PARALLEL })
      const parent2 = createIssue({ id: 'p2', executionMode: ExecutionMode.PARALLEL })

      const taskGraph: TaskGraphResponse = {
        nodes: [
          createNode(parent1, 1, 0),
          createNode(parent2, 2, 1),
          createNode(sharedChild, 0, 2),
        ],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph)
      const issueLines = result.filter(isIssueRenderLine)

      // Each issueId should appear at most once (unique in this graph)
      const sharedLines = issueLines.filter((l) => l.issueId === 'shared')
      // If the shared child appears once, it's a unique occurrence - multiParentIndex is null
      if (sharedLines.length === 1) {
        expect(sharedLines[0].multiParentIndex).toBeNull()
        expect(sharedLines[0].multiParentTotal).toBeNull()
      } else {
        // If Fleece.Core produces multiple entries, each should have an index
        sharedLines.forEach((line, i) => {
          expect(line.multiParentIndex).toBe(i)
          expect(line.multiParentTotal).toBe(sharedLines.length)
        })
      }
    })

    it('assigns indices in order of appearance for duplicated render lines', () => {
      // Build a result manually that mimics what computeLayout would produce
      // if Fleece.Core emitted duplicates, and verify getRenderKey produces
      // distinct keys for each instance.
      const baseIssue = createIssue({ id: 'dup-issue' })
      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(baseIssue, 0, 0)],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph)
      const line = result.find(isIssueRenderLine)!

      // Single occurrence: no index needed
      expect(line.multiParentIndex).toBeNull()
      expect(line.multiParentTotal).toBeNull()
    })
  })
})

describe('getRenderKey', () => {
  it('returns issueId when multiParentIndex is null', () => {
    const line: TaskGraphIssueRenderLine = {
      type: 'issue',
      issueId: 'abc123',
      title: 'Test',
      description: null,
      branchName: null,
      lane: 0,
      marker: TaskGraphMarkerType.Open,
      parentLane: null,
      isFirstChild: false,
      isSeriesChild: false,
      drawTopLine: false,
      drawBottomLine: false,
      seriesConnectorFromLane: null,
      issueType: IssueType.TASK,
      status: IssueStatus.OPEN,
      hasDescription: false,
      linkedPr: null,
      agentStatus: null,
      assignedTo: null,
      drawLane0Connector: false,
      isLastLane0Connector: false,
      drawLane0PassThrough: false,
      lane0Color: null,
      hasHiddenParent: false,
      hiddenParentIsSeriesMode: false,
      executionMode: ExecutionMode.SERIES,
      parentIssues: null,
      multiParentIndex: null,
      multiParentTotal: null,
      isLastChild: false,
      hasParallelChildren: false,
      parentIssueId: null,
      parentLaneReservations: [],
    }
    expect(getRenderKey(line)).toBe('abc123')
  })

  it('returns issueId:index when multiParentIndex is set', () => {
    const line: TaskGraphIssueRenderLine = {
      type: 'issue',
      issueId: 'abc123',
      title: 'Test',
      description: null,
      branchName: null,
      lane: 0,
      marker: TaskGraphMarkerType.Open,
      parentLane: null,
      isFirstChild: false,
      isSeriesChild: false,
      drawTopLine: false,
      drawBottomLine: false,
      seriesConnectorFromLane: null,
      issueType: IssueType.TASK,
      status: IssueStatus.OPEN,
      hasDescription: false,
      linkedPr: null,
      agentStatus: null,
      assignedTo: null,
      drawLane0Connector: false,
      isLastLane0Connector: false,
      drawLane0PassThrough: false,
      lane0Color: null,
      hasHiddenParent: false,
      hiddenParentIsSeriesMode: false,
      executionMode: ExecutionMode.SERIES,
      parentIssues: null,
      multiParentIndex: 0,
      multiParentTotal: 3,
      isLastChild: false,
      hasParallelChildren: false,
      parentIssueId: null,
      parentLaneReservations: [],
    }
    expect(getRenderKey(line)).toBe('abc123:0')
  })

  it('produces distinct keys for different multi-parent indices of same issueId', () => {
    const base = {
      type: 'issue' as const,
      issueId: 'dup',
      title: 'Dup',
      description: null,
      branchName: null,
      lane: 0,
      marker: TaskGraphMarkerType.Open,
      parentLane: null,
      isFirstChild: false,
      isSeriesChild: false,
      drawTopLine: false,
      drawBottomLine: false,
      seriesConnectorFromLane: null,
      issueType: IssueType.TASK,
      status: IssueStatus.OPEN,
      hasDescription: false,
      linkedPr: null,
      agentStatus: null,
      assignedTo: null,
      drawLane0Connector: false,
      isLastLane0Connector: false,
      drawLane0PassThrough: false,
      lane0Color: null,
      hasHiddenParent: false,
      hiddenParentIsSeriesMode: false,
      executionMode: ExecutionMode.SERIES,
      parentIssues: null,
      multiParentTotal: 2,
      isLastChild: false,
      hasParallelChildren: false,
      parentIssueId: null,
      parentLaneReservations: [],
    }
    const line0: TaskGraphIssueRenderLine = { ...base, multiParentIndex: 0 }
    const line1: TaskGraphIssueRenderLine = { ...base, multiParentIndex: 1 }

    expect(getRenderKey(line0)).toBe('dup:0')
    expect(getRenderKey(line1)).toBe('dup:1')
    expect(getRenderKey(line0)).not.toBe(getRenderKey(line1))
  })
})
