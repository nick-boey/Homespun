/**
 * Orthogonal edge router for task graph SVG rendering.
 *
 * Generates right-angle SVG paths between nodes with collision avoidance.
 * Supports series (vertical) and parallel (horizontal-then-vertical) connector styles.
 */

import {
  LANE_WIDTH,
  ROW_HEIGHT,
  NODE_RADIUS,
  LINE_STROKE_WIDTH,
} from '../components/task-graph-svg'

/**
 * A positioned node for edge routing calculations.
 */
export interface RoutingNode {
  issueId: string
  x: number // Center X position
  y: number // Center Y position
  lane: number
  parentLane: number | null
  isSeriesChild: boolean
  isFirstChild: boolean
  /** Width of this row's content (for horizontal scrolling) */
  rowHeight: number
}

/**
 * Edge types matching the existing task graph connector styles.
 */
export type EdgeType =
  | 'series' // Vertical connector from series parent
  | 'parallel' // L-shaped horizontal-to-vertical connector
  | 'lane0' // PR-to-issue connector at lane 0
  | 'seriesConnector' // L-shaped incoming from series children to parent

/**
 * A computed edge with its SVG path.
 */
export interface RoutingEdge {
  sourceId: string
  targetId: string
  type: EdgeType
  path: string
  color: string
}

/**
 * Options for generating an orthogonal path.
 */
interface OrthogonalPathOptions {
  startX: number
  startY: number
  endX: number
  endY: number
  cornerRadius?: number
  direction?: 'horizontal-first' | 'vertical-first'
}

/**
 * Generates a simple L-bend orthogonal path between two points.
 * Path goes horizontal first, then vertical (or vice versa based on direction).
 */
export function generateOrthogonalPath(opts: OrthogonalPathOptions): string {
  const {
    startX,
    startY,
    endX,
    endY,
    cornerRadius = NODE_RADIUS,
    direction = 'horizontal-first',
  } = opts
  const r = Math.min(cornerRadius, Math.abs(endX - startX) / 2, Math.abs(endY - startY) / 2)

  // Handle straight lines (no bend needed)
  if (startX === endX || startY === endY) {
    return `M ${startX} ${startY} L ${endX} ${endY}`
  }

  if (direction === 'horizontal-first') {
    // Horizontal then vertical with arc corner
    const isGoingDown = endY > startY
    const isGoingRight = endX > startX

    // Arc sweep direction depends on which way we're turning
    const sweepFlag = (isGoingRight && isGoingDown) || (!isGoingRight && !isGoingDown) ? 1 : 0

    const cornerX = endX - (isGoingRight ? r : -r)
    const cornerY = startY + (isGoingDown ? r : -r)

    return `M ${startX} ${startY} L ${cornerX} ${startY} A ${r} ${r} 0 0 ${sweepFlag} ${endX} ${cornerY} L ${endX} ${endY}`
  } else {
    // Vertical then horizontal with arc corner
    const isGoingDown = endY > startY
    const isGoingRight = endX > startX

    // Arc sweep direction depends on which way we're turning
    const sweepFlag = (isGoingRight && isGoingDown) || (!isGoingRight && !isGoingDown) ? 0 : 1

    const cornerX = startX + (isGoingRight ? r : -r)
    const cornerY = endY - (isGoingDown ? r : -r)

    return `M ${startX} ${startY} L ${startX} ${cornerY} A ${r} ${r} 0 0 ${sweepFlag} ${cornerX} ${endY} L ${endX} ${endY}`
  }
}

/**
 * Generates an S-bend path with two corners.
 * Used when we need to route around intermediate nodes.
 */
export function generateSBendPath(
  startX: number,
  startY: number,
  endX: number,
  endY: number,
  midX: number,
  cornerRadius: number = NODE_RADIUS
): string {
  const r = cornerRadius

  // First bend: horizontal to mid, then vertical
  const isGoingRight1 = midX > startX
  const isGoingDown1 = endY > startY
  const sweep1 = (isGoingRight1 && isGoingDown1) || (!isGoingRight1 && !isGoingDown1) ? 1 : 0

  // Second bend: vertical to target y, then horizontal
  const isGoingRight2 = endX > midX
  const sweep2 = (isGoingRight2 && isGoingDown1) || (!isGoingRight2 && !isGoingDown1) ? 0 : 1

  const midY = (startY + endY) / 2

  return `M ${startX} ${startY}
          L ${midX - (isGoingRight1 ? r : -r)} ${startY}
          A ${r} ${r} 0 0 ${sweep1} ${midX} ${startY + (isGoingDown1 ? r : -r)}
          L ${midX} ${midY - (isGoingDown1 ? r : -r)}
          A ${r} ${r} 0 0 ${sweep2} ${midX + (isGoingRight2 ? r : -r)} ${midY}
          L ${endX - (isGoingRight2 ? r : -r)} ${midY}
          A ${r} ${r} 0 0 ${sweep2} ${endX} ${midY + (isGoingDown1 ? r : -r)}
          L ${endX} ${endY}`
}

/**
 * Generates a vertical line segment (for series continuity).
 */
export function generateVerticalLine(x: number, startY: number, endY: number): string {
  return `M ${x} ${startY} L ${x} ${endY}`
}

/**
 * Generates a horizontal line segment.
 */
export function generateHorizontalLine(y: number, startX: number, endX: number): string {
  return `M ${startX} ${y} L ${endX} ${y}`
}

/**
 * Generates a series connector path (vertical with L-bend into parent).
 * This is the L-shaped path from a series child's lane up and left into the parent node.
 */
