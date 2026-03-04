/**
 * Row components for the task graph view.
 */

import { memo, forwardRef, type HTMLAttributes } from 'react'
import { cn } from '@/lib/utils'
import {
  TaskGraphNodeSvg,
  TaskGraphPrSvg,
  TaskGraphSeparatorSvg,
  TaskGraphLoadMoreSvg,
  ROW_HEIGHT,
  getTypeColor,
} from './task-graph-svg'
import type { TaskGraphIssueRenderLine, TaskGraphPrRenderLine } from '../services'
import { TaskGraphMarkerType } from '../services'
import { IssueRowActions } from './issue-row-actions'

/** Issue status labels */
const STATUS_LABELS: Record<number, string> = {
  0: 'Open',
  1: 'Complete',
  2: 'Closed',
  3: 'Archived',
  4: 'Progress',
  5: 'Review',
  6: 'Blocked',
}

/** Issue type labels */
const TYPE_LABELS: Record<number, string> = {
  0: 'Task',
  1: 'Feature',
  2: 'Bug',
  3: 'Chore',
  4: 'Epic',
}

interface TaskGraphIssueRowProps extends HTMLAttributes<HTMLDivElement> {
  line: TaskGraphIssueRenderLine
  maxLanes: number
  isSelected?: boolean
  isExpanded?: boolean
  searchQuery?: string
  onToggleExpand?: () => void
  onEdit?: (issueId: string) => void
  onRunAgent?: (issueId: string) => void
  showActions?: boolean
  /** Whether this issue is the source of a move operation */
  isMoveSource?: boolean
  /** Whether a move operation is in progress (any issue is being moved) */
  isMoveOperationActive?: boolean
}

/**
 * Row component for rendering an issue in the task graph.
 */
export const TaskGraphIssueRow = memo(
  forwardRef<HTMLDivElement, TaskGraphIssueRowProps>(function TaskGraphIssueRow(
    {
      line,
      maxLanes,
      isSelected = false,
      isExpanded = false,
      searchQuery,
      onToggleExpand,
      onEdit,
      onRunAgent,
      showActions = true,
      isMoveSource = false,
      isMoveOperationActive = false,
      className,
      ...props
    },
    ref
  ) {
    const typeColor = getTypeColor(line.issueType)
    const hasSearchMatch =
      searchQuery && line.title.toLowerCase().includes(searchQuery.toLowerCase())

    // Status color based on marker
    const getStatusColor = () => {
      switch (line.marker) {
        case TaskGraphMarkerType.Complete:
          return 'bg-green-500/20 text-green-700 dark:text-green-400'
        case TaskGraphMarkerType.Closed:
          return 'bg-gray-500/20 text-gray-700 dark:text-gray-400'
        case TaskGraphMarkerType.Actionable:
          return 'bg-blue-500/20 text-blue-700 dark:text-blue-400'
        default:
          return 'bg-muted text-muted-foreground'
      }
    }

    return (
      <div
        ref={ref}
        role="row"
        tabIndex={0}
        aria-selected={isSelected}
        aria-expanded={isExpanded}
        data-issue-id={line.issueId}
        className={cn(
          'group flex cursor-pointer items-center gap-2 transition-colors',
          'hover:bg-muted/50 focus-visible:ring-ring focus-visible:ring-2 focus-visible:outline-none',
          isSelected && 'bg-muted',
          hasSearchMatch && 'ring-2 ring-yellow-400',
          // Move operation styling
          isMoveSource && 'ring-primary opacity-70 ring-2',
          isMoveOperationActive && !isMoveSource && 'hover:ring-primary hover:ring-2',
          className
        )}
        style={{ height: ROW_HEIGHT }}
        onDoubleClick={onToggleExpand}
        {...props}
      >
        {/* SVG graph visualization */}
        <TaskGraphNodeSvg line={line} maxLanes={maxLanes} />

        {/* Issue content */}
        <div className="flex flex-1 items-center gap-2 pr-2">
          {/* Type badge */}
          <span
            className="shrink-0 rounded px-1.5 py-0.5 text-[10px] font-medium"
            style={{
              backgroundColor: `${typeColor}20`,
              color: typeColor,
            }}
          >
            {TYPE_LABELS[line.issueType] ?? 'Task'}
          </span>

          {/* Status badge */}
          <span
            className={cn(
              'shrink-0 rounded px-1.5 py-0.5 text-[10px] font-medium',
              getStatusColor()
            )}
          >
            {STATUS_LABELS[line.status] ?? 'Open'}
          </span>

          {/* Title - no truncation to allow full horizontal scroll */}
          <span className="text-sm whitespace-nowrap">{line.title || 'Untitled'}</span>

          {/* Spacer */}
          <div className="flex-1" />

          {/* Series indicator */}
          {line.isSeriesChild && (
            <span className="text-muted-foreground shrink-0 text-xs" title="Series execution mode">
              ↓
            </span>
          )}

          {/* Linked PR indicator */}
          {line.linkedPr && (
            <a
              href={line.linkedPr.url ?? '#'}
              target="_blank"
              rel="noopener noreferrer"
              className="text-muted-foreground hover:text-foreground shrink-0 text-xs underline"
              onClick={(e) => e.stopPropagation()}
            >
              #{line.linkedPr.number}
            </a>
          )}

          {/* Agent status */}
          {line.agentStatus?.isActive && (
            <span className="relative flex h-2 w-2 shrink-0">
              <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-green-400 opacity-75" />
              <span className="relative inline-flex h-2 w-2 rounded-full bg-green-500" />
            </span>
          )}

          {/* Hover actions */}
          {showActions && (
            <IssueRowActions
              issueId={line.issueId}
              isExpanded={isExpanded}
              onEdit={onEdit}
              onRunAgent={onRunAgent}
              onExpand={onToggleExpand}
            />
          )}
        </div>
      </div>
    )
  })
)

