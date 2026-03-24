/**
 * Tests for use-edge-paths hook.
 *
 * Tests edge path computation from task graph render lines.
 * The algorithm computes edges based on actual node positions (X, Y)
 * rather than row-by-row flags.
 */

import { describe, it, expect } from 'vitest'
import { computeEdgePaths } from './use-edge-paths'
import type { TaskGraphIssueRenderLine, TaskGraphPrRenderLine } from '../../services'
import { IssueType, IssueStatus, ExecutionMode } from '@/api'
import { TaskGraphMarkerType } from '../../services'
import { ROW_HEIGHT, NODE_RADIUS, getLaneCenterX, getRowCenterY } from '../task-graph-svg'

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

/** Helper to create a PR render line */
function createPrLine(
  overrides: Partial<TaskGraphPrRenderLine> & { prNumber: number }
): TaskGraphPrRenderLine {
  return {
    type: 'pr',
    title: 'Test PR',
    url: null,
    isMerged: true,
    hasDescription: false,
    agentStatus: null,
    drawTopLine: false,
    drawBottomLine: false,
    ...overrides,
  }
}

describe('computeEdgePaths', () => {
  describe('basic functionality', () => {
    it('returns empty array for empty input', () => {
      const result = computeEdgePaths([])
      expect(result).toEqual([])
    })

    it('returns empty array for single issue with no connections', () => {
      const lines = [createIssueLine({ issueId: 'issue-1', lane: 0 })]
      const result = computeEdgePaths(lines)
      expect(result).toEqual([])
    })

    it('ignores non-issue render lines', () => {
      const lines = [
        createPrLine({ prNumber: 1, drawTopLine: false, drawBottomLine: true }),
        createIssueLine({ issueId: 'issue-1', lane: 0 }),
      ]
      const result = computeEdgePaths(lines)
      expect(result).toEqual([])
    })

    it('returns no edge when parent is not in render lines', () => {
      const lines = [
        createIssueLine({
          issueId: 'child-1',
          lane: 0,
          parentIssues: [{ parentIssue: 'missing-parent', sortOrder: 'V' }],
          isSeriesChild: false,
        }),
      ]
      const result = computeEdgePaths(lines)
      expect(result).toEqual([])
    })
  })

  describe('parallel mode edges (child right -> parent top)', () => {
    it('generates L-shaped path for parallel child to parent', () => {
      // child-1 at row 0, lane 0; parent-1 at row 1, lane 1
      const lines = [
        createIssueLine({
          issueId: 'child-1',
          lane: 0,
          isSeriesChild: false,
          parentIssues: [{ parentIssue: 'parent-1', sortOrder: 'V' }],
        }),
        createIssueLine({ issueId: 'parent-1', lane: 1 }),
      ]
      const result = computeEdgePaths(lines)

      expect(result.length).toBe(1)
      const edge = result[0]
      expect(edge.fromIssueId).toBe('child-1')
      expect(edge.toIssueId).toBe('parent-1')
      expect(edge.isSeriesEdge).toBe(false)

      // Parallel: exit child right, enter parent top
      const childCx = getLaneCenterX(0)
      const childCy = 0 * ROW_HEIGHT + getRowCenterY()
      const parentCx = getLaneCenterX(1)
      const parentCy = 1 * ROW_HEIGHT + getRowCenterY()

      // Start: child right edge
      expect(edge.points[0]).toBe(childCx + NODE_RADIUS + 2)
      expect(edge.points[1]).toBe(childCy)
      // Middle: horizontal to parent's x
      expect(edge.points[2]).toBe(parentCx)
      expect(edge.points[3]).toBe(childCy)
      // End: parent top
      expect(edge.points[4]).toBe(parentCx)
      expect(edge.points[5]).toBe(parentCy - NODE_RADIUS - 2)
    })

    it('generates independent edges for multiple parallel children', () => {
      const lines = [
        createIssueLine({
          issueId: 'child-1',
          lane: 0,
          isSeriesChild: false,
          parentIssues: [{ parentIssue: 'parent-1', sortOrder: 'V' }],
        }),
        createIssueLine({
          issueId: 'child-2',
          lane: 1,
          isSeriesChild: false,
          parentIssues: [{ parentIssue: 'parent-1', sortOrder: 'VV' }],
        }),
        createIssueLine({ issueId: 'parent-1', lane: 2 }),
      ]
      const result = computeEdgePaths(lines)

      // Each child gets its own edge to parent
      expect(result.length).toBe(2)
      expect(result[0].fromIssueId).toBe('child-1')
      expect(result[0].toIssueId).toBe('parent-1')
      expect(result[1].fromIssueId).toBe('child-2')
      expect(result[1].toIssueId).toBe('parent-1')
    })
  })

  describe('series mode edges (child bottom -> parent left)', () => {
    it('generates vertical path for series child in same lane', () => {
      // child at row 1, parent at row 0, both in lane 0
      const lines = [
        createIssueLine({
          issueId: 'parent-1',
          lane: 0,
          executionMode: ExecutionMode.SERIES,
        }),
        createIssueLine({
          issueId: 'child-1',
          lane: 0,
          isSeriesChild: true,
          parentIssues: [{ parentIssue: 'parent-1', sortOrder: 'V' }],
        }),
      ]
      const result = computeEdgePaths(lines)

      const seriesEdges = result.filter((e) => e.isSeriesEdge)
      expect(seriesEdges.length).toBe(1)

      const edge = seriesEdges[0]
      expect(edge.fromIssueId).toBe('child-1')
      expect(edge.toIssueId).toBe('parent-1')

      // Series same lane: vertical from child bottom to parent bottom (enters at left = same as bottom for same lane)
      const cx = getLaneCenterX(0)
      const childCy = 1 * ROW_HEIGHT + getRowCenterY()
      const parentCy = 0 * ROW_HEIGHT + getRowCenterY()

      // Vertical line from child top to parent bottom
      expect(edge.points[0]).toBe(cx)
      expect(edge.points[1]).toBe(parentCy + NODE_RADIUS + 2)
      expect(edge.points[2]).toBe(cx)
      expect(edge.points[3]).toBe(childCy - NODE_RADIUS - 2)
    })

    it('generates L-shaped path for series child in different lane', () => {
      // parent at row 0, lane 1; child at row 1, lane 0
      const lines = [
        createIssueLine({
          issueId: 'parent-1',
          lane: 1,
          executionMode: ExecutionMode.SERIES,
        }),
        createIssueLine({
          issueId: 'child-1',
          lane: 0,
          isSeriesChild: true,
          parentIssues: [{ parentIssue: 'parent-1', sortOrder: 'V' }],
        }),
      ]
      const result = computeEdgePaths(lines)

      expect(result.length).toBe(1)
      const edge = result[0]
      expect(edge.fromIssueId).toBe('child-1')
      expect(edge.toIssueId).toBe('parent-1')
      expect(edge.isSeriesEdge).toBe(true)

      // Series different lane: exit child bottom, go down, horizontal to parent left
      const childCx = getLaneCenterX(0)
      const childCy = 1 * ROW_HEIGHT + getRowCenterY()
      const parentCx = getLaneCenterX(1)
      const parentCy = 0 * ROW_HEIGHT + getRowCenterY()

      // Start: child bottom
      expect(edge.points[0]).toBe(childCx)
      expect(edge.points[1]).toBe(childCy - NODE_RADIUS - 2)
      // Middle: vertical up to parent row
      expect(edge.points[2]).toBe(childCx)
      expect(edge.points[3]).toBe(parentCy)
      // End: horizontal to parent left
      expect(edge.points[4]).toBe(parentCx - NODE_RADIUS - 2)
      expect(edge.points[5]).toBe(parentCy)
    })

    it('generates edges for multi-level series chain', () => {
      // grandparent -> parent -> child, all series, same lane
      const lines = [
        createIssueLine({
          issueId: 'grandparent',
          lane: 0,
          executionMode: ExecutionMode.SERIES,
        }),
        createIssueLine({
          issueId: 'parent-1',
          lane: 0,
          isSeriesChild: true,
          executionMode: ExecutionMode.SERIES,
          parentIssues: [{ parentIssue: 'grandparent', sortOrder: 'V' }],
        }),
        createIssueLine({
          issueId: 'child-1',
          lane: 0,
          isSeriesChild: true,
          parentIssues: [{ parentIssue: 'parent-1', sortOrder: 'V' }],
        }),
      ]
      const result = computeEdgePaths(lines)

      // Two edges: parent->grandparent, child->parent
      expect(result.length).toBe(2)
      expect(result[0].fromIssueId).toBe('parent-1')
      expect(result[0].toIssueId).toBe('grandparent')
      expect(result[1].fromIssueId).toBe('child-1')
      expect(result[1].toIssueId).toBe('parent-1')
    })
  })

  describe('lane 0 connector edges', () => {
    it('generates lane 0 connector for merged PR connection', () => {
      const lines = [
        createIssueLine({
          issueId: 'issue-1',
          lane: 1,
          drawLane0Connector: true,
          isLastLane0Connector: true,
          lane0Color: '#3b82f6',
        }),
      ]
      const result = computeEdgePaths(lines)

      const lane0Edge = result.find((e) => e.isLane0Connector)
      expect(lane0Edge).toBeDefined()
    })

    it('generates pass-through line for lane 0 pass-through', () => {
      const lines = [
        createIssueLine({
          issueId: 'issue-1',
          lane: 2,
          drawLane0PassThrough: true,
          lane0Color: '#3b82f6',
        }),
      ]
      const result = computeEdgePaths(lines)

      const passThrough = result.find((e) => e.isLane0PassThrough)
      expect(passThrough).toBeDefined()
    })

    it('generates vertical + horizontal for non-last lane 0 connector', () => {
      const lines = [
        createIssueLine({
          issueId: 'issue-1',
          lane: 1,
          drawLane0Connector: true,
          isLastLane0Connector: false,
          lane0Color: '#3b82f6',
        }),
      ]
      const result = computeEdgePaths(lines)

      const lane0Edges = result.filter((e) => e.isLane0Connector)
      // Non-last connector generates 2 edges: vertical + horizontal
      expect(lane0Edges.length).toBe(2)
    })
  })

  describe('edge path structure', () => {
    it('returns properly typed EdgePath objects', () => {
      const lines = [
        createIssueLine({
          issueId: 'child-1',
          lane: 0,
          isSeriesChild: false,
          parentIssues: [{ parentIssue: 'parent-1', sortOrder: 'V' }],
        }),
        createIssueLine({ issueId: 'parent-1', lane: 1 }),
      ]
      const result = computeEdgePaths(lines)

      expect(result.length).toBeGreaterThan(0)
      const edge = result[0]
      expect(edge).toHaveProperty('id')
      expect(edge).toHaveProperty('fromIssueId')
      expect(edge).toHaveProperty('toIssueId')
      expect(edge).toHaveProperty('points')
      expect(edge).toHaveProperty('color')
      expect(edge).toHaveProperty('isSeriesEdge')
      expect(Array.isArray(edge.points)).toBe(true)
    })

    it('assigns correct colors based on child issue type', () => {
      const lines = [
        createIssueLine({
          issueId: 'child-1',
          lane: 0,
          isSeriesChild: false,
          issueType: IssueType.BUG,
          parentIssues: [{ parentIssue: 'parent-1', sortOrder: 'V' }],
        }),
        createIssueLine({ issueId: 'parent-1', lane: 1 }),
      ]
      const result = computeEdgePaths(lines)

      expect(result.length).toBeGreaterThan(0)
      expect(result[0].color).toBe('#ef4444') // BUG color
    })
  })

  describe('edge uniqueness', () => {
    it('generates unique IDs for each edge', () => {
      const lines = [
        createIssueLine({
          issueId: 'child-1',
          lane: 0,
          isSeriesChild: false,
          parentIssues: [{ parentIssue: 'parent-1', sortOrder: 'V' }],
        }),
        createIssueLine({
          issueId: 'child-2',
          lane: 1,
          isSeriesChild: false,
          parentIssues: [{ parentIssue: 'parent-1', sortOrder: 'VV' }],
        }),
        createIssueLine({ issueId: 'parent-1', lane: 2 }),
      ]
      const result = computeEdgePaths(lines)

      const ids = result.map((e) => e.id)
      const uniqueIds = new Set(ids)
      expect(uniqueIds.size).toBe(ids.length)
    })
  })
})
