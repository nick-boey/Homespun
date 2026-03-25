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
 * Generates points for a quarter-circle arc at a 90-degree corner.
 *
 * @param cornerX - X coordinate where the two straight lines would meet
 * @param cornerY - Y coordinate where the two straight lines would meet
 * @param direction - Turn direction: 'down-right', 'right-down', or 'up-right'
 * @param radius - Arc radius
 * @param numPoints - Number of segments for the arc (default 8)
 * @returns Flat array of [x, y, x, y, ...] points along the arc
 */
export function generateCornerArc(
  cornerX: number,
  cornerY: number,
  direction: 'down-right' | 'right-down' | 'up-right',
  radius: number,
  numPoints: number = 8
): number[] {
  const points: number[] = []
  let cx: number, cy: number, startAngle: number, endAngle: number

  if (direction === 'down-right') {
    // Going ↓ then →. Center at (cornerX + r, cornerY - r). Sweep π → π/2.
    cx = cornerX + radius
    cy = cornerY - radius
    startAngle = Math.PI
    endAngle = Math.PI / 2
  } else if (direction === 'right-down') {
    // Going → then ↓. Center at (cornerX - r, cornerY + r). Sweep -π/2 → 0.
    cx = cornerX - radius
    cy = cornerY + radius
    startAngle = -Math.PI / 2
    endAngle = 0
  } else {
    // up-right: Going ↑ then →. Center at (cornerX + r, cornerY + r). Sweep π → 3π/2.
    cx = cornerX + radius
    cy = cornerY + radius
    startAngle = Math.PI
    endAngle = (3 * Math.PI) / 2
  }

  for (let i = 0; i <= numPoints; i++) {
    const t = i / numPoints
    const angle = startAngle + (endAngle - startAngle) * t
    points.push(cx + radius * Math.cos(angle))
    points.push(cy + radius * Math.sin(angle))
  }

  return points
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
 * @param rowYPositions - Optional array mapping row index to Y position (for expanded row offsets)
 * @returns Array of edge paths for Konva rendering
 */
export function computeEdgePaths(
  renderLines: TaskGraphRenderLine[],
  rowYPositions?: number[]
): EdgePath[] {
  const edges: EdgePath[] = []

  // Filter to issue render lines only
  const issueLines = renderLines.filter(isIssueRenderLine)

  // Phase 1: Build position map
  const positionMap = new Map<string, { x: number; y: number; lane: number; rowIndex: number }>()
  const cy = getRowCenterY()

  /** Get the Y position for a given row index */
  const getRowY = (index: number): number => {
    return rowYPositions ? rowYPositions[index] : index * ROW_HEIGHT
  }

  /** Get the Y position for the next row (bottom of current row) */
  const getNextRowY = (index: number): number => {
    if (rowYPositions && index + 1 < rowYPositions.length) {
      return rowYPositions[index + 1]
    }
    return getRowY(index) + ROW_HEIGHT
  }

  issueLines.forEach((line, rowIndex) => {
    positionMap.set(line.issueId, {
      x: getLaneCenterX(line.lane),
      y: getRowY(rowIndex) + cy,
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
          // Different lane: L-shape with curved arc at corner
          const verticalY =
            childPos.y > parentPos.y ? childPos.y - NODE_RADIUS - 2 : childPos.y + NODE_RADIUS + 2
          const horizontalSpace = parentPos.x - NODE_RADIUS - 2 - childPos.x
          const verticalSpace = Math.abs(parentPos.y - verticalY)
          const arcRadius = Math.min(ROW_HEIGHT / 2, horizontalSpace, verticalSpace)
          const direction = childPos.y > parentPos.y ? 'up-right' : 'down-right'
          const arcCornerY = parentPos.y
          const arcBeforeY =
            direction === 'up-right' ? arcCornerY + arcRadius : arcCornerY - arcRadius
          const arcPoints = generateCornerArc(childPos.x, arcCornerY, direction, arcRadius)
          edges.push({
            id: `series-${line.issueId}-${parentRef.parentIssue}`,
            fromIssueId: line.issueId,
            toIssueId: parentRef.parentIssue,
            points: [
              childPos.x,
              verticalY,
              childPos.x,
              arcBeforeY,
              ...arcPoints,
              parentPos.x - NODE_RADIUS - 2,
              parentPos.y,
            ],
            color: nodeColor,
            isSeriesEdge: true,
            rowIndex,
          })
        }
      } else {
        // Parallel: exit child right, curved arc at corner, enter parent top
        const horizontalSpace = parentPos.x - (childPos.x + NODE_RADIUS + 2)
        const verticalSpace = parentPos.y - NODE_RADIUS - 2 - childPos.y
        const arcRadius = Math.min(ROW_HEIGHT / 2, horizontalSpace, verticalSpace)
        const arcPoints = generateCornerArc(parentPos.x, childPos.y, 'right-down', arcRadius)
        edges.push({
          id: `parallel-${line.issueId}-${parentRef.parentIssue}`,
          fromIssueId: line.issueId,
          toIssueId: parentRef.parentIssue,
          points: [
            childPos.x + NODE_RADIUS + 2,
            childPos.y,
            parentPos.x - arcRadius,
            childPos.y,
            ...arcPoints,
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
    const rowY_top = getRowY(rowIndex)

    // Lane 0 pass-through line (full vertical at lane 0)
    if (line.drawLane0PassThrough) {
      const lane0X = getLaneCenterX(0)
      const yTop = rowY_top
      const yBottom = getNextRowY(rowIndex)

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
      const rowCenterY = rowY_top + cy
      const effectiveLane0Color = line.lane0Color ?? '#6b7280'

      if (line.isLastLane0Connector) {
        const horizontalSpace = cx - NODE_RADIUS - 2 - lane0X
        const verticalSpace = rowCenterY - rowY_top
        const arcRadius = Math.min(ROW_HEIGHT / 2, horizontalSpace, verticalSpace)
        const arcPoints = generateCornerArc(lane0X, rowCenterY, 'down-right', arcRadius)

        edges.push({
          id: `lane0-connector-${line.issueId}-${rowIndex}`,
          fromIssueId: line.issueId,
          toIssueId: line.issueId,
          points: [
            lane0X,
            rowY_top,
            lane0X,
            rowCenterY - arcRadius,
            ...arcPoints,
            cx - NODE_RADIUS - 2,
            rowCenterY,
          ],
          color: effectiveLane0Color,
          isSeriesEdge: false,
          isLane0Connector: true,
          rowIndex,
        })
      } else {
        const yBottom = getNextRowY(rowIndex)

        edges.push({
          id: `lane0-vertical-${line.issueId}-${rowIndex}`,
          fromIssueId: line.issueId,
          toIssueId: line.issueId,
          points: [lane0X, rowY_top, lane0X, yBottom],
          color: effectiveLane0Color,
          isSeriesEdge: true,
          isLane0Connector: true,
          rowIndex,
        })

        edges.push({
          id: `lane0-horizontal-${line.issueId}-${rowIndex}`,
          fromIssueId: line.issueId,
          toIssueId: line.issueId,
          points: [lane0X, rowCenterY, cx - NODE_RADIUS - 2, rowCenterY],
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
export function useEdgePaths(
  renderLines: TaskGraphRenderLine[],
  rowYPositions?: number[]
): EdgePath[] {
  return useMemo(() => computeEdgePaths(renderLines, rowYPositions), [renderLines, rowYPositions])
}

/**
 * Hook for computing diagonal edge paths for multi-parent issues.
 */
export function useDiagonalEdges(renderLines: TaskGraphRenderLine[]): DiagonalEdgePath[] {
  return useMemo(() => computeDiagonalEdges(renderLines), [renderLines])
}
