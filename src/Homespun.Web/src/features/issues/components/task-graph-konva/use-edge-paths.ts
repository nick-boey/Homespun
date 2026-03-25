/**
 * Hook for computing edge paths from task graph render lines.
 *
 * Converts the connector information in TaskGraphIssueRenderLine
 * into complete edge paths for Konva rendering.
 */

import { useMemo } from 'react'
import type { TaskGraphRenderLine } from '../../services'
import { isIssueRenderLine } from '../../services'
import {
  ROW_HEIGHT,
  NODE_RADIUS,
  getTypeColor,
  getLaneCenterX,
  getRowCenterY,
} from '../task-graph-svg'

/**
 * Represents a single edge path in the task graph.
 */
export interface EdgePath {
  /** Unique identifier for the edge */
  id: string
  /** Source issue ID (for parent-child relationships) */
  fromIssueId: string
  /** Target issue ID (for parent-child relationships) */
  toIssueId: string
  /** Points array for Konva Line [x1, y1, x2, y2, ...] */
  points: number[]
  /** Color of the edge */
  color: string
  /** Whether this is a series (vertical) edge vs parallel (horizontal) */
  isSeriesEdge: boolean
  /** Whether this is a lane 0 connector for merged PRs */
  isLane0Connector?: boolean
  /** Whether this is a lane 0 pass-through line */
  isLane0PassThrough?: boolean
  /** Row index for this edge (for vertical positioning) */
  rowIndex: number
}

/**
 * Computes edge paths from render lines using node-position-based rendering.
 *
 * Three-phase algorithm:
 * 1. Build position map from issue render lines
 * 2. Resolve parent-child edges from parentIssues array
 * 3. Preserve lane 0 connector edges for merged PRs
 *
 * @param renderLines - Array of render lines from computeLayout
 * @returns Array of edge paths for Konva rendering
 */
export function computeEdgePaths(renderLines: TaskGraphRenderLine[]): EdgePath[] {
  const edges: EdgePath[] = []

  // Filter to issue render lines only
  const issueLines = renderLines.filter(isIssueRenderLine)

  // Phase 1: Build position map
  const positionMap = new Map<string, { x: number; y: number; lane: number; rowIndex: number }>()
  const cy = getRowCenterY()

  issueLines.forEach((line, rowIndex) => {
    positionMap.set(line.issueId, {
      x: getLaneCenterX(line.lane),
      y: rowIndex * ROW_HEIGHT + cy,
      lane: line.lane,
      rowIndex,
    })
  })

  // Phase 2: Resolve parent-child edges from parentIssues
  issueLines.forEach((line, rowIndex) => {
    if (!line.parentIssues) return

    const childPos = positionMap.get(line.issueId)
    if (!childPos) return

    const nodeColor = getTypeColor(line.issueType)

    for (const parentRef of line.parentIssues) {
      if (!parentRef.parentIssue) continue

      const parentPos = positionMap.get(parentRef.parentIssue)
      if (!parentPos) continue

      if (line.isSeriesChild) {
        // Series: exit child toward parent, enter parent at left (or top for same lane)
        if (childPos.lane === parentPos.lane) {
          // Same lane: straight vertical between nodes
          const minY = Math.min(childPos.y, parentPos.y)
          const maxY = Math.max(childPos.y, parentPos.y)
          edges.push({
            id: `series-${line.issueId}-${parentRef.parentIssue}`,
            fromIssueId: line.issueId,
            toIssueId: parentRef.parentIssue,
            points: [childPos.x, minY + NODE_RADIUS + 2, childPos.x, maxY - NODE_RADIUS - 2],
            color: nodeColor,
            isSeriesEdge: true,
            rowIndex,
          })
        } else {
          // Different lane: L-shape - vertical from child toward parent row, horizontal to parent left
          const verticalY =
            childPos.y > parentPos.y ? childPos.y - NODE_RADIUS - 2 : childPos.y + NODE_RADIUS + 2
          edges.push({
            id: `series-${line.issueId}-${parentRef.parentIssue}`,
            fromIssueId: line.issueId,
            toIssueId: parentRef.parentIssue,
            points: [
              childPos.x,
              verticalY,
              childPos.x,
              parentPos.y,
              parentPos.x - NODE_RADIUS - 2,
              parentPos.y,
            ],
            color: nodeColor,
            isSeriesEdge: true,
            rowIndex,
          })
        }
      } else {
        // Parallel: exit child right, enter parent top
        edges.push({
          id: `parallel-${line.issueId}-${parentRef.parentIssue}`,
          fromIssueId: line.issueId,
          toIssueId: parentRef.parentIssue,
          points: [
            childPos.x + NODE_RADIUS + 2,
            childPos.y,
            parentPos.x,
            childPos.y,
            parentPos.x,
            parentPos.y - NODE_RADIUS - 2,
          ],
          color: nodeColor,
          isSeriesEdge: false,
          rowIndex,
        })
      }
    }
  })

  // Phase 3: Lane 0 connectors (preserved for PR connections)
  issueLines.forEach((line, rowIndex) => {
    const cx = getLaneCenterX(line.lane)

    // Lane 0 pass-through line (full vertical at lane 0)
    if (line.drawLane0PassThrough) {
      const lane0X = getLaneCenterX(0)
      const yTop = rowIndex * ROW_HEIGHT
      const yBottom = (rowIndex + 1) * ROW_HEIGHT

      edges.push({
        id: `lane0-passthrough-${line.issueId}-${rowIndex}`,
        fromIssueId: line.issueId,
        toIssueId: line.issueId,
        points: [lane0X, yTop, lane0X, yBottom],
        color: line.lane0Color ?? '#6b7280',
        isSeriesEdge: true,
        isLane0PassThrough: true,
        rowIndex,
      })
    }

    // Lane 0 connector (horizontal from lane 0 to node)
    if (line.drawLane0Connector) {
      const lane0X = getLaneCenterX(0)
      const rowY = rowIndex * ROW_HEIGHT + cy
      const effectiveLane0Color = line.lane0Color ?? '#6b7280'

      if (line.isLastLane0Connector) {
        const junctionY = rowY - NODE_RADIUS
        const arcEndX = lane0X + NODE_RADIUS

        edges.push({
          id: `lane0-connector-${line.issueId}-${rowIndex}`,
          fromIssueId: line.issueId,
          toIssueId: line.issueId,
          points: [
            lane0X,
            rowIndex * ROW_HEIGHT,
            lane0X,
            junctionY,
            arcEndX,
            rowY,
            cx - NODE_RADIUS - 2,
            rowY,
          ],
          color: effectiveLane0Color,
          isSeriesEdge: false,
          isLane0Connector: true,
          rowIndex,
        })
      } else {
        const yTop = rowIndex * ROW_HEIGHT
        const yBottom = (rowIndex + 1) * ROW_HEIGHT

        edges.push({
          id: `lane0-vertical-${line.issueId}-${rowIndex}`,
          fromIssueId: line.issueId,
          toIssueId: line.issueId,
          points: [lane0X, yTop, lane0X, yBottom],
          color: effectiveLane0Color,
          isSeriesEdge: true,
          isLane0Connector: true,
          rowIndex,
        })

        edges.push({
          id: `lane0-horizontal-${line.issueId}-${rowIndex}`,
          fromIssueId: line.issueId,
          toIssueId: line.issueId,
          points: [lane0X, rowY, cx - NODE_RADIUS - 2, rowY],
          color: effectiveLane0Color,
          isSeriesEdge: false,
          isLane0Connector: true,
          rowIndex,
        })
      }
    }
  })

  return edges
}

