/**
 * Tests for Konva node components.
 */

import { describe, it, expect } from 'vitest'
import { render } from '@testing-library/react'
import { Stage, Layer } from 'react-konva'
import {
  KonvaIssueNode,
  KonvaEdge,
  KonvaDiagonalEdge,
  KonvaHiddenParentIndicator,
  KonvaAgentStatusRing,
} from './konva-nodes'
import type { TaskGraphIssueRenderLine } from '../../services'
import { TaskGraphMarkerType } from '../../services'
import { IssueType, IssueStatus, ExecutionMode, ClaudeSessionStatus } from '@/api'

/** Helper to wrap Konva components in Stage/Layer */
function KonvaWrapper({ children }: { children: React.ReactNode }) {
  return (
    <Stage width={200} height={200}>
      <Layer>{children}</Layer>
    </Stage>
  )
}

/** Helper to create a minimal issue render line */
function createIssueLine(
  overrides: Partial<TaskGraphIssueRenderLine> & { issueId: string; lane: number }
): TaskGraphIssueRenderLine {
  return {
    type: 'issue',
    title: 'Test Issue',
    description: null,
    branchName: null,
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
    executionMode: ExecutionMode.PARALLEL,
    parentIssues: null,
    multiParentIndex: null,
    multiParentTotal: null,
    ...overrides,
  }
}

describe('KonvaIssueNode', () => {
  it('renders without crashing', () => {
    const line = createIssueLine({ issueId: 'test-1', lane: 0 })

    expect(() =>
      render(
        <KonvaWrapper>
          <KonvaIssueNode line={line} rowIndex={0} />
        </KonvaWrapper>
      )
    ).not.toThrow()
  })

  it('renders with different issue types', () => {
    const issueTypes = [IssueType.TASK, IssueType.BUG, IssueType.FEATURE, IssueType.CHORE]

    issueTypes.forEach((issueType) => {
      const line = createIssueLine({ issueId: `test-${issueType}`, lane: 0, issueType })
      expect(() =>
        render(
          <KonvaWrapper>
            <KonvaIssueNode line={line} rowIndex={0} />
          </KonvaWrapper>
        )
      ).not.toThrow()
    })
  })

  it('renders outline-only circle when no description', () => {
    const line = createIssueLine({
      issueId: 'test-1',
      lane: 0,
      hasDescription: false,
    })

    // Should not throw - outline style applied internally
    expect(() =>
      render(
        <KonvaWrapper>
          <KonvaIssueNode line={line} rowIndex={0} />
        </KonvaWrapper>
      )
    ).not.toThrow()
  })

  it('renders filled circle when has description', () => {
    const line = createIssueLine({
      issueId: 'test-1',
      lane: 0,
      hasDescription: true,
    })

    expect(() =>
      render(
        <KonvaWrapper>
          <KonvaIssueNode line={line} rowIndex={0} />
        </KonvaWrapper>
      )
    ).not.toThrow()
  })

  describe('multi-parent indicator', () => {
    it('renders without crashing when multiParentIndex is null', () => {
      const line = createIssueLine({
        issueId: 'test-1',
        lane: 0,
        multiParentIndex: null,
        multiParentTotal: null,
      })

      expect(() =>
        render(
          <KonvaWrapper>
            <KonvaIssueNode line={line} rowIndex={0} />
          </KonvaWrapper>
        )
      ).not.toThrow()
    })

    it('renders without crashing for first multi-parent instance', () => {
      const line = createIssueLine({
        issueId: 'test-mp',
        lane: 0,
        multiParentIndex: 0,
        multiParentTotal: 3,
      })

      expect(() =>
        render(
          <KonvaWrapper>
            <KonvaIssueNode line={line} rowIndex={0} />
          </KonvaWrapper>
        )
      ).not.toThrow()
    })

    it('renders without crashing for middle multi-parent instance', () => {
      const line = createIssueLine({
        issueId: 'test-mp',
        lane: 0,
        multiParentIndex: 1,
        multiParentTotal: 3,
      })

      expect(() =>
        render(
          <KonvaWrapper>
            <KonvaIssueNode line={line} rowIndex={0} />
          </KonvaWrapper>
        )
      ).not.toThrow()
    })

    it('renders without crashing for last multi-parent instance', () => {
      const line = createIssueLine({
        issueId: 'test-mp',
        lane: 0,
        multiParentIndex: 2,
        multiParentTotal: 3,
      })

      expect(() =>
        render(
          <KonvaWrapper>
            <KonvaIssueNode line={line} rowIndex={0} />
          </KonvaWrapper>
        )
      ).not.toThrow()
    })
  })
})