export function generateSeriesConnectorPath(
  childLaneX: number,
  rowTopY: number,
  parentX: number,
  parentY: number,
  cornerRadius: number = NODE_RADIUS
): string {
  const r = cornerRadius

  // Path goes: vertical up from child lane, arc, horizontal to parent node edge
  const nodeEdgeX = parentX - NODE_RADIUS - 2

  return `M ${childLaneX} ${rowTopY}
          L ${childLaneX} ${parentY - r}
          A ${r} ${r} 0 0 0 ${childLaneX + r} ${parentY}
          L ${nodeEdgeX} ${parentY}`
}

/**
 * Generates a parallel parent connector path (horizontal with L-bend down).
 * This is the path from a child node to its parallel parent at a higher lane.
 */
export function generateParallelConnectorPath(
  childX: number,
  childY: number,
  parentLaneX: number,
  rowBottomY: number,
  isFirstChild: boolean,
  cornerRadius: number = NODE_RADIUS
): string {
  const r = cornerRadius
  const nodeEdgeX = childX + NODE_RADIUS + 2

  if (isFirstChild) {
    // First child: horizontal from node, arc, vertical down to bottom
    return `M ${nodeEdgeX} ${childY}
            L ${parentLaneX - r} ${childY}
            A ${r} ${r} 0 0 1 ${parentLaneX} ${childY + r}
            L ${parentLaneX} ${rowBottomY}`
  } else {
    // Non-first child: horizontal to parent lane + full vertical line
    return `M ${nodeEdgeX} ${childY}
            L ${parentLaneX} ${childY}`
  }
}

/**
 * Generates a full vertical line at the parent lane (for non-first parallel children).
 */
export function generateParallelVerticalLine(
  parentLaneX: number,
  rowTopY: number,
  rowBottomY: number
): string {
  return `M ${parentLaneX} ${rowTopY} L ${parentLaneX} ${rowBottomY}`
}

/**
 * Generates a lane 0 connector (from PRs to first issue column).
 * Can be either:
 * - A full vertical passthrough (for intermediate rows)
 * - An L-shaped connector (for connected rows)
 * - A last connector (vertical with arc to node)
 */
export function generateLane0ConnectorPath(
  lane0X: number,
  nodeX: number,
  nodeY: number,
  rowTopY: number,
  rowBottomY: number,
  isLast: boolean,
  isPassthrough: boolean,
  cornerRadius: number = NODE_RADIUS
): string {
  const r = cornerRadius
  const nodeEdgeX = nodeX - NODE_RADIUS - 2

  if (isPassthrough) {
    // Full vertical line at lane 0
    return `M ${lane0X} ${rowTopY} L ${lane0X} ${rowBottomY}`
  }

  if (isLast) {
    // Last connector: vertical from top, arc, horizontal to node
    return `M ${lane0X} ${rowTopY}
            L ${lane0X} ${nodeY - r}
            A ${r} ${r} 0 0 0 ${lane0X + r} ${nodeY}
            L ${nodeEdgeX} ${nodeY}`
  }

  // Non-last connector: full vertical + horizontal branch
  return `M ${lane0X} ${rowTopY}
          L ${lane0X} ${rowBottomY}
          M ${lane0X} ${nodeY}
          L ${nodeEdgeX} ${nodeY}`
}

/**
 * Calculates the X coordinate for the center of a lane.
 */
export function getLaneCenterX(laneIndex: number): number {
  return LANE_WIDTH / 2 + laneIndex * LANE_WIDTH
}

/**
 * Calculates the Y coordinate for the center of a row.
 */
export function getRowCenterY(rowTopY: number): number {
  return rowTopY + ROW_HEIGHT / 2
}

/**
 * Finds a safe vertical channel for routing bypass edges.
 * Returns an X coordinate that avoids node centers in intermediate rows.
 */
export function findSafeVerticalChannel(
  sourceX: number,
  targetX: number,
  intermediateNodes: RoutingNode[]
): number {
  // Get occupied X positions (node centers)
  const occupiedX = new Set(intermediateNodes.map((n) => n.x))

  // Try lane boundaries (between lanes) first
  const minLane = Math.min(Math.floor(sourceX / LANE_WIDTH), Math.floor(targetX / LANE_WIDTH))
  const maxLane = Math.max(Math.ceil(sourceX / LANE_WIDTH), Math.ceil(targetX / LANE_WIDTH))

  for (let lane = minLane; lane <= maxLane; lane++) {
    const boundaryX = lane * LANE_WIDTH
    let isSafe = true

    for (const occupied of occupiedX) {
      if (Math.abs(occupied - boundaryX) < NODE_RADIUS + LINE_STROKE_WIDTH) {
        isSafe = false
        break
      }
    }

    if (isSafe) {
      return boundaryX
    }
  }

  // Fallback: use midpoint between source and target
  return (sourceX + targetX) / 2
}

/**
 * Routes an edge that bypasses intermediate rows.
 * Uses an S-bend or staircase pattern to avoid collisions.
 */
export function routeBypassEdge(
  sourceX: number,
  sourceY: number,
  targetX: number,
  targetY: number,
  intermediateNodes: RoutingNode[],
  cornerRadius: number = NODE_RADIUS
): string {
  // Find a safe vertical channel
  const safeX = findSafeVerticalChannel(sourceX, targetX, intermediateNodes)

  // If safe channel is at source or target X, use simple L-bend
  if (safeX === sourceX || safeX === targetX) {
    const direction = safeX === sourceX ? 'vertical-first' : 'horizontal-first'
    return generateOrthogonalPath({
      startX: sourceX,
      startY: sourceY,
      endX: targetX,
      endY: targetY,
      cornerRadius,
      direction,
    })
  }

  // Otherwise, use S-bend through safe channel
  return generateSBendPath(sourceX, sourceY, targetX, targetY, safeX, cornerRadius)
}
