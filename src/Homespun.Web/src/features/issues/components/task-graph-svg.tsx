/**
 * SVG rendering for task graph nodes and connectors.
 * Ports TimelineSvgRenderer logic from C#.
 */

import { memo } from 'react'
import { ClaudeSessionStatus, IssueType } from '@/api'
import type { IssueType as IssueTypeEnum } from '@/api'
import type { TaskGraphIssueRenderLine } from '../services'

// Constants matching TimelineSvgRenderer.cs
export const LANE_WIDTH = 24
export const ROW_HEIGHT = 40
export const NODE_RADIUS = 6
export const LINE_STROKE_WIDTH = 2

/** Type colors matching the issue acceptance criteria */
const TYPE_COLORS: Record<string, string> = {
  [IssueType.TASK]: '#3b82f6', // Task: Blue
  [IssueType.BUG]: '#ef4444', // Bug: Red
  [IssueType.CHORE]: '#6b7280', // Chore: Gray
  [IssueType.FEATURE]: '#22c55e', // Feature: Green
  [IssueType.IDEA]: '#8b5cf6', // Idea: Purple
  [IssueType.VERIFY]: '#3b82f6', // Verify: Blue (same as Task)
}

export function getTypeColor(issueType: IssueTypeEnum): string {
  return TYPE_COLORS[issueType] ?? TYPE_COLORS[IssueType.TASK]
}

/**
 * Maps agent status to ring color based on status value.
 * Returns null if no ring should be shown.
 * Handles string status values.
 */