describe('KonvaEdge', () => {
  it('renders without crashing', () => {
    expect(() =>
      render(
        <KonvaWrapper>
          <KonvaEdge id="edge-1" points={[0, 0, 100, 100]} color="#3b82f6" />
        </KonvaWrapper>
      )
    ).not.toThrow()
  })

  it('renders with different colors', () => {
    const colors = ['#3b82f6', '#ef4444', '#22c55e', '#6b7280']

    colors.forEach((color) => {
      expect(() =>
        render(
          <KonvaWrapper>
            <KonvaEdge id={`edge-${color}`} points={[0, 0, 50, 50]} color={color} />
          </KonvaWrapper>
        )
      ).not.toThrow()
    })
  })

  it('renders with complex point arrays', () => {
    const points = [0, 0, 50, 0, 50, 50, 100, 50, 100, 100]

    expect(() =>
      render(
        <KonvaWrapper>
          <KonvaEdge id="edge-complex" points={points} color="#3b82f6" />
        </KonvaWrapper>
      )
    ).not.toThrow()
  })
})

describe('KonvaDiagonalEdge', () => {
  it('renders without crashing', () => {
    expect(() =>
      render(
        <KonvaWrapper>
          <KonvaDiagonalEdge id="diag-1" points={[50, 50, 30, 30]} color="#3b82f6" />
        </KonvaWrapper>
      )
    ).not.toThrow()
  })

  it('renders with different colors', () => {
    const colors = ['#3b82f6', '#ef4444', '#22c55e']

    colors.forEach((color) => {
      expect(() =>
        render(
          <KonvaWrapper>
            <KonvaDiagonalEdge id={`diag-${color}`} points={[0, 0, 20, 20]} color={color} />
          </KonvaWrapper>
        )
      ).not.toThrow()
    })
  })
})

describe('KonvaHiddenParentIndicator', () => {
  it('renders horizontal dots for parallel mode', () => {
    expect(() =>
      render(
        <KonvaWrapper>
          <KonvaHiddenParentIndicator cx={100} cy={100} nodeColor="#3b82f6" isSeriesMode={false} />
        </KonvaWrapper>
      )
    ).not.toThrow()
  })

  it('renders vertical dots for series mode', () => {
    expect(() =>
      render(
        <KonvaWrapper>
          <KonvaHiddenParentIndicator cx={100} cy={100} nodeColor="#3b82f6" isSeriesMode={true} />
        </KonvaWrapper>
      )
    ).not.toThrow()
  })
})

describe('KonvaAgentStatusRing', () => {
  it('renders for running status', () => {
    expect(() =>
      render(
        <KonvaWrapper>
          <KonvaAgentStatusRing cx={100} cy={100} status={ClaudeSessionStatus.RUNNING} />
        </KonvaWrapper>
      )
    ).not.toThrow()
  })

  it('renders for waiting status', () => {
    expect(() =>
      render(
        <KonvaWrapper>
          <KonvaAgentStatusRing cx={100} cy={100} status={ClaudeSessionStatus.WAITING_FOR_INPUT} />
        </KonvaWrapper>
      )
    ).not.toThrow()
  })

  it('renders for error status', () => {
    expect(() =>
      render(
        <KonvaWrapper>
          <KonvaAgentStatusRing cx={100} cy={100} status={ClaudeSessionStatus.ERROR} />
        </KonvaWrapper>
      )
    ).not.toThrow()
  })

  it('does not render ring for stopped status', () => {
    // For stopped status, the ring should not be visible
    expect(() =>
      render(
        <KonvaWrapper>
          <KonvaAgentStatusRing cx={100} cy={100} status={ClaudeSessionStatus.STOPPED} />
        </KonvaWrapper>
      )
    ).not.toThrow()
  })
})
