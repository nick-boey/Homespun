/**
 * Tests for task graph SVG rendering components.
 */

import { describe, it, expect } from 'vitest'
import { render } from '@testing-library/react'
import {
  TaskGraphNodeSvg,
  getTypeColor,
  getRowY,
  ROW_HEIGHT,
  EXPANDED_DETAIL_HEIGHT,
  getLaneCenterX,
} from './task-graph-svg'
import type { TaskGraphIssueRenderLine } from '../services'
import { TaskGraphMarkerType } from '../services'
import { IssueType, IssueStatus, ClaudeSessionStatus, ExecutionMode } from '@/api'

describe('TaskGraphNodeSvg', () => {
  const createMockLine = (
    overrides?: Partial<TaskGraphIssueRenderLine>
  ): TaskGraphIssueRenderLine => ({
    type: 'issue',
    issueId: 'test-id',
    issueType: IssueType.TASK,
    status: IssueStatus.OPEN,
    title: 'Test Issue',
    description: null,
    branchName: null,
    hasDescription: true,
    lane: 0,
    parentLane: null,
    isFirstChild: false,
    drawTopLine: false,
    drawBottomLine: false,
    isSeriesChild: false,
    seriesConnectorFromLane: null,
    drawLane0Connector: false,
    isLastLane0Connector: false,
    drawLane0PassThrough: false,
    lane0Color: null,
    hasHiddenParent: false,
    hiddenParentIsSeriesMode: false,
    marker: TaskGraphMarkerType.Open,
    linkedPr: null,
    agentStatus: null,
    assignedTo: null,
    executionMode: ExecutionMode.SERIES,
    parentIssues: null,
    multiParentIndex: null,
    multiParentTotal: null,
    ...overrides,
  })

  describe('agent status ring', () => {
    it('should not render a ring when agentStatus is null', () => {
      const line = createMockLine({ agentStatus: null })
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={1} />)

      // Should only have the main node circle, no ring
      const circles = container.querySelectorAll('circle')
      expect(circles).toHaveLength(1)
    })

    it('should not render a ring when agent is not active', () => {
      const line = createMockLine({
        agentStatus: {
          isActive: false,
          status: ClaudeSessionStatus.RUNNING,
          sessionId: 'session-123',
        },
      })
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={1} />)

      const circles = container.querySelectorAll('circle')
      expect(circles).toHaveLength(1)
    })

    it('should render blue ring for running states', () => {
      const runningStates = [
        ClaudeSessionStatus.STARTING,
        ClaudeSessionStatus.RUNNING_HOOKS,
        ClaudeSessionStatus.RUNNING,
      ]

      runningStates.forEach((status) => {
        const line = createMockLine({
          agentStatus: {
            isActive: true,
            status,
            sessionId: 'session-123',
          },
        })
        const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={1} />)

        const circles = container.querySelectorAll('circle')
        expect(circles).toHaveLength(2)

        const ring = circles[0]
        expect(ring).toHaveAttribute('stroke', '#3b82f6') // Blue
        expect(ring).toHaveAttribute('stroke-width', '2')
        expect(ring).toHaveAttribute('opacity', '0.6')
        expect(ring).toHaveClass('animate-pulse')
      })
    })

    it('should render yellow ring for waiting states', () => {
      const waitingStates = [
        ClaudeSessionStatus.WAITING_FOR_INPUT,
        ClaudeSessionStatus.WAITING_FOR_QUESTION_ANSWER,
        ClaudeSessionStatus.WAITING_FOR_PLAN_EXECUTION,
      ]

      waitingStates.forEach((status) => {
        const line = createMockLine({
          agentStatus: {
            isActive: true,
            status,
            sessionId: 'session-123',
          },
        })
        const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={1} />)

        const circles = container.querySelectorAll('circle')
        expect(circles).toHaveLength(2)

        const ring = circles[0]
        expect(ring).toHaveAttribute('stroke', '#eab308') // Yellow
        expect(ring).toHaveAttribute('stroke-width', '2')
        expect(ring).toHaveAttribute('opacity', '0.6')
        expect(ring).toHaveClass('animate-pulse')
      })
    })

    it('should render red ring for error state', () => {
      const line = createMockLine({
        agentStatus: {
          isActive: true,
          status: ClaudeSessionStatus.ERROR,
          sessionId: 'session-123',
        },
      })
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={1} />)

      const circles = container.querySelectorAll('circle')
      expect(circles).toHaveLength(2)

      const ring = circles[0]
      expect(ring).toHaveAttribute('stroke', '#ef4444') // Red
      expect(ring).toHaveAttribute('stroke-width', '2')
      expect(ring).toHaveAttribute('opacity', '0.6')
      expect(ring).toHaveClass('animate-pulse')
    })

    it('should not render ring for stopped state', () => {
      const line = createMockLine({
        agentStatus: {
          isActive: true,
          status: ClaudeSessionStatus.STOPPED,
          sessionId: 'session-123',
        },
      })
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={1} />)

      const circles = container.querySelectorAll('circle')
      expect(circles).toHaveLength(1) // Only the main node
    })

    it('should not render ring for unknown status', () => {
      const line = createMockLine({
        agentStatus: {
          isActive: true,
          // Test with an unknown status value (cast to bypass type safety for edge case testing)
          status: '99' as unknown as ClaudeSessionStatus,
          sessionId: 'session-123',
        },
      })
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={1} />)

      const circles = container.querySelectorAll('circle')
      expect(circles).toHaveLength(1) // Only the main node
    })

    it('should not render ring for null status', () => {
      const line = createMockLine({
        agentStatus: {
          isActive: true,
          // Test with null status (cast to bypass type safety for edge case testing)
          status: null as unknown as ClaudeSessionStatus,
          sessionId: 'session-123',
        },
      })
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={1} />)

      const circles = container.querySelectorAll('circle')
      expect(circles).toHaveLength(1) // Only the main node
    })

    it('should have correct ring radius', () => {
      const line = createMockLine({
        agentStatus: {
          isActive: true,
          status: ClaudeSessionStatus.RUNNING,
          sessionId: 'session-123',
        },
      })
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={1} />)

      const circles = container.querySelectorAll('circle')
      const ring = circles[0]
      const node = circles[1]

      // Ring should be 4 pixels larger radius than the node
      expect(ring).toHaveAttribute('r', '10') // NODE_RADIUS (6) + 4
      expect(node).toHaveAttribute('r', '6')
    })

    it('should not show actionable ring when agent is active', () => {
      const line = createMockLine({
        marker: TaskGraphMarkerType.Actionable,
        agentStatus: {
          isActive: true,
          status: ClaudeSessionStatus.RUNNING,
          sessionId: 'session-123',
        },
      })
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={1} />)

      const circles = container.querySelectorAll('circle')
      expect(circles).toHaveLength(2) // Agent ring + node, no actionable ring

      // Verify the ring is the agent status ring (blue)
      const ring = circles[0]
      expect(ring).toHaveAttribute('stroke', '#3b82f6')
      expect(ring).toHaveClass('animate-pulse')
    })

    it('should not show actionable ring when agent is not active', () => {
      const line = createMockLine({
        marker: TaskGraphMarkerType.Actionable,
        agentStatus: {
          isActive: false,
          status: ClaudeSessionStatus.STOPPED,
          sessionId: 'session-123',
        },
      })
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={1} />)

      const circles = container.querySelectorAll('circle')
      expect(circles).toHaveLength(1) // Only the node, no actionable ring
    })
  })

  describe('connector rendering based on isSeriesChild', () => {
    it('renders parallel connector (horizontal leftward) when isSeriesChild=false and parentLane < lane', () => {
      const line = createMockLine({
        isSeriesChild: false,
        lane: 1,
        parentLane: 0,
        isFirstChild: true,
      })
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={2} />)

      // Should have a path for parallel connector (from cx - NODE_RADIUS - 2)
      const paths = container.querySelectorAll('path')
      expect(paths.length).toBeGreaterThan(0)

      // Find the path with horizontal component going leftward
      // For lane 1, cx = 36 (LANE_WIDTH / 2 + 1 * LANE_WIDTH), so start x = 36 - 6 - 2 = 28
      const parallelPath = Array.from(paths).find((p) => {
        const d = p.getAttribute('d') || ''
        return d.includes('M 28 20') // cx - NODE_RADIUS - 2, cy
      })
      expect(parallelPath).toBeDefined()
    })

    it('does NOT render parallel connector when isSeriesChild=true', () => {
      const line = createMockLine({
        isSeriesChild: true,
        lane: 1,
        parentLane: 0,
        isFirstChild: true,
      })
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={2} />)

      // Should NOT have a horizontal path from cx - NODE_RADIUS - 2
      const paths = container.querySelectorAll('path')
      const parallelPath = Array.from(paths).find((p) => {
        const d = p.getAttribute('d') || ''
        // Parallel connector starts from left edge of node: M (cx - NODE_RADIUS - 2) cy
        return d.startsWith('M 28 20') // This is the parallel connector start
      })
      expect(parallelPath).toBeUndefined()
    })

    it('renders series top line when drawTopLine=true', () => {
      const line = createMockLine({
        isSeriesChild: true,
        lane: 0,
        parentLane: 1,
        drawTopLine: true,
      })
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={2} />)

      // Should have a vertical path from top (y=0) to node top edge
      const paths = container.querySelectorAll('path')
      const topLinePath = Array.from(paths).find((p) => {
        const d = p.getAttribute('d') || ''
        // Top line: M cx 0 L cx (cy - NODE_RADIUS - 2)
        // For lane 0: cx = 12, cy = 20, so: M 12 0 L 12 12
        return d.includes('M 12 0 L 12 12')
      })
      expect(topLinePath).toBeDefined()
    })

    it('renders series bottom line when drawBottomLine=true', () => {
      const line = createMockLine({
        isSeriesChild: true,
        lane: 0,
        parentLane: 1,
        drawBottomLine: true,
      })
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={2} />)

      // Should have a vertical path from node bottom edge to bottom (ROW_HEIGHT=40)
      const paths = container.querySelectorAll('path')
      const bottomLinePath = Array.from(paths).find((p) => {
        const d = p.getAttribute('d') || ''
        // Bottom line: M cx (cy + NODE_RADIUS + 2) L cx ROW_HEIGHT
        // For lane 0: cx = 12, cy = 20, so: M 12 28 L 12 40
        return d.includes('M 12 28 L 12 40')
      })
      expect(bottomLinePath).toBeDefined()
    })

    it('does NOT render series lines when drawTopLine and drawBottomLine are false', () => {
      const line = createMockLine({
        isSeriesChild: true,
        lane: 0,
        parentLane: 1,
        drawTopLine: false,
        drawBottomLine: false,
      })
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={2} />)

      // Should NOT have vertical series paths
      const paths = container.querySelectorAll('path')
      const seriesLinePaths = Array.from(paths).filter((p) => {
        const d = p.getAttribute('d') || ''
        // Check for top or bottom series lines
        return d.includes('M 12 0 L 12 12') || d.includes('M 12 28 L 12 40')
      })
      expect(seriesLinePaths).toHaveLength(0)
    })
  })

  describe('multi-parent indicator', () => {
    it('renders no diagonal when multiParentIndex is null', () => {
      const line = createMockLine()
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={1} />)

      const lines = container.querySelectorAll('line')
      // No multi-parent diagonal lines when index is null
      expect(lines).toHaveLength(0)
    })

    it('renders down-right diagonal for first multi-parent instance', () => {
      const line = createMockLine({ multiParentIndex: 0, multiParentTotal: 3 })
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={1} />)

      const lines = container.querySelectorAll('line')
      // First instance (index 0): should have down-right diagonal segment(s)
      expect(lines.length).toBeGreaterThan(0)
    })

    it('renders up-left diagonal for last multi-parent instance', () => {
      const line = createMockLine({ multiParentIndex: 2, multiParentTotal: 3 })
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={1} />)

      const lines = container.querySelectorAll('line')
      // Last instance (index 2 of 3): should have up-left diagonal segment(s)
      expect(lines.length).toBeGreaterThan(0)
    })

    it('renders both diagonals for middle instance', () => {
      const line = createMockLine({ multiParentIndex: 1, multiParentTotal: 3 })
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={1} />)

      const lines = container.querySelectorAll('line')
      // Middle instance (index 1 of 3): should have both diagonal sets
      expect(lines.length).toBeGreaterThan(0)
    })
  })

  describe('type colors', () => {
    it('should return correct colors for each issue type', () => {
      expect(getTypeColor(IssueType.TASK)).toBe('#3b82f6') // Task: Blue
      expect(getTypeColor(IssueType.BUG)).toBe('#ef4444') // Bug: Red
      expect(getTypeColor(IssueType.CHORE)).toBe('#6b7280') // Chore: Gray
      expect(getTypeColor(IssueType.FEATURE)).toBe('#22c55e') // Feature: Green
      expect(getTypeColor(IssueType.IDEA)).toBe('#8b5cf6') // Idea: Purple
    })

    it('should return default color for unknown issue type', () => {
      expect(getTypeColor('unknown' as IssueType)).toBe('#3b82f6') // Default to Task color
    })
  })
})

