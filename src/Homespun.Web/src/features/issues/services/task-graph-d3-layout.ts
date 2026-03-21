/**
 * D3-based layout service for task graph SVG rendering.
 *
 * Computes node positions and edge paths for rendering a single large SVG.
 * Preserves the existing layout algorithm from task-graph-layout.ts but adds
 * cumulative Y positioning and edge path generation.
 */

import { LANE_WIDTH, ROW_HEIGHT, NODE_RADIUS, getTypeColor } from '../components/task-graph-svg'
import {
  type TaskGraphRenderLine,
  type TaskGraphIssueRenderLine,
  type TaskGraphPrRenderLine,
  isIssueRenderLine,
  isPrRenderLine,
  isSeparatorRenderLine,
  isLoadMoreRenderLine,
} from './task-graph-layout'
import {
  generateVerticalLine,
  generateSeriesConnectorPath,
  generateParallelConnectorPath,
  generateParallelVerticalLine,
  generateLane0ConnectorPath,
  getLaneCenterX,
} from './task-graph-edge-router'

/**
 * Describes where an inline editor should be inserted in the layout.
 */
export interface InlineEditorPlacement {
  /** The issue ID that the editor is positioned relative to */
  referenceIssueId: string
  /** Whether the editor appears above or below the reference issue */
  position: 'above' | 'below'
}

/**
 * A node with computed D3 position data.
 */
export interface D3TaskGraphNode {
  // Original render line data
  type: 'issue' | 'pr' | 'separator' | 'loadMore' | 'inlineEditor'
  line: TaskGraphRenderLine

  // Computed positions
  x: number // Center X position (lane center)
  y: number // Center Y position (within the SVG)
  contentY: number // Y position for foreignObject (top of row)
  rowHeight: number // Total height of this row (40 for collapsed, more for expanded)

  // For issues only
  issueId?: string
  lane?: number
  parentLane?: number | null
  nodeColor?: string
}

/**
 * An edge with computed SVG path.
 */
export interface D3TaskGraphEdge {
  id: string // Unique edge identifier
  type:
    | 'topLine'
    | 'bottomLine'
    | 'parentConnector'
    | 'seriesConnector'
    | 'lane0Connector'
    | 'lane0Passthrough'
    | 'parallelVertical'
  path: string
  color: string
  sourceId?: string
  targetId?: string
}

/**
 * Result of D3 layout computation.
 */
export interface D3LayoutResult {
  nodes: D3TaskGraphNode[]
  edges: D3TaskGraphEdge[]
  totalHeight: number
  totalWidth: number
}

/**
 * Computes D3 layout from render lines.
 *
 * @param renderLines - Output from computeLayout()
 * @param expandedIds - Set of issue IDs that are expanded
 * @param expandedHeights - Map of issue ID to measured expanded content height
 * @param maxLanes - Maximum number of lanes (for width calculation)
 */
