/**
 * Tests for task graph SVG rendering components.
 */

import { describe, it, expect } from 'vitest'
import { render } from '@testing-library/react'
import { TaskGraphNodeSvg, getTypeColor } from './task-graph-svg'
import type { TaskGraphIssueRenderLine } from '../services'
import { TaskGraphMarkerType } from '../services'

describe('TaskGraphNodeSvg', () => {
  const createMockLine = (
    overrides?: Partial<TaskGraphIssueRenderLine>
  ): TaskGraphIssueRenderLine => ({
    type: 'issue',
    issueId: 'test-id',
    issueType: 0, // Task
    status: 0, // Open
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
          status: '2', // Running
          sessionId: 'session-123',
        },
      })
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={1} />)

      const circles = container.querySelectorAll('circle')
      expect(circles).toHaveLength(1)
    })

    it('should render blue ring for running states', () => {
      const runningStates = [
        // Test both numeric and string formats
        '0',
        '1',
        '2', // Starting, RunningHooks, Running
        'Starting',
        'RunningHooks',
        'Running',
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
        // Test both numeric and string formats
        '3',
        '4',
        '5', // WaitingForInput, WaitingForQuestionAnswer, WaitingForPlanExecution
        'WaitingForInput',
        'WaitingForQuestionAnswer',
        'WaitingForPlanExecution',
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
      const errorStates = ['7', 'Error'] // Test both formats

      errorStates.forEach((status) => {
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
        expect(ring).toHaveAttribute('stroke', '#ef4444') // Red
        expect(ring).toHaveAttribute('stroke-width', '2')
        expect(ring).toHaveAttribute('opacity', '0.6')
        expect(ring).toHaveClass('animate-pulse')
      })
    })

    it('should not render ring for stopped state', () => {
      const stoppedStates = ['6', 'Stopped'] // Test both formats

      stoppedStates.forEach((status) => {
        const line = createMockLine({
          agentStatus: {
            isActive: true,
            status,
            sessionId: 'session-123',
          },
        })
        const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={1} />)

        const circles = container.querySelectorAll('circle')
        expect(circles).toHaveLength(1) // Only the main node
      })
    })

    it('should not render ring for unknown status', () => {
      const line = createMockLine({
        agentStatus: {
          isActive: true,
          status: '99', // Unknown
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
          status: null,
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
          status: '2', // Running
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
          status: 'Running',
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

    it('should show actionable ring when agent is not active', () => {
      const line = createMockLine({
        marker: TaskGraphMarkerType.Actionable,
        agentStatus: {
          isActive: false,
          status: 'Stopped',
          sessionId: 'session-123',
        },
      })
      const { container } = render(<TaskGraphNodeSvg line={line} maxLanes={1} />)

      const circles = container.querySelectorAll('circle')
      expect(circles).toHaveLength(2) // Actionable ring + node

      // Verify the ring is the actionable ring (no animation)
      const ring = circles[0]
      expect(ring).toHaveAttribute('opacity', '0.4')
      expect(ring).not.toHaveClass('animate-pulse')
    })
  })

  describe('type colors', () => {
    it('should return correct colors for each issue type', () => {
      expect(getTypeColor(0)).toBe('#3b82f6') // Task: Blue
      expect(getTypeColor(1)).toBe('#ef4444') // Bug: Red
      expect(getTypeColor(2)).toBe('#6b7280') // Chore: Gray
      expect(getTypeColor(3)).toBe('#22c55e') // Feature: Green
      expect(getTypeColor(4)).toBe('#8b5cf6') // Idea: Purple
    })

    it('should return default color for unknown issue type', () => {
      expect(getTypeColor(99)).toBe('#3b82f6') // Default to Task color
    })
  })
})