describe('getRowY', () => {
  const issueLines = [{ issueId: 'a' }, { issueId: 'b' }, { issueId: 'c' }, { issueId: 'd' }]

  it('returns 0 for row index 0 with no expanded rows', () => {
    expect(getRowY(0, new Set(), issueLines)).toBe(0)
  })

  it('returns rowIndex * ROW_HEIGHT with no expanded rows', () => {
    expect(getRowY(2, new Set(), issueLines)).toBe(2 * ROW_HEIGHT)
    expect(getRowY(3, new Set(), issueLines)).toBe(3 * ROW_HEIGHT)
  })

  it('adds EXPANDED_DETAIL_HEIGHT for expanded rows above', () => {
    const expanded = new Set(['a'])
    // Row 1 should be offset by ROW_HEIGHT + EXPANDED_DETAIL_HEIGHT (row 0 is expanded)
    expect(getRowY(1, expanded, issueLines)).toBe(ROW_HEIGHT + EXPANDED_DETAIL_HEIGHT)
  })

  it('adds EXPANDED_DETAIL_HEIGHT for each expanded row above', () => {
    const expanded = new Set(['a', 'b'])
    // Row 2: (ROW_HEIGHT + EXPANDED) + (ROW_HEIGHT + EXPANDED)
    expect(getRowY(2, expanded, issueLines)).toBe(2 * ROW_HEIGHT + 2 * EXPANDED_DETAIL_HEIGHT)
  })

  it('does not add EXPANDED_DETAIL_HEIGHT for expanded row at or after target index', () => {
    const expanded = new Set(['c']) // row index 2
    // Row 1 should not be affected by expanded row at index 2
    expect(getRowY(1, expanded, issueLines)).toBe(ROW_HEIGHT)
  })

  it('handles empty expanded set', () => {
    expect(getRowY(3, new Set(), issueLines)).toBe(3 * ROW_HEIGHT)
  })
})

