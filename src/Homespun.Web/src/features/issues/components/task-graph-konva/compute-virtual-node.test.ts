/**
 * Tests for compute-virtual-node functions.
 */

import { describe, it, expect } from 'vitest'
import {
  computeVirtualNodeData,
  computeVirtualLane,
  getDisplayRowIndex,
} from './compute-virtual-node'
import type { TaskGraphIssueRenderLine } from '../../services'
import { TaskGraphMarkerType } from '../../services'
import { KeyboardEditMode, type PendingNewIssue } from '../../types'
import { IssueType, IssueStatus, ExecutionMode } from '@/api'

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
    ...overrides,
  }
}

function createPendingIssue(overrides: Partial<PendingNewIssue> = {}): PendingNewIssue {
  return {
    insertAtIndex: 1,
    title: '',
    isAbove: false,
    referenceIssueId: 'issue-2',
    ...overrides,
  }
}

describe('computeVirtualNodeData', () => {
  const issueLines = [
    createIssueLine({ issueId: 'issue-1', lane: 0 }),
    createIssueLine({ issueId: 'issue-2', lane: 0 }),
    createIssueLine({ issueId: 'issue-3', lane: 0 }),
  ]

  it('returns null when not in CreatingNew mode', () => {
    const pending = createPendingIssue()
    const result = computeVirtualNodeData(pending, KeyboardEditMode.Viewing, issueLines)
    expect(result).toBeNull()
  })

  it('returns null when pendingNewIssue is null', () => {
    const result = computeVirtualNodeData(null, KeyboardEditMode.CreatingNew, issueLines)
    expect(result).toBeNull()
  })

  it('returns null when reference issue not found', () => {
    const pending = createPendingIssue({ referenceIssueId: 'nonexistent' })
    const result = computeVirtualNodeData(pending, KeyboardEditMode.CreatingNew, issueLines)
    expect(result).toBeNull()
  })

  it('positions editor exactly 1 row below selected issue for o key', () => {
    const pending = createPendingIssue({ isAbove: false, referenceIssueId: 'issue-2' })
    const result = computeVirtualNodeData(pending, KeyboardEditMode.CreatingNew, issueLines)

    expect(result).not.toBeNull()
    // issue-2 is at index 1, so insertion should be at index 2 (1 row below)
    expect(result!.insertionRowIndex).toBe(2)
  })

  it('positions editor exactly 1 row above selected issue for Shift+O', () => {
    const pending = createPendingIssue({ isAbove: true, referenceIssueId: 'issue-2' })
    const result = computeVirtualNodeData(pending, KeyboardEditMode.CreatingNew, issueLines)

    expect(result).not.toBeNull()
    // issue-2 is at index 1, so insertion should be at index 1 (1 row above, displacing issue-2 down)
    expect(result!.insertionRowIndex).toBe(1)
  })

  it('uses same lane as reference issue by default', () => {
    const laneLines = [
      createIssueLine({ issueId: 'issue-1', lane: 0 }),
      createIssueLine({ issueId: 'issue-2', lane: 2 }),
    ]
    const pending = createPendingIssue({ referenceIssueId: 'issue-2' })
    const result = computeVirtualNodeData(pending, KeyboardEditMode.CreatingNew, laneLines)

    expect(result!.virtualLane).toBe(2)
  })

  it('moves virtual node to parent lane when Tab is pressed (pendingChildId set)', () => {
    const laneLines = [
      createIssueLine({ issueId: 'issue-1', lane: 0 }),
      createIssueLine({ issueId: 'issue-2', lane: 1 }),
    ]
    const pending = createPendingIssue({
      referenceIssueId: 'issue-2',
      pendingChildId: 'issue-2',
    })
    const result = computeVirtualNodeData(pending, KeyboardEditMode.CreatingNew, laneLines)

    // Tab: new issue becomes parent → one lane right (1 + 1 = 2)
    expect(result!.virtualLane).toBe(2)
  })

  it('moves virtual node to child lane when Shift+Tab is pressed (pendingParentId set)', () => {
    const laneLines = [
      createIssueLine({ issueId: 'issue-1', lane: 0 }),
      createIssueLine({ issueId: 'issue-2', lane: 2 }),
    ]
    const pending = createPendingIssue({
      referenceIssueId: 'issue-2',
      pendingParentId: 'issue-2',
    })
    const result = computeVirtualNodeData(pending, KeyboardEditMode.CreatingNew, laneLines)

    // Shift+Tab: new issue becomes child → one lane left (2 - 1 = 1)
    expect(result!.virtualLane).toBe(1)
  })

  it('clamps virtual lane to 0 when Shift+Tab at lane 0', () => {
    const laneLines = [createIssueLine({ issueId: 'issue-1', lane: 0 })]
    const pending = createPendingIssue({
      referenceIssueId: 'issue-1',
      pendingParentId: 'issue-1',
      isAbove: false,
    })
    const result = computeVirtualNodeData(pending, KeyboardEditMode.CreatingNew, laneLines)

    expect(result!.virtualLane).toBe(0)
  })
})

describe('computeVirtualLane', () => {
  it('returns same lane for sibling (no hierarchy change)', () => {
    const refLine = createIssueLine({ issueId: 'ref', lane: 3 })
    const pending = createPendingIssue()
    expect(computeVirtualLane(refLine, pending)).toBe(3)
  })

  it('returns lane + 1 when Tab sets pendingChildId', () => {
    const refLine = createIssueLine({ issueId: 'ref', lane: 1 })
    const pending = createPendingIssue({ pendingChildId: 'ref' })
    expect(computeVirtualLane(refLine, pending)).toBe(2)
  })

  it('returns lane - 1 when Shift+Tab sets pendingParentId', () => {
    const refLine = createIssueLine({ issueId: 'ref', lane: 2 })
    const pending = createPendingIssue({ pendingParentId: 'ref' })
    expect(computeVirtualLane(refLine, pending)).toBe(1)
  })

  it('clamps to 0 when pendingParentId at lane 0', () => {
    const refLine = createIssueLine({ issueId: 'ref', lane: 0 })
    const pending = createPendingIssue({ pendingParentId: 'ref' })
    expect(computeVirtualLane(refLine, pending)).toBe(0)
  })
})

describe('getDisplayRowIndex', () => {
  it('returns original index when no insertion', () => {
    expect(getDisplayRowIndex(0, null)).toBe(0)
    expect(getDisplayRowIndex(3, null)).toBe(3)
  })

  it('returns original index for rows before insertion point', () => {
    expect(getDisplayRowIndex(0, 2)).toBe(0)
    expect(getDisplayRowIndex(1, 2)).toBe(1)
  })

  it('shifts rows at insertion point down by 1', () => {
    expect(getDisplayRowIndex(2, 2)).toBe(3)
  })

  it('shifts rows after insertion point down by 1', () => {
    expect(getDisplayRowIndex(3, 2)).toBe(4)
    expect(getDisplayRowIndex(5, 2)).toBe(6)
  })

  it('handles insertion at row 0', () => {
    expect(getDisplayRowIndex(0, 0)).toBe(1)
    expect(getDisplayRowIndex(1, 0)).toBe(2)
  })
})
