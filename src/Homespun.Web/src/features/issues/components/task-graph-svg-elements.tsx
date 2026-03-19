/**
 * Reusable SVG elements for task graph rendering.
 */

import { memo } from 'react'
import { NODE_RADIUS } from './task-graph-svg'

export interface HiddenParentIndicatorProps {
  cx: number
  cy: number
  nodeColor: string
  isSeriesMode: boolean
}

/**
 * Hidden parent indicator (3 small faded dots).
 * Shows when a node has parents that were filtered out by depth limit.
 */
export const HiddenParentIndicator = memo(function HiddenParentIndicator({
  cx,
  cy,
  nodeColor,
  isSeriesMode,
}: HiddenParentIndicatorProps) {
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
})
