/**
 * Konva shape components for task graph visualization.
 *
 * Renders circles for issue nodes, lines for edges, and status indicators.
 */

import { memo } from 'react'
import { Circle, Line, Group } from 'react-konva'
import { ClaudeSessionStatus } from '@/api'
import type { TaskGraphIssueRenderLine } from '../../services'
import {
  LANE_WIDTH,
  ROW_HEIGHT,
  NODE_RADIUS,
  LINE_STROKE_WIDTH,
  getTypeColor,
  getLaneCenterX,
  getRowCenterY,
} from '../task-graph-svg'

/**
 * Maps agent status to ring color.
 * Returns null if no ring should be shown.
 */
function getAgentStatusColor(status: string | null): string | null {
  if (!status) return null

  switch (status) {
    case ClaudeSessionStatus.STARTING:
    case ClaudeSessionStatus.RUNNING_HOOKS:
    case ClaudeSessionStatus.RUNNING:
      return '#3b82f6' // Blue
    case ClaudeSessionStatus.WAITING_FOR_INPUT:
    case ClaudeSessionStatus.WAITING_FOR_QUESTION_ANSWER:
    case ClaudeSessionStatus.WAITING_FOR_PLAN_EXECUTION:
      return '#eab308' // Yellow
    case ClaudeSessionStatus.ERROR:
      return '#ef4444' // Red
    case ClaudeSessionStatus.STOPPED:
    default:
      return null // No ring for Stopped or unknown status
  }
}

interface KonvaIssueNodeProps {
  /** The issue render line data */
  line: TaskGraphIssueRenderLine
  /** Row index for vertical positioning */
  rowIndex: number
  /** Optional Y position override (from row Y offset computation) */
  rowY?: number
  /** Click handler */
  onClick?: () => void
  /** Whether this node is selected */
  isSelected?: boolean
  /** Background color for no-description nodes (to occlude edges underneath) */
  backgroundColor?: string
}

/**
 * Konva circle component for rendering an issue node.
 */
export const KonvaIssueNode = memo(function KonvaIssueNode({
  line,
  rowIndex,
  rowY,
  onClick,
  isSelected = false,
  backgroundColor = '#09090b',
}: KonvaIssueNodeProps) {
  const cx = getLaneCenterX(line.lane)
  const cy = (rowY ?? rowIndex * ROW_HEIGHT) + getRowCenterY()
  const nodeColor = getTypeColor(line.issueType)
  const isOutlineOnly = !line.hasDescription

  return (
    <Group onClick={onClick}>
      {/* Agent status ring */}
      {line.agentStatus?.isActive && line.agentStatus.status && (
        <KonvaAgentStatusRing cx={cx} cy={cy} status={line.agentStatus.status} />
      )}

      {/* Selection ring */}
      {isSelected && (
        <Circle
          x={cx}
          y={cy}
          radius={NODE_RADIUS + 6}
          fill="transparent"
          stroke="#3b82f6"
          strokeWidth={2}
          opacity={0.6}
        />
      )}

      {/* Node circle */}
      {isOutlineOnly ? (
        <>
          {/* Background fill to occlude edges passing underneath */}
          <Circle x={cx} y={cy} radius={NODE_RADIUS + 1} fill={backgroundColor} />
          {/* Stroke ring */}
          <Circle
            x={cx}
            y={cy}
            radius={NODE_RADIUS}
            fill={backgroundColor}
            stroke={nodeColor}
            strokeWidth={2}
          />
        </>
      ) : (
        <Circle x={cx} y={cy} radius={NODE_RADIUS} fill={nodeColor} />
      )}

      {/* Hidden parent indicator */}
      {line.hasHiddenParent && (
        <KonvaHiddenParentIndicator
          cx={cx}
          cy={cy}
          nodeColor={nodeColor}
          isSeriesMode={line.hiddenParentIsSeriesMode}
        />
      )}

      {/* Multi-parent diagonal indicator */}
      {line.multiParentIndex != null && line.multiParentTotal != null && (
        <KonvaMultiParentIndicator
          cx={cx}
          cy={cy}
          nodeColor={nodeColor}
          multiParentIndex={line.multiParentIndex}
          multiParentTotal={line.multiParentTotal}
        />
      )}
    </Group>
  )
})

interface KonvaEdgeProps {
  /** Unique ID for the edge */
  id: string
  /** Points array [x1, y1, x2, y2, ...] */
  points: number[]
  /** Edge color */
  color: string
  /** Optional opacity */
  opacity?: number
}

/**
 * Konva line component for rendering an edge.
 */
export const KonvaEdge = memo(function KonvaEdge({
  id,
  points,
  color,
  opacity = 1,
}: KonvaEdgeProps) {
  return (
    <Line
      key={id}
      points={points}
      stroke={color}
      strokeWidth={LINE_STROKE_WIDTH}
      opacity={opacity}
      lineCap="round"
      lineJoin="round"
    />
  )
})

interface KonvaHiddenParentIndicatorProps {
  cx: number
  cy: number
  nodeColor: string
  isSeriesMode: boolean
}

/**
 * Renders three small dots indicating hidden parent.
 * Dots are horizontal for parallel mode, vertical for series mode.
 */