function getAgentStatusColor(status: string | null): string | null {
  if (!status) return null

  // Map status to colors using the enum values
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

export function calculateSvgWidth(maxLanes: number): number {
  return LANE_WIDTH * Math.max(maxLanes, 1) + LANE_WIDTH / 2
}

export function getLaneCenterX(laneIndex: number): number {
  return LANE_WIDTH / 2 + laneIndex * LANE_WIDTH
}

export function getRowCenterY(): number {
  return ROW_HEIGHT / 2
}

interface TaskGraphNodeSvgProps {
  line: TaskGraphIssueRenderLine
  maxLanes: number
}

/**
 * Renders an SVG for a task graph issue node with all its connectors.
 */
export const TaskGraphNodeSvg = memo(function TaskGraphNodeSvg({
  line,
  maxLanes,
}: TaskGraphNodeSvgProps) {
  const width = calculateSvgWidth(maxLanes)
  const cx = getLaneCenterX(line.lane)
  const cy = getRowCenterY()
  const nodeColor = getTypeColor(line.issueType)
  const effectiveLane0Color = line.lane0Color ?? '#6b7280'

  // Determine if node should be outline only (no description)
  const isOutlineOnly = !line.hasDescription

  return (
    <svg width={width} height={ROW_HEIGHT} className="shrink-0" aria-hidden="true">
      {/* Lane 0 merged-PR connector */}
      {line.drawLane0PassThrough && (
        <path
          d={`M ${getLaneCenterX(0)} 0 L ${getLaneCenterX(0)} ${ROW_HEIGHT}`}
          stroke={effectiveLane0Color}
          strokeWidth={LINE_STROKE_WIDTH}
          fill="none"
        />
      )}

      {line.drawLane0Connector && !line.drawLane0PassThrough && (
        <>
          {line.isLastLane0Connector ? (
            // Last connector: vertical from top to junction, arc, horizontal to node
            <path
              d={`M ${getLaneCenterX(0)} 0 L ${getLaneCenterX(0)} ${cy - NODE_RADIUS} A ${NODE_RADIUS} ${NODE_RADIUS} 0 0 0 ${getLaneCenterX(0) + NODE_RADIUS} ${cy} L ${cx - NODE_RADIUS - 2} ${cy}`}
              stroke={effectiveLane0Color}
              strokeWidth={LINE_STROKE_WIDTH}
              fill="none"
            />
          ) : (
            // Non-last connector: full vertical at lane 0 + horizontal branch to node
            <>
              <path
                d={`M ${getLaneCenterX(0)} 0 L ${getLaneCenterX(0)} ${ROW_HEIGHT}`}
                stroke={effectiveLane0Color}
                strokeWidth={LINE_STROKE_WIDTH}
                fill="none"
              />
              <path
                d={`M ${getLaneCenterX(0)} ${cy} L ${cx - NODE_RADIUS - 2} ${cy}`}
                stroke={effectiveLane0Color}
                strokeWidth={LINE_STROKE_WIDTH}
                fill="none"
              />
            </>
          )}
        </>
      )}

      {/* Parent connector (parallel mode) */}
      {!line.isSeriesChild && line.parentLane != null && line.parentLane > line.lane && (
        <>
          {line.isFirstChild ? (
            // Merged horizontal + arc elbow + vertical down
            <path
              d={`M ${cx + NODE_RADIUS + 2} ${cy} L ${getLaneCenterX(line.parentLane) - NODE_RADIUS} ${cy} A ${NODE_RADIUS} ${NODE_RADIUS} 0 0 1 ${getLaneCenterX(line.parentLane)} ${cy + NODE_RADIUS} L ${getLaneCenterX(line.parentLane)} ${ROW_HEIGHT}`}
              stroke={nodeColor}
              strokeWidth={LINE_STROKE_WIDTH}
              fill="none"
            />
          ) : (
            // Horizontal line from circle right edge to parent lane center + full-height vertical
            <>
              <path
                d={`M ${cx + NODE_RADIUS + 2} ${cy} L ${getLaneCenterX(line.parentLane)} ${cy}`}
                stroke={nodeColor}
                strokeWidth={LINE_STROKE_WIDTH}
                fill="none"
              />
              <path
                d={`M ${getLaneCenterX(line.parentLane)} 0 L ${getLaneCenterX(line.parentLane)} ${ROW_HEIGHT}`}
                stroke={nodeColor}
                strokeWidth={LINE_STROKE_WIDTH}
                fill="none"
              />
            </>
          )}
        </>
      )}

      {/* Series connector from children (L-shaped) */}
      {line.seriesConnectorFromLane != null && (
        <path
          d={`M ${getLaneCenterX(line.seriesConnectorFromLane)} 0 L ${getLaneCenterX(line.seriesConnectorFromLane)} ${cy - NODE_RADIUS} A ${NODE_RADIUS} ${NODE_RADIUS} 0 0 0 ${getLaneCenterX(line.seriesConnectorFromLane) + NODE_RADIUS} ${cy} L ${cx - NODE_RADIUS - 2} ${cy}`}
          stroke={nodeColor}
          strokeWidth={LINE_STROKE_WIDTH}
          fill="none"
        />
      )}

      {/* Top line (series continuity) */}
      {line.drawTopLine && (
        <path
          d={`M ${cx} 0 L ${cx} ${cy - NODE_RADIUS - 2}`}
          stroke={nodeColor}
          strokeWidth={LINE_STROKE_WIDTH}
          fill="none"
        />
      )}

      {/* Bottom line (series continuity) */}
      {line.drawBottomLine && (
        <path
          d={`M ${cx} ${cy + NODE_RADIUS + 2} L ${cx} ${ROW_HEIGHT}`}
          stroke={nodeColor}
          strokeWidth={LINE_STROKE_WIDTH}
          fill="none"
        />
      )}

      {/* Agent status ring */}
      {line.agentStatus?.isActive &&
        (() => {
          const color = getAgentStatusColor(line.agentStatus.status)
          return color ? (
            <circle
              cx={cx}
              cy={cy}
              r={NODE_RADIUS + 4}
              fill="none"
              stroke={color}
              strokeWidth={2}
              opacity={0.6}
              className="animate-pulse"
            />
          ) : null
        })()}

      {/* Node circle */}
      {isOutlineOnly ? (
        <circle cx={cx} cy={cy} r={NODE_RADIUS} fill="none" stroke={nodeColor} strokeWidth={2} />
      ) : (
        <circle cx={cx} cy={cy} r={NODE_RADIUS} fill={nodeColor} />
      )}

      {/* Hidden parent indicator (3 small faded dots) */}
      {line.hasHiddenParent && (
        <HiddenParentIndicator
          cx={cx}
          cy={cy}
          nodeColor={nodeColor}
          isSeriesMode={line.hiddenParentIsSeriesMode}
        />
      )}
    </svg>
  )
})

interface HiddenParentIndicatorProps {
  cx: number
  cy: number
  nodeColor: string
  isSeriesMode: boolean
}

function HiddenParentIndicator({ cx, cy, nodeColor, isSeriesMode }: HiddenParentIndicatorProps) {
  const dotRadius = 2
  const dotSpacing = 5
  const opacity = 0.4

  if (isSeriesMode) {
    // Series: dots below the node (vertical arrangement)
    const startY = cy + NODE_RADIUS + 6
    return (
      <>
        {[0, 1, 2].map((i) => (
          <circle
            key={i}
            cx={cx}
            cy={startY + i * dotSpacing}
            r={dotRadius}
            fill={nodeColor}
            opacity={opacity}
          />
        ))}
      </>
    )
  } else {
    // Parallel: dots to the right of the node (horizontal arrangement)
    const startX = cx + NODE_RADIUS + 6
    return (
      <>
        {[0, 1, 2].map((i) => (
          <circle
            key={i}
            cx={startX + i * dotSpacing}
            cy={cy}
            r={dotRadius}
            fill={nodeColor}
            opacity={opacity}
          />
        ))}
      </>
    )
  }
}

interface TaskGraphPrSvgProps {
  drawTopLine: boolean
  drawBottomLine: boolean
  maxLanes: number
  lane0Color?: string
}

/**
 * Renders an SVG for a merged PR row.
 */
export const TaskGraphPrSvg = memo(function TaskGraphPrSvg({
  drawTopLine,
  drawBottomLine,
  maxLanes,
  lane0Color = '#6b7280',
}: TaskGraphPrSvgProps) {
  const width = calculateSvgWidth(maxLanes)
  const cx = getLaneCenterX(0)
  const cy = getRowCenterY()

  return (
    <svg width={width} height={ROW_HEIGHT} className="shrink-0" aria-hidden="true">
      {/* Vertical line segments */}
      {drawTopLine && (
        <path
          d={`M ${cx} 0 L ${cx} ${cy - NODE_RADIUS - 2}`}
          stroke={lane0Color}
          strokeWidth={LINE_STROKE_WIDTH}
          fill="none"
        />
      )}
      {drawBottomLine && (
        <path
          d={`M ${cx} ${cy + NODE_RADIUS + 2} L ${cx} ${ROW_HEIGHT}`}
          stroke={lane0Color}
          strokeWidth={LINE_STROKE_WIDTH}
          fill="none"
        />
      )}

      {/* PR node (circle with checkmark or X) */}
      <circle cx={cx} cy={cy} r={NODE_RADIUS + 2} fill="#51A5C1" stroke="white" strokeWidth={2} />
    </svg>
  )
})

interface TaskGraphSeparatorSvgProps {
  maxLanes: number
  lane0Color?: string
}

/**
 * Renders an SVG for a separator row.
 */
export const TaskGraphSeparatorSvg = memo(function TaskGraphSeparatorSvg({
  maxLanes,
  lane0Color = '#6b7280',
}: TaskGraphSeparatorSvgProps) {
  const width = calculateSvgWidth(maxLanes)
  const x = getLaneCenterX(0)

  return (
    <svg width={width} height={ROW_HEIGHT} className="shrink-0" aria-hidden="true">
      <path
        d={`M ${x} 0 L ${x} ${ROW_HEIGHT}`}
        stroke={lane0Color}
        strokeWidth={LINE_STROKE_WIDTH}
        fill="none"
      />
    </svg>
  )
})

interface TaskGraphLoadMoreSvgProps {
  maxLanes: number
}

/**
 * Renders an SVG for a "load more" button row.
 */
export const TaskGraphLoadMoreSvg = memo(function TaskGraphLoadMoreSvg({
  maxLanes,
}: TaskGraphLoadMoreSvgProps) {
  const width = calculateSvgWidth(maxLanes)
  const cx = getLaneCenterX(0)
  const cy = getRowCenterY()
  const r = NODE_RADIUS + 2

  return (
    <svg width={width} height={ROW_HEIGHT} className="shrink-0" aria-hidden="true">
      {/* Vertical line below the load more button */}
      <path
        d={`M ${cx} ${cy + r + 2} L ${cx} ${ROW_HEIGHT}`}
        stroke="#6b7280"
        strokeWidth={LINE_STROKE_WIDTH}
        fill="none"
      />

      {/* Load more button */}
      <circle cx={cx} cy={cy} r={r} fill="#51A5C1" stroke="white" strokeWidth={2} />
      <text
        x={cx}
        y={cy}
        textAnchor="middle"
        dominantBaseline="central"
        fill="white"
        fontSize={14}
        fontWeight="bold"
      >
        +
      </text>
    </svg>
  )
})