export function computeD3Layout(
  renderLines: TaskGraphRenderLine[],
  expandedIds: Set<string>,
  expandedHeights: Map<string, number>,
  maxLanes: number,
  editorPlacement?: InlineEditorPlacement | null
): D3LayoutResult {
  const nodes: D3TaskGraphNode[] = []
  const edges: D3TaskGraphEdge[] = []
  let cumulativeY = 0

  // Track Y ranges for continuous parallel parent vertical lines
  // Key: parentLane number, Value: { startY (rowBottom of first child), endY (rowTop of last child), color }
  const parallelParentSpans = new Map<number, { startY: number; endY: number; color: string }>()

  const emitEditorNode = () => {
    nodes.push({
      type: 'inlineEditor',
      line: renderLines[0], // Placeholder — not used for rendering
      x: 0,
      y: cumulativeY + ROW_HEIGHT / 2,
      contentY: cumulativeY,
      rowHeight: ROW_HEIGHT,
    })
    cumulativeY += ROW_HEIGHT
  }

  for (const line of renderLines) {
    let rowHeight = ROW_HEIGHT
    let x = 0
    let y = 0

    if (isIssueRenderLine(line)) {
      // Insert editor above this issue if requested
      if (
        editorPlacement?.position === 'above' &&
        editorPlacement.referenceIssueId === line.issueId
      ) {
        emitEditorNode()
      }

      // Check if expanded
      const isExpanded = expandedIds.has(line.issueId)
      const expandedHeight = isExpanded ? (expandedHeights.get(line.issueId) ?? 0) : 0
      rowHeight = ROW_HEIGHT + expandedHeight

      x = getLaneCenterX(line.lane)
      y = cumulativeY + ROW_HEIGHT / 2

      const nodeColor = getTypeColor(line.issueType)

      nodes.push({
        type: 'issue',
        line,
        x,
        y,
        contentY: cumulativeY,
        rowHeight,
        issueId: line.issueId,
        lane: line.lane,
        parentLane: line.parentLane,
        nodeColor,
      })

      // Generate edges for this node
      generateEdgesForNode(edges, line, x, y, cumulativeY, nodeColor)

      // Collect parallel parent vertical spans for post-processing
      if (!line.isSeriesChild && line.parentLane != null && line.parentLane > line.lane) {
        if (line.isFirstChild) {
          // Span starts at the bottom of the first child's collapsed row portion
          parallelParentSpans.set(line.parentLane, {
            startY: cumulativeY + ROW_HEIGHT,
            endY: cumulativeY + ROW_HEIGHT,
            color: nodeColor,
          })
        } else {
          const existing = parallelParentSpans.get(line.parentLane)
          if (existing) {
            // Extend to the top of this child's row
            existing.endY = cumulativeY
          }
        }
      }

      // Insert editor below this issue if requested (after advancing past this row)
      if (
        editorPlacement?.position === 'below' &&
        editorPlacement.referenceIssueId === line.issueId
      ) {
        cumulativeY += rowHeight
        emitEditorNode()
        // Skip the normal cumulativeY advance at end of loop iteration
        continue
      }
    } else if (isPrRenderLine(line)) {
      x = getLaneCenterX(0)
      y = cumulativeY + ROW_HEIGHT / 2

      nodes.push({
        type: 'pr',
        line,
        x,
        y,
        contentY: cumulativeY,
        rowHeight,
      })

      // Generate PR edges
      generateEdgesForPr(edges, line, x, cumulativeY)
    } else if (isSeparatorRenderLine(line)) {
      nodes.push({
        type: 'separator',
        line,
        x: getLaneCenterX(0),
        y: cumulativeY + ROW_HEIGHT / 2,
        contentY: cumulativeY,
        rowHeight,
      })
    } else if (isLoadMoreRenderLine(line)) {
      nodes.push({
        type: 'loadMore',
        line,
        x: getLaneCenterX(0),
        y: cumulativeY + ROW_HEIGHT / 2,
        contentY: cumulativeY,
        rowHeight,
      })
    }

    cumulativeY += rowHeight
  }

  // Post-process: emit continuous vertical lines at parallel parent lanes
  for (const [parentLane, span] of parallelParentSpans) {
    if (span.endY > span.startY) {
      const parentLaneX = getLaneCenterX(parentLane)
      edges.push({
        id: `parallel-vertical-span-lane${parentLane}`,
        type: 'parallelVertical',
        path: generateVerticalLine(parentLaneX, span.startY, span.endY),
        color: span.color,
      })
    }
  }

  const totalWidth = LANE_WIDTH * Math.max(maxLanes, 1) + LANE_WIDTH / 2

  return {
    nodes,
    edges,
    totalHeight: cumulativeY,
    totalWidth,
  }
}

/**
 * Generates all edges for an issue node.
 */