describe('connector directions', () => {
  const createMockLine = (
    overrides?: Partial<TaskGraphIssueRenderLine>
  ): TaskGraphIssueRenderLine => ({
    type: 'issue',
    issueId: 'test-id',
    issueType: IssueType.TASK,
    status: IssueStatus.OPEN,
    title: 'Test Issue',
    description: null,
    branchName: null,
    hasDescription: true,
    lane: 1,
    parentLane: null,
    isFirstChild: false,
    drawTopLine: false,
    drawBottomLine: false,
    isSeriesChild: false,
    seriesConnectorFromLane: null,
    drawLane0Connector: false,
    isLastLane0Connector: false,
    drawLane0PassThrough: false,
    lane0Color: null,
    hasHiddenParent: false,
    hiddenParentIsSeriesMode: false,
    marker: TaskGraphMarkerType.Open,
    linkedPr: null,
    agentStatus: null,
    assignedTo: null,
    executionMode: ExecutionMode.SERIES,
    parentIssues: null,
    multiParentIndex: null,
    multiParentTotal: null,
    ...overrides,
  })

  it('first-child parallel connector arcs upward toward parent', () => {
    const line = createMockLine({
      lane: 1,
      parentLane: 0,
      isFirstChild: true,
      isSeriesChild: false,
    })
    const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={2} />)

    const paths = container.querySelectorAll('path')
    const firstChildPath = Array.from(paths).find((p) => {
      const d = p.getAttribute('d') ?? ''
      return d.includes('A') && d.includes(`${getLaneCenterX(0)}`)
    })

    expect(firstChildPath).toBeDefined()
    const d = firstChildPath!.getAttribute('d')!
    // Should arc upward: sweep flag 0 0 1, vertical line ends at 0
    expect(d).toContain('0 0 1')
    expect(d).toMatch(/L \d+ 0$/)
  })

  it('series connector starts from bottom of row', () => {
    const line = createMockLine({
      lane: 2,
      seriesConnectorFromLane: 1,
      isSeriesChild: false,
    })
    const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={3} />)

    const paths = container.querySelectorAll('path')
    const seriesPath = Array.from(paths).find((p) => {
      const d = p.getAttribute('d') ?? ''
      return d.includes(`${getLaneCenterX(1)}`) && d.includes('A')
    })

    expect(seriesPath).toBeDefined()
    const d = seriesPath!.getAttribute('d')!
    // Should start from ROW_HEIGHT (bottom of row)
    expect(d).toMatch(new RegExp(`^M ${getLaneCenterX(1)} ${ROW_HEIGHT}`))
    // Should use sweep flag 0 0 1
    expect(d).toContain('0 0 1')
  })
})
