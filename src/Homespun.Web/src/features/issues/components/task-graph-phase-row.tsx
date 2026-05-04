import { memo, forwardRef, type HTMLAttributes } from 'react'
import { cn } from '@/lib/utils'
import type { TaskGraphPhaseRenderLine } from '../services'
import { TaskGraphPhaseSvg } from './task-graph-phase-svg'
import { ROW_HEIGHT } from './task-graph-svg'

interface TaskGraphPhaseRowProps extends HTMLAttributes<HTMLDivElement> {
  line: TaskGraphPhaseRenderLine
  maxLanes: number
  isSelected?: boolean
  isExpanded?: boolean
  onToggleExpand?: () => void
}

export const TaskGraphPhaseRow = memo(
  forwardRef<HTMLDivElement, TaskGraphPhaseRowProps>(function TaskGraphPhaseRow(
    { line, maxLanes, isSelected = false, isExpanded = false, onToggleExpand, className, ...props },
    ref
  ) {
    return (
      <div
        ref={ref}
        role="row"
        tabIndex={0}
        aria-selected={isSelected}
        aria-expanded={isExpanded}
        data-testid="task-graph-phase-row"
        data-phase-id={line.phaseId}
        className={cn(
          'group flex cursor-pointer items-center gap-2 transition-colors',
          'hover:bg-muted/50 focus-visible:ring-ring focus-visible:ring-2 focus-visible:outline-none',
          isSelected && 'bg-muted',
          className
        )}
        style={{ height: ROW_HEIGHT }}
        onDoubleClick={onToggleExpand}
        {...props}
      >
        <TaskGraphPhaseSvg line={line} maxLanes={maxLanes} />
        <span className="text-muted-foreground text-sm">
          {line.phaseName}: {line.done}/{line.total}
        </span>
      </div>
    )
  })
)
