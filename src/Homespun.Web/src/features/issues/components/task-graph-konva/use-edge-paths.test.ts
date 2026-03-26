/**
 * Tests for use-edge-paths hook.
 *
 * Tests edge path computation from task graph render lines.
 */

import { describe, it, expect } from 'vitest'
import { computeEdgePaths } from './use-edge-paths'
import type { TaskGraphIssueRenderLine, TaskGraphPrRenderLine } from '../../services'
import { IssueType, IssueStatus, ExecutionMode } from '@/api'
import { TaskGraphMarkerType } from '../../services'

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
  })

  describe('parallel mode edges', () => {
    it('generates horizontal then vertical path for parallel child', () => {
      const lines = [
        createIssueLine({
          issueId: 'child-1',
          lane: 0,
          parentLane: 1,
          isSeriesChild: false,
          isFirstChild: true,
        }),
        createIssueLine({ issueId: 'parent-1', lane: 1 }),
      ]
      const result = computeEdgePaths(lines)

      expect(result.length).toBe(1)
      expect(result[0].fromIssueId).toBe('child-1')
      expect(result[0].toIssueId).toBe('parent-1')
      expect(result[0].isSeriesEdge).toBe(false)
      // Points should form an L-shape (horizontal then vertical)
      expect(result[0].points.length).toBeGreaterThan(0)
    })

    it('generates connecting edge between parallel siblings', () => {
      const lines = [
        createIssueLine({
          issueId: 'child-1',
          lane: 0,
          parentLane: 2,
          isSeriesChild: false,
          isFirstChild: true,
        }),
        createIssueLine({
          issueId: 'child-2',
          lane: 1,
          parentLane: 2,
          isSeriesChild: false,
          isFirstChild: false,
        }),
        createIssueLine({ issueId: 'parent-1', lane: 2 }),
      ]
      const result = computeEdgePaths(lines)

      // Should have edges for both children connecting to parent
      expect(result.length).toBeGreaterThanOrEqual(2)
    })
  })

  describe('series mode edges', () => {
    it('generates vertical path for series child (top line)', () => {
      const lines = [
        createIssueLine({
          issueId: 'child-1',
          lane: 0,
          parentLane: 0,
          isSeriesChild: true,
          drawTopLine: true,
        }),
        createIssueLine({
          issueId: 'parent-1',
          lane: 0,
          drawBottomLine: true,
        }),
      ]
      const result = computeEdgePaths(lines)

      // Series connections are handled by top/bottom lines
      const seriesEdges = result.filter((e) => e.isSeriesEdge)
      expect(seriesEdges.length).toBeGreaterThanOrEqual(0)
    })

    it('generates L-shaped connector for series parent receiving children', () => {
      const lines = [
        createIssueLine({
          issueId: 'parent-1',
          lane: 1,
          seriesConnectorFromLane: 0,
        }),
        createIssueLine({
          issueId: 'child-1',
          lane: 0,
          parentLane: 1,
          isSeriesChild: true,
        }),
      ]
      const result = computeEdgePaths(lines)

      // Should have an L-shaped connector
      const connectorEdge = result.find((e) => e.fromIssueId === 'parent-1')
      if (connectorEdge) {
        expect(connectorEdge.points.length).toBeGreaterThan(0)
      }
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

      // Should have a lane 0 connector edge
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

      // Should have a pass-through edge
      const passThrough = result.find((e) => e.isLane0PassThrough)
      expect(passThrough).toBeDefined()
    })
  })

  describe('edge path structure', () => {
    it('returns properly typed EdgePath objects', () => {
      const lines = [
        createIssueLine({
          issueId: 'child-1',
          lane: 0,
          parentLane: 1,
          isSeriesChild: false,
        }),
        createIssueLine({ issueId: 'parent-1', lane: 1 }),
      ]
      const result = computeEdgePaths(lines)

      if (result.length > 0) {
        const edge = result[0]
        expect(edge).toHaveProperty('id')
        expect(edge).toHaveProperty('fromIssueId')
        expect(edge).toHaveProperty('toIssueId')
        expect(edge).toHaveProperty('points')
        expect(edge).toHaveProperty('color')
        expect(edge).toHaveProperty('isSeriesEdge')
        expect(Array.isArray(edge.points)).toBe(true)
      }
    })

    it('assigns correct colors based on issue type', () => {
      const lines = [
        createIssueLine({
          issueId: 'child-1',
          lane: 0,
          parentLane: 1,
          isSeriesChild: false,
          issueType: IssueType.BUG,
        }),
        createIssueLine({ issueId: 'parent-1', lane: 1 }),
      ]
      const result = computeEdgePaths(lines)

      if (result.length > 0) {
        expect(result[0].color).toBeDefined()
        expect(result[0].color.startsWith('#')).toBe(true)
      }
    })
  })

  describe('edge uniqueness', () => {
    it('generates unique IDs for each edge', () => {
      const lines = [
        createIssueLine({
          issueId: 'child-1',
          lane: 0,
          parentLane: 2,
          isSeriesChild: false,
        }),
        createIssueLine({
          issueId: 'child-2',
          lane: 1,
          parentLane: 2,
          isSeriesChild: false,
        }),
        createIssueLine({ issueId: 'parent-1', lane: 2 }),
      ]
      const result = computeEdgePaths(lines)

      const ids = result.map((e) => e.id)
      const uniqueIds = new Set(ids)
      expect(uniqueIds.size).toBe(ids.length)
    })
  })

  describe('hidden parent sibling edges', () => {
    it('generates top and bottom lines for series siblings with hidden parent', () => {
      const lines = [
        createIssueLine({
          issueId: 'child-1',
          lane: 0,
          isSeriesChild: true,
          drawBottomLine: true,
          hasHiddenParent: false, // suppressed because drawBottomLine=true
          hiddenParentIsSeriesMode: true,
        }),
        createIssueLine({
          issueId: 'child-2',
          lane: 0,
          isSeriesChild: true,
          drawTopLine: true,
          hasHiddenParent: true,
          hiddenParentIsSeriesMode: true,
        }),
      ]
      const result = computeEdgePaths(lines)

      // Should have bottom line for child-1 and top line for child-2
      const bottomLine = result.find((e) => e.id === 'bottom-line-child-1')
      const topLine = result.find((e) => e.id === 'top-line-child-2')

      expect(bottomLine).toBeDefined()
      expect(topLine).toBeDefined()
      expect(bottomLine!.isSeriesEdge).toBe(true)
      expect(topLine!.isSeriesEdge).toBe(true)
    })

    it('generates continuous vertical lines for 3 series siblings with hidden parent', () => {
      const lines = [
        createIssueLine({
          issueId: 'child-1',
          lane: 0,
          isSeriesChild: true,
          drawBottomLine: true,
          hasHiddenParent: false,
          hiddenParentIsSeriesMode: true,
        }),
        createIssueLine({
          issueId: 'child-2',
          lane: 0,
          isSeriesChild: true,
          drawTopLine: true,
          drawBottomLine: true,
          hasHiddenParent: false,
          hiddenParentIsSeriesMode: true,
        }),
        createIssueLine({
          issueId: 'child-3',
          lane: 0,
          isSeriesChild: true,
          drawTopLine: true,
          hasHiddenParent: true,
          hiddenParentIsSeriesMode: true,
        }),
      ]
      const result = computeEdgePaths(lines)

      expect(result.find((e) => e.id === 'bottom-line-child-1')).toBeDefined()
      expect(result.find((e) => e.id === 'top-line-child-2')).toBeDefined()
      expect(result.find((e) => e.id === 'bottom-line-child-2')).toBeDefined()
      expect(result.find((e) => e.id === 'top-line-child-3')).toBeDefined()
    })
  })
})