export const KonvaHiddenParentIndicator = memo(function KonvaHiddenParentIndicator({
  cx,
  cy,
  nodeColor,
  isSeriesMode,
}: KonvaHiddenParentIndicatorProps) {
  const dotRadius = 2
  const dotSpacing = 5
  const opacity = 0.4

  if (isSeriesMode) {
    // Series: dots below the node (vertical arrangement)
    const startY = cy + NODE_RADIUS + 6
    return (
      <Group>
        {[0, 1, 2].map((i) => (
          <Circle
            key={i}
            x={cx}
            y={startY + i * dotSpacing}
            radius={dotRadius}
            fill={nodeColor}
            opacity={opacity}
          />
        ))}
      </Group>
    )
  } else {
    // Parallel: dots to the right of the node (horizontal arrangement)
    const startX = cx + NODE_RADIUS + 6
    return (
      <Group>
        {[0, 1, 2].map((i) => (
          <Circle
            key={i}
            x={startX + i * dotSpacing}
            y={cy}
            radius={dotRadius}
            fill={nodeColor}
            opacity={opacity}
          />
        ))}
      </Group>
    )
  }
})

interface KonvaMultiParentIndicatorProps {
  cx: number
  cy: number
  nodeColor: string
  multiParentIndex: number
  multiParentTotal: number
}

/**
 * Renders diagonal lines indicating multi-parent issue instances.
 * First instance: diagonal down-right. Last instance: diagonal up-left. Middle: both.
 * Uses stepped opacity segments for a fade effect.
 */
export const KonvaMultiParentIndicator = memo(function KonvaMultiParentIndicator({
  cx,
  cy,
  nodeColor,
  multiParentIndex,
  multiParentTotal,
}: KonvaMultiParentIndicatorProps) {
  const lineLength = ROW_HEIGHT / 2
  const segmentCount = 3
  const segmentLength = lineLength / segmentCount
  const isFirst = multiParentIndex === 0
  const isLast = multiParentIndex === multiParentTotal - 1

  const showDownRight = !isLast
  const showUpLeft = !isFirst

  return (
    <Group>
      {showDownRight &&
        [0, 1, 2].map((i) => {
          const opacity = 0.5 - (i * 0.5) / segmentCount
          const x1 = cx + NODE_RADIUS + 2 + i * segmentLength * 0.7
          const y1 = cy + NODE_RADIUS + 2 + i * segmentLength * 0.7
          const x2 = cx + NODE_RADIUS + 2 + (i + 1) * segmentLength * 0.7
          const y2 = cy + NODE_RADIUS + 2 + (i + 1) * segmentLength * 0.7
          return (
            <Line
              key={`mp-dr-${i}`}
              points={[x1, y1, x2, y2]}
              stroke={nodeColor}
              strokeWidth={1.5}
              opacity={opacity}
            />
          )
        })}
      {showUpLeft &&
        [0, 1, 2].map((i) => {
          const opacity = 0.5 - (i * 0.5) / segmentCount
          const x1 = cx - NODE_RADIUS - 2 - i * segmentLength * 0.7
          const y1 = cy - NODE_RADIUS - 2 - i * segmentLength * 0.7
          const x2 = cx - NODE_RADIUS - 2 - (i + 1) * segmentLength * 0.7
          const y2 = cy - NODE_RADIUS - 2 - (i + 1) * segmentLength * 0.7
          return (
            <Line
              key={`mp-ul-${i}`}
              points={[x1, y1, x2, y2]}
              stroke={nodeColor}
              strokeWidth={1.5}
              opacity={opacity}
            />
          )
        })}
    </Group>
  )
})

interface KonvaAgentStatusRingProps {
  cx: number
  cy: number
  status: string
}

/**
 * Renders an animated ring around the node indicating agent status.
 */
export const KonvaAgentStatusRing = memo(function KonvaAgentStatusRing({
  cx,
  cy,
  status,
}: KonvaAgentStatusRingProps) {
  const color = getAgentStatusColor(status)

  if (!color) {
    return null
  }

  return (
    <Circle
      x={cx}
      y={cy}
      radius={NODE_RADIUS + 4}
      fill="transparent"
      stroke={color}
      strokeWidth={2}
      opacity={0.6}
    />
  )
})

interface KonvaDiagonalEdgeProps {
  /** Unique ID for the edge */
  id: string
  /** Points array [x1, y1, x2, y2] */
  points: number[]
  /** Edge color */
  color: string
}

/**
 * Konva line component for rendering a diagonal secondary-parent edge.
 * Rendered as a dashed line with fading opacity.
 */
export const KonvaDiagonalEdge = memo(function KonvaDiagonalEdge({
  id,
  points,
  color,
}: KonvaDiagonalEdgeProps) {
  return (
    <Line
      key={id}
      points={points}
      stroke={color}
      strokeWidth={LINE_STROKE_WIDTH}
      dash={[4, 4]}
      opacity={0.3}
      lineCap="round"
    />
  )
})

/**
 * Constants re-exported for use by other components.
 */
export {
  LANE_WIDTH,
  ROW_HEIGHT,
  NODE_RADIUS,
  LINE_STROKE_WIDTH,
  getTypeColor,
  getLaneCenterX,
  getRowCenterY,
}