interface TaskGraphPrRowProps extends HTMLAttributes<HTMLDivElement> {
  line: TaskGraphPrRenderLine
  maxLanes: number
}

/**
 * Row component for rendering a merged PR in the task graph.
 */
export const TaskGraphPrRow = memo(
  forwardRef<HTMLDivElement, TaskGraphPrRowProps>(function TaskGraphPrRow(
    { line, maxLanes, className, ...props },
    ref
  ) {
    return (
      <div
        ref={ref}
        role="row"
        className={cn('flex cursor-default items-center gap-2', 'opacity-70', className)}
        style={{ height: ROW_HEIGHT }}
        {...props}
      >
        {/* SVG graph visualization */}
        <TaskGraphPrSvg
          drawTopLine={line.drawTopLine}
          drawBottomLine={line.drawBottomLine}
          maxLanes={maxLanes}
        />

        {/* PR content */}
        <div className="flex flex-1 items-center gap-2 overflow-hidden pr-2">
          {/* PR number */}
          <span className="text-muted-foreground shrink-0 font-mono text-xs">#{line.prNumber}</span>

          {/* Status badge */}
          <span
            className={cn(
              'shrink-0 rounded px-1.5 py-0.5 text-[10px] font-medium',
              line.isMerged
                ? 'bg-purple-500/20 text-purple-700 dark:text-purple-400'
                : 'bg-red-500/20 text-red-700 dark:text-red-400'
            )}
          >
            {line.isMerged ? 'Merged' : 'Closed'}
          </span>

          {/* Title */}
          <span className="text-sm whitespace-nowrap" title={line.title}>
            {line.title || 'Untitled PR'}
          </span>

          {/* Spacer */}
          <div className="flex-1" />

          {/* Link */}
          {line.url && (
            <a
              href={line.url}
              target="_blank"
              rel="noopener noreferrer"
              className="text-muted-foreground hover:text-foreground shrink-0 text-xs underline"
            >
              View
            </a>
          )}

          {/* Agent status */}
          {line.agentStatus?.isActive && (
            <span className="relative flex h-2 w-2 shrink-0">
              <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-green-400 opacity-75" />
              <span className="relative inline-flex h-2 w-2 rounded-full bg-green-500" />
            </span>
          )}
        </div>
      </div>
    )
  })
)

interface TaskGraphSeparatorRowProps extends HTMLAttributes<HTMLDivElement> {
  maxLanes: number
}

/**
 * Row component for rendering a separator between PRs and issues.
 */
export const TaskGraphSeparatorRow = memo(
  forwardRef<HTMLDivElement, TaskGraphSeparatorRowProps>(function TaskGraphSeparatorRow(
    { maxLanes, className, ...props },
    ref
  ) {
    return (
      <div
        ref={ref}
        role="separator"
        className={cn('flex items-center', className)}
        style={{ height: ROW_HEIGHT / 2 }}
        {...props}
      >
        <TaskGraphSeparatorSvg maxLanes={maxLanes} />
        <div className="bg-border mx-2 h-px flex-1" />
      </div>
    )
  })
)

interface TaskGraphLoadMoreRowProps extends HTMLAttributes<HTMLButtonElement> {
  maxLanes: number
  onLoadMore?: () => void
}

/**
 * Row component for the "load more PRs" button.
 */
export const TaskGraphLoadMoreRow = memo(
  forwardRef<HTMLButtonElement, TaskGraphLoadMoreRowProps>(function TaskGraphLoadMoreRow(
    { maxLanes, onLoadMore, className, ...props },
    ref
  ) {
    return (
      <button
        ref={ref}
        type="button"
        className={cn(
          'flex w-full cursor-pointer items-center gap-2 transition-colors',
          'hover:bg-muted/50 focus-visible:ring-ring focus-visible:ring-2 focus-visible:outline-none',
          className
        )}
        style={{ height: ROW_HEIGHT }}
        onClick={onLoadMore}
        {...props}
      >
        <TaskGraphLoadMoreSvg maxLanes={maxLanes} />
        <span className="text-muted-foreground text-sm">Load more PRs...</span>
      </button>
    )
  })
)

/**
 * Expanded details panel shown below an issue row.
 * Re-exports InlineIssueDetailRow for backwards compatibility.
 */
export { InlineIssueDetailRow as TaskGraphExpandedDetails } from './inline-issue-detail-row'

// Export the new components for direct use
export { InlineIssueDetailRow } from './inline-issue-detail-row'
export { InlinePrDetailRow } from './inline-pr-detail-row'
export { IssueRowActions } from './issue-row-actions'
export type { InlineIssueDetailRowProps } from './inline-issue-detail-row'
export type { InlinePrDetailRowProps, PrCommit, RelatedIssue } from './inline-pr-detail-row'
export type { IssueRowActionsProps } from './issue-row-actions'
