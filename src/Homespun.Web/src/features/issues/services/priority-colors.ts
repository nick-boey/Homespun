/**
 * Maps priority values to colors for the task graph.
 * Matches TimelineSvgRenderer.GetPriorityColor from C#.
 *
 * P0 (critical) = Red
 * P1 = Orange
 * P2 = Yellow
 * P3 = Green
 * P4 (low) = Blue
 * Null or invalid = Grey
 */
export function getPriorityColor(priority: number | null | undefined): string {
  switch (priority) {
    case 0:
      return '#ef4444' // P0: Red (critical)
    case 1:
      return '#f97316' // P1: Orange
    case 2:
      return '#eab308' // P2: Yellow
    case 3:
      return '#22c55e' // P3: Green
    case 4:
      return '#3b82f6' // P4: Blue (low)
    default:
      return '#6b7280' // Default: Grey (no priority or invalid)
  }
}