function generateEdgesForNode(
  edges: D3TaskGraphEdge[],
  line: TaskGraphIssueRenderLine,
  cx: number,
  cy: number,
  rowTopY: number,
  nodeColor: string
): void {
  const rowBottomY = rowTopY + ROW_HEIGHT
  const effectiveLane0Color = line.lane0Color ?? '#6b7280'

  // Lane 0 pass-through (full vertical at lane 0)
  if (line.drawLane0PassThrough) {
    const lane0X = getLaneCenterX(0)
    edges.push({
      id: `lane0-passthrough-${line.issueId}`,
      type: 'lane0Passthrough',
      path: generateVerticalLine(lane0X, rowTopY, rowBottomY),
      color: effectiveLane0Color,
    })
  }

  // Lane 0 connector (from PRs)
  if (line.drawLane0Connector && !line.drawLane0PassThrough) {
    const lane0X = getLaneCenterX(0)
    edges.push({
      id: `lane0-connector-${line.issueId}`,
      type: 'lane0Connector',
      path: generateLane0ConnectorPath(
        lane0X,
        cx,
        cy,
        rowTopY,
        rowBottomY,
        line.isLastLane0Connector,
        false
      ),
      color: effectiveLane0Color,
    })
  }

  // Parent connector (parallel mode)
  if (!line.isSeriesChild && line.parentLane != null && line.parentLane > line.lane) {
    const parentLaneX = getLaneCenterX(line.parentLane)

    if (line.isFirstChild) {
      // First child: horizontal + arc + vertical down
      edges.push({
        id: `parent-connector-${line.issueId}`,
        type: 'parentConnector',
        path: generateParallelConnectorPath(cx, cy, parentLaneX, rowBottomY, true),
        color: nodeColor,
      })
    } else {
      // Non-first child: horizontal to parent lane + full vertical
      edges.push({
        id: `parent-connector-h-${line.issueId}`,
        type: 'parentConnector',
        path: generateParallelConnectorPath(cx, cy, parentLaneX, rowBottomY, false),
        color: nodeColor,
      })
      edges.push({
        id: `parent-connector-v-${line.issueId}`,
        type: 'parallelVertical',
        path: generateParallelVerticalLine(parentLaneX, rowTopY, rowBottomY),
        color: nodeColor,
      })
    }
  }

  // Series connector from children (L-shaped incoming)
  if (line.seriesConnectorFromLane != null) {
    const childLaneX = getLaneCenterX(line.seriesConnectorFromLane)
    edges.push({
      id: `series-connector-${line.issueId}`,
      type: 'seriesConnector',
      path: generateSeriesConnectorPath(childLaneX, rowTopY, cx, cy),
      color: nodeColor,
    })
  }

  // Top line (series continuity from above)
  if (line.drawTopLine) {
    edges.push({
      id: `top-line-${line.issueId}`,
      type: 'topLine',
      path: generateVerticalLine(cx, rowTopY, cy - NODE_RADIUS - 2),
      color: nodeColor,
    })
  }

  // Bottom line (series continuity to below)
  if (line.drawBottomLine) {
    edges.push({
      id: `bottom-line-${line.issueId}`,
      type: 'bottomLine',
      path: generateVerticalLine(cx, cy + NODE_RADIUS + 2, rowBottomY),
      color: nodeColor,
    })
  }
}

/**
 * Generates edges for a PR node.
 */
function generateEdgesForPr(
  edges: D3TaskGraphEdge[],
  line: TaskGraphPrRenderLine,
  cx: number,
  rowTopY: number
): void {
  const rowBottomY = rowTopY + ROW_HEIGHT
  const cy = rowTopY + ROW_HEIGHT / 2
  const lane0Color = '#6b7280'

  // Top line
  if (line.drawTopLine) {
    edges.push({
      id: `pr-top-${line.prNumber}`,
      type: 'topLine',
      path: generateVerticalLine(cx, rowTopY, cy - NODE_RADIUS - 2),
      color: lane0Color,
    })
  }

  // Bottom line
  if (line.drawBottomLine) {
    edges.push({
      id: `pr-bottom-${line.prNumber}`,
      type: 'bottomLine',
      path: generateVerticalLine(cx, cy + NODE_RADIUS + 2, rowBottomY),
      color: lane0Color,
    })
  }
}

/**
 * Recalculates layout when expansion state changes.
 * Used for animating expansion/collapse transitions.
 */
export function recalculateLayoutForExpansion(
  currentLayout: D3LayoutResult,
  expandedIds: Set<string>,
  expandedHeights: Map<string, number>
): D3LayoutResult {
  // Simply recompute - the animation layer handles transitions
  const renderLines = currentLayout.nodes.map((n) => n.line)
  const maxLanes = Math.max(
    1,
    ...currentLayout.nodes
      .filter((n): n is D3TaskGraphNode & { lane: number } => n.lane !== undefined)
      .map((n) => n.lane + 1)
  )

  return computeD3Layout(renderLines, expandedIds, expandedHeights, maxLanes)
}

/**
 * Computes the content area width (for foreignObject).
 * This is the full width minus the SVG lanes area.
 */
export function getContentWidth(_totalWidth: number): number {
  // Content starts after the lanes SVG area
  // Return a large value since we use 100% width for foreignObject
  return 9999
}

/**
 * Computes the X offset for content (after the SVG lanes).
 */
export function getContentX(maxLanes: number): number {
  return LANE_WIDTH * Math.max(maxLanes, 1) + LANE_WIDTH / 2
}
