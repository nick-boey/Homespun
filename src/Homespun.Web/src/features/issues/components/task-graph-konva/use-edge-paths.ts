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
 * Computes edge paths from render lines.
 *
 * @param renderLines - Array of render lines from computeLayout
 * @returns Array of edge paths for Konva rendering
 */
export function computeEdgePaths(renderLines: TaskGraphRenderLine[]): EdgePath[] {
  const edges: EdgePath[] = []

  // Filter to issue render lines only
  const issueLines = renderLines.filter(isIssueRenderLine)

  // Build issue ID to row index map
  const issueRowMap = new Map<string, number>()
  issueLines.forEach((line, index) => {
    issueRowMap.set(line.issueId, index)
  })

  const cy = getRowCenterY()

  issueLines.forEach((line, rowIndex) => {
    const cx = getLaneCenterX(line.lane)
    const nodeColor = getTypeColor(line.issueType)

    // Lane 0 pass-through line (full vertical at lane 0)
    if (line.drawLane0PassThrough) {
      const lane0X = getLaneCenterX(0)
      const yTop = rowIndex * ROW_HEIGHT
      const yBottom = (rowIndex + 1) * ROW_HEIGHT

      edges.push({
        id: `lane0-passthrough-${line.issueId}`,
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
        // Last connector: vertical from top to junction, arc, horizontal to node
        // For Konva we'll approximate the arc with a series of points
        const junctionY = rowY - NODE_RADIUS
        const arcEndX = lane0X + NODE_RADIUS

        edges.push({
          id: `lane0-connector-${line.issueId}`,
          fromIssueId: line.issueId,
          toIssueId: line.issueId,
          points: [
            lane0X,
            rowIndex * ROW_HEIGHT, // Top
            lane0X,
            junctionY, // Down to junction
            arcEndX,
            rowY, // Arc end (approximated)
            cx - NODE_RADIUS - 2,
            rowY, // Horizontal to node
          ],
          color: effectiveLane0Color,
          isSeriesEdge: false,
          isLane0Connector: true,
          rowIndex,
        })
      } else {
        // Non-last connector: full vertical at lane 0 + horizontal branch to node
        const yTop = rowIndex * ROW_HEIGHT
        const yBottom = (rowIndex + 1) * ROW_HEIGHT

        // Vertical line
        edges.push({
          id: `lane0-vertical-${line.issueId}`,
          fromIssueId: line.issueId,
          toIssueId: line.issueId,
          points: [lane0X, yTop, lane0X, yBottom],
          color: effectiveLane0Color,
          isSeriesEdge: true,
          isLane0Connector: true,
          rowIndex,
        })

        // Horizontal branch
        edges.push({
          id: `lane0-horizontal-${line.issueId}`,
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

    // Parent connector (parallel mode) - horizontal + vertical path
    if (!line.isSeriesChild && line.parentLane != null && line.parentLane > line.lane) {
      const parentLaneCenterX = getLaneCenterX(line.parentLane)
      const rowY = rowIndex * ROW_HEIGHT + cy
      const yTop = rowIndex * ROW_HEIGHT
      const yBottom = (rowIndex + 1) * ROW_HEIGHT

      // Find parent issue ID
      const parentLine = issueLines.find((l) => l.lane === line.parentLane)
      const toIssueId = parentLine?.issueId ?? line.issueId

      if (line.isFirstChild) {
        // First child: horizontal from node to parent lane, arc, vertical down
        const arcStartY = rowY + NODE_RADIUS

        edges.push({
          id: `parallel-${line.issueId}`,
          fromIssueId: line.issueId,
          toIssueId,
          points: [
            cx + NODE_RADIUS + 2,
            rowY, // Right of node
            parentLaneCenterX - NODE_RADIUS,
            rowY, // Horizontal to parent lane
            parentLaneCenterX,
            arcStartY, // Arc (approximated)
            parentLaneCenterX,
            yBottom, // Vertical down
          ],
          color: nodeColor,
          isSeriesEdge: false,
          rowIndex,
        })
      } else {
        // Not first child: horizontal + full vertical at parent lane
        edges.push({
          id: `parallel-h-${line.issueId}`,
          fromIssueId: line.issueId,
          toIssueId,
          points: [cx + NODE_RADIUS + 2, rowY, parentLaneCenterX, rowY],
          color: nodeColor,
          isSeriesEdge: false,
          rowIndex,
        })

        edges.push({
          id: `parallel-v-${line.issueId}`,
          fromIssueId: line.issueId,
          toIssueId,
          points: [parentLaneCenterX, yTop, parentLaneCenterX, yBottom],
          color: nodeColor,
          isSeriesEdge: true,
          rowIndex,
        })
      }
    }

    // Series connector from children (L-shaped)
    if (line.seriesConnectorFromLane != null) {
      const fromLaneX = getLaneCenterX(line.seriesConnectorFromLane)
      const rowY = rowIndex * ROW_HEIGHT + cy
      const yTop = rowIndex * ROW_HEIGHT
      const junctionY = rowY - NODE_RADIUS
      const arcEndX = fromLaneX + NODE_RADIUS

      edges.push({
        id: `series-connector-${line.issueId}`,
        fromIssueId: line.issueId,
        toIssueId: line.issueId,
        points: [
          fromLaneX,
          yTop, // Top
          fromLaneX,
          junctionY, // Down to junction
          arcEndX,
          rowY, // Arc end (approximated)
          cx - NODE_RADIUS - 2,
          rowY, // Horizontal to node
        ],
        color: nodeColor,
        isSeriesEdge: true,
        rowIndex,
      })
    }

    // Top line (series continuity from above)
    if (line.drawTopLine) {
      const rowTopY = rowIndex * ROW_HEIGHT
      const nodeTopY = rowTopY + cy - NODE_RADIUS - 2

      edges.push({
        id: `top-line-${line.issueId}`,
        fromIssueId: line.issueId,
        toIssueId: line.issueId,
        points: [cx, rowTopY, cx, nodeTopY],
        color: nodeColor,
        isSeriesEdge: true,
        rowIndex,
      })
    }

    // Bottom line (series continuity to below)
    if (line.drawBottomLine) {
      const rowTopY = rowIndex * ROW_HEIGHT
      const nodeBottom = rowTopY + cy + NODE_RADIUS + 2
      const yBottom = (rowIndex + 1) * ROW_HEIGHT

      edges.push({
        id: `bottom-line-${line.issueId}`,
        fromIssueId: line.issueId,
        toIssueId: line.issueId,
        points: [cx, nodeBottom, cx, yBottom],
        color: nodeColor,
        isSeriesEdge: true,
        rowIndex,
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
