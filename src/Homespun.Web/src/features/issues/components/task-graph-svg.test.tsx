/**
 * Tests for task graph SVG rendering components.
 */

import { describe, it, expect } from 'vitest'
import { render } from '@testing-library/react'
import { TaskGraphNodeSvg, getTypeColor } from './task-graph-svg'
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
    it('renders parallel connector (horizontal) when isSeriesChild=false and parentLane > lane', () => {
      const line = createMockLine({
        isSeriesChild: false,
        lane: 0,
        parentLane: 1,
        isFirstChild: true,
      })
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={2} />)

      // Should have a path for parallel connector (from cx + NODE_RADIUS + 2)
      const paths = container.querySelectorAll('path')
      expect(paths.length).toBeGreaterThan(0)

      // Find the path with horizontal component (M x y L x2 y ... which is the parallel connector)
      const parallelPath = Array.from(paths).find((p) => {
        const d = p.getAttribute('d') || ''
        // Parallel connector starts from right edge of node: cx + NODE_RADIUS + 2
        // For lane 0, cx = 12 (LANE_WIDTH / 2 + 0 * LANE_WIDTH), so start x = 12 + 6 + 2 = 20
        return d.includes('M 20 20') // cx + NODE_RADIUS + 2, cy
      })
      expect(parallelPath).toBeDefined()
    })

    it('does NOT render parallel connector when isSeriesChild=true', () => {
      const line = createMockLine({
        isSeriesChild: true,
        lane: 0,
        parentLane: 1,
        isFirstChild: true,
      })
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={2} />)

      // Should NOT have a horizontal path from cx + NODE_RADIUS + 2
      const paths = container.querySelectorAll('path')
      const parallelPath = Array.from(paths).find((p) => {
        const d = p.getAttribute('d') || ''
        // Parallel connector starts from right edge of node: M (cx + NODE_RADIUS + 2) cy
        return d.startsWith('M 20 20') // This is the parallel connector start
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
