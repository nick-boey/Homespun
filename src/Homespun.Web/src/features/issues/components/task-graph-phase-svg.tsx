import { memo } from 'react'
import type { TaskGraphPhaseRenderLine } from '../services'
import {
  LaneGuideLines,
  NODE_RADIUS,
  ROW_HEIGHT,
  calculateSvgWidth,
  getLaneCenterX,
  getRowCenterY,
} from './task-graph-svg'

const COMPLETE_COLOR = '#22c55e' // Green — mirrors badge complete-state

interface TaskGraphPhaseSvgProps {
  line: TaskGraphPhaseRenderLine
  maxLanes: number
}

export const TaskGraphPhaseSvg = memo(function TaskGraphPhaseSvg({
  line,
  maxLanes,
}: TaskGraphPhaseSvgProps) {
  const width = calculateSvgWidth(maxLanes)
  const cx = getLaneCenterX(line.lane)
  const cy = getRowCenterY()
  const isComplete = line.total > 0 && line.done >= line.total
  const nodeColor = isComplete ? COMPLETE_COLOR : '#6b7280'

  // Diamond: rotated square. Half-size = NODE_RADIUS so bounding box matches circles/squares.
  const r = NODE_RADIUS
  const points = `${cx},${cy - r} ${cx + r},${cy} ${cx},${cy + r} ${cx - r},${cy}`

  return (
    <svg
      width={width}
      height={ROW_HEIGHT}
      className="shrink-0"
      aria-hidden="true"
      data-testid="task-graph-phase-svg"
    >
      <LaneGuideLines maxLanes={maxLanes} />
      <polygon
        points={points}
        fill={nodeColor}
        fillOpacity={0.5}
        stroke={nodeColor}
        strokeWidth={2}
      />
    </svg>
  )
})
