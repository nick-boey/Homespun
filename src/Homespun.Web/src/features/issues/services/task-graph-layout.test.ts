import { describe, it, expect } from 'vitest'
import {
  computeLayout,
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

// Helper to create a mock issue
function createIssue(overrides: Partial<IssueResponse> = {}): IssueResponse {
  return {
    id: 'test-123',
    title: 'Test Issue',
    description: 'Test description',
    status: 0, // Open
    type: 0, // Task
    parentIssues: [],
    executionMode: 0, // Parallel
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
        issueType: 0,
        status: 0,
        hasDescription: false,
        linkedPr: null,
        agentStatus: null,
        drawLane0Connector: false,
        isLastLane0Connector: false,
        drawLane0PassThrough: false,
        lane0Color: null,
        hasHiddenParent: false,
        hiddenParentIsSeriesMode: false,
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
      const issue = createIssue({ status: 0 }) // Open
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
      const issue = createIssue({ status: 1 }) // Complete
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
      const issue = createIssue({ status: 2 }) // Closed
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

  describe('merged PRs rendering', () => {
    it('adds load more line when hasMorePastPrs is true', () => {
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

      expect(result[0].type).toBe('loadMore')
    })

    it('renders merged PRs with correct flags', () => {
      const taskGraph: TaskGraphResponse = {
        nodes: [],
        mergedPrs: [
          { number: 1, title: 'PR 1', url: null, isMerged: true, hasDescription: false },
          { number: 2, title: 'PR 2', url: null, isMerged: false, hasDescription: true },
        ],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph)

      expect(result).toHaveLength(2)

      const pr1 = result[0] as TaskGraphPrRenderLine
      expect(pr1.prNumber).toBe(1)
      expect(pr1.drawTopLine).toBe(false) // First PR has no top line
      expect(pr1.drawBottomLine).toBe(true) // Not last, has bottom line

      const pr2 = result[1] as TaskGraphPrRenderLine
      expect(pr2.prNumber).toBe(2)
      expect(pr2.drawTopLine).toBe(true) // Not first, has top line
      expect(pr2.drawBottomLine).toBe(false) // Last PR, no issues below
    })

    it('adds separator between PRs and issues', () => {
      const issue = createIssue()
      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(issue, 0, 0)],
        mergedPrs: [{ number: 1, title: 'PR', url: null, isMerged: true, hasDescription: false }],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph)

      // Should be: PR, Separator, Issue
      expect(result).toHaveLength(3)
      expect(result[0].type).toBe('pr')
      expect(result[1].type).toBe('separator')
      expect(result[2].type).toBe('issue')
    })

    it('offsets issue lanes by 1 when PRs exist', () => {
      const issue = createIssue()
      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(issue, 0, 0)],
        mergedPrs: [{ number: 1, title: 'PR', url: null, isMerged: true, hasDescription: false }],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph)
      const issueLine = result[2] as TaskGraphIssueRenderLine

      expect(issueLine.lane).toBe(1) // Lane 0 + offset 1
    })
  })

  describe('parent-child relationships', () => {
    it('renders parent-child with correct lanes and parentLane', () => {
      const parent = createIssue({ id: 'parent', executionMode: 0 }) // Parallel
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

      const childLine = result.find(
        (l) => isIssueRenderLine(l) && l.issueId === 'child'
      ) as TaskGraphIssueRenderLine
      expect(childLine.lane).toBe(0)
      expect(childLine.parentLane).toBe(1)
      expect(childLine.isSeriesChild).toBe(false)
    })

    it('renders series children with correct flags', () => {
      const parent = createIssue({ id: 'parent', executionMode: 1 }) // Series
      const child1 = createIssue({ id: 'child1', parentIssues: [{ parentIssue: 'parent' }] })
      const child2 = createIssue({ id: 'child2', parentIssues: [{ parentIssue: 'parent' }] })

      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(parent, 1, 0), createNode(child1, 0, 1), createNode(child2, 0, 2)],
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
    it('filters nodes beyond maxDepth', () => {
      const parent = createIssue({ id: 'parent' })
      const child = createIssue({ id: 'child', parentIssues: [{ parentIssue: 'parent' }] })
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

      // maxDepth = 1 means show lanes 0 and 1 (relative to min lane in group)
      const result = computeLayout(taskGraph, 1)

      // grandchild at lane 0 should be shown (depth 0)
      // child at lane 1 should be shown (depth 1)
      // parent at lane 2 should be filtered (depth 2 > maxDepth 1)
      expect(result).toHaveLength(2)
      const issueIds = result.filter(isIssueRenderLine).map((l) => l.issueId)
      expect(issueIds).toContain('grandchild')
      expect(issueIds).toContain('child')
      expect(issueIds).not.toContain('parent')
    })

    it('marks hidden parent indicator when parent is filtered', () => {
      const parent = createIssue({ id: 'parent', executionMode: 0 }) // Parallel
      const child = createIssue({ id: 'child', parentIssues: [{ parentIssue: 'parent' }] })

      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(parent, 1, 0), createNode(child, 0, 1)],
        mergedPrs: [],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      // maxDepth = 0 means only show lane 0 (relative)
      const result = computeLayout(taskGraph, 0)

      expect(result).toHaveLength(1)
      const childLine = result[0] as TaskGraphIssueRenderLine
      expect(childLine.issueId).toBe('child')
      expect(childLine.hasHiddenParent).toBe(true)
      expect(childLine.hiddenParentIsSeriesMode).toBe(false) // Parent is parallel
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

  describe('lane 0 connectors', () => {
    it('adds lane 0 connectors when PRs exist', () => {
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

      expect(issueLine.lane).toBe(1) // Offset by 1
      expect(issueLine.drawLane0Connector).toBe(true)
      expect(issueLine.isLastLane0Connector).toBe(true) // Only issue, so it's the last
    })

    it('marks last lane 0 connector correctly with multiple issues', () => {
      const issue1 = createIssue({ id: 'issue1' })
      const issue2 = createIssue({ id: 'issue2' })

      const taskGraph: TaskGraphResponse = {
        nodes: [createNode(issue1, 0, 0), createNode(issue2, 0, 1)],
        mergedPrs: [{ number: 1, title: 'PR', url: null, isMerged: true, hasDescription: false }],
        hasMorePastPrs: false,
        agentStatuses: {},
        linkedPrs: {},
      }

      const result = computeLayout(taskGraph)
      const issueLines = result.filter(isIssueRenderLine)

      const issue1Line = issueLines.find((l) => l.issueId === 'issue1')!
      const issue2Line = issueLines.find((l) => l.issueId === 'issue2')!

      expect(issue1Line.drawLane0Connector).toBe(true)
      expect(issue1Line.isLastLane0Connector).toBe(false)

      expect(issue2Line.drawLane0Connector).toBe(true)
      expect(issue2Line.isLastLane0Connector).toBe(true)
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

  describe('branch name generation', () => {
    it('generates branch name for issues', () => {
      const issue = createIssue({
        id: 'abc123',
        title: 'Add feature',
        type: 0, // Task
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
})