/**
 * Represents a diagonal edge indicating a secondary parent relationship.
 */
export interface DiagonalEdgePath {
  /** Unique identifier for the edge */
  id: string
  /** Source issue ID (the child) */
  fromIssueId: string
  /** Target issue ID (the secondary parent) */
  toIssueId: string
  /** Points array [x1, y1, x2, y2] */
  points: number[]
  /** Color of the edge */
  color: string
  /** Always true for diagonal edges */
  isDiagonal: true
}

/** Length of the diagonal indicator line */
const DIAGONAL_LINE_LENGTH = 20

/**
 * Computes diagonal edge paths for issues with multiple parents.
 * Only secondary parents (index > 0) get diagonal edges.
 * The diagonal points from the child node toward the secondary parent's position.
 */
export function computeDiagonalEdges(renderLines: TaskGraphRenderLine[]): DiagonalEdgePath[] {
  const edges: DiagonalEdgePath[] = []
  const issueLines = renderLines.filter(isIssueRenderLine)

  // Build issue ID to (rowIndex, lane) map
  const issuePositionMap = new Map<string, { rowIndex: number; lane: number }>()
  issueLines.forEach((line, index) => {
    issuePositionMap.set(line.issueId, { rowIndex: index, lane: line.lane })
  })

  const cy = getRowCenterY()

  issueLines.forEach((line, rowIndex) => {
    if (!line.parentIssues || line.parentIssues.length <= 1) return

    const childCx = getLaneCenterX(line.lane)
    const childCy = rowIndex * ROW_HEIGHT + cy
    const nodeColor = getTypeColor(line.issueType)

    // Skip the first parent (primary) — only process secondary parents
    for (let i = 1; i < line.parentIssues.length; i++) {
      const parentRef = line.parentIssues[i]
      const parentId = parentRef.parentIssue
      if (!parentId) continue

      const parentPos = issuePositionMap.get(parentId)
      if (!parentPos) continue

      // Compute direction vector toward secondary parent
      const parentCx = getLaneCenterX(parentPos.lane)
      const parentCy = parentPos.rowIndex * ROW_HEIGHT + cy

      const dx = parentCx - childCx
      const dy = parentCy - childCy
      const dist = Math.sqrt(dx * dx + dy * dy)

      if (dist === 0) continue

      // Normalize and scale to line length
      const endX = childCx + (dx / dist) * DIAGONAL_LINE_LENGTH
      const endY = childCy + (dy / dist) * DIAGONAL_LINE_LENGTH

      edges.push({
        id: `diagonal-${line.issueId}-${parentId}`,
        fromIssueId: line.issueId,
        toIssueId: parentId,
        points: [childCx, childCy, endX, endY],
        color: nodeColor,
        isDiagonal: true,
      })
    }
  })

  return edges
}

/**
 * Hook version of computeEdgePaths for use in React components.
 */
export function useEdgePaths(renderLines: TaskGraphRenderLine[]): EdgePath[] {
  return useMemo(() => computeEdgePaths(renderLines), [renderLines])
}

/**
 * Hook for computing diagonal edge paths for multi-parent issues.
 */
export function useDiagonalEdges(renderLines: TaskGraphRenderLine[]): DiagonalEdgePath[] {
  return useMemo(() => computeDiagonalEdges(renderLines), [renderLines])
}
