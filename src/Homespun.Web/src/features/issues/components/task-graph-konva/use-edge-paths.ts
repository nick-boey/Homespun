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
 * Hook version of computeEdgePaths for use in React components.
 */
export function useEdgePaths(renderLines: TaskGraphRenderLine[]): EdgePath[] {
  return useMemo(() => computeEdgePaths(renderLines), [renderLines])
}
