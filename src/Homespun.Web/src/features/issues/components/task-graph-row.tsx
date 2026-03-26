/**
 * Row components for the task graph view.
 */

import { memo, forwardRef, type HTMLAttributes } from 'react'
import { cn } from '@/lib/utils'
import { IssueType, IssueStatus, ExecutionMode } from '@/api'
import {
  ISSUE_STATUS_LABELS,
  ISSUE_STATUS_COMPACT_LABELS,
  ISSUE_TYPE_LABELS,
} from '@/lib/issue-constants'
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
import { PrStatusIndicator } from './pr-status-indicator'
import { ExecutionModeToggle } from './execution-mode-toggle'
import { useLinkedPrStatus } from '../hooks/use-linked-pr-status'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Badge } from '@/components/ui/badge'

/**
 * Extracts the username portion from an email address.
 * Returns the part before the '@' symbol, or the full string if no '@' is present.
 */
function getDisplayName(email: string | null | undefined): string | null {
  if (!email) return null
  const atIndex = email.indexOf('@')
  return atIndex > 0 ? email.substring(0, atIndex) : email
}

interface TaskGraphIssueRowProps extends HTMLAttributes<HTMLDivElement> {
  line: TaskGraphIssueRenderLine
  maxLanes: number
  projectId: string
  isSelected?: boolean
  isExpanded?: boolean
  searchQuery?: string
  onToggleExpand?: () => void
  onEdit?: (issueId: string) => void
  onRunAgent?: (issueId: string) => void
  /** Called when clicking the Open Session button */
  onOpenSession?: (sessionId: string) => void
  showActions?: boolean
  /** Whether this issue is the source of a move operation */
  isMoveSource?: boolean
  /** Whether a move operation is in progress (any issue is being moved) */
  isMoveOperationActive?: boolean
  /** Callback for changing issue type */
  onTypeChange?: (issueId: string, newType: IssueType) => void
  /** Callback for changing issue status */
  onStatusChange?: (issueId: string, newStatus: IssueStatus) => void
  /** Callback for changing execution mode */
  onExecutionModeChange?: (issueId: string, newMode: ExecutionMode) => void
  /** Called when clicking a multi-parent badge to navigate to the first instance */
  onSelectFirstInstance?: (issueId: string) => void
}

/**
 * Row component for rendering an issue in the task graph.
 */
export const TaskGraphIssueRow = memo(
  forwardRef<HTMLDivElement, TaskGraphIssueRowProps>(function TaskGraphIssueRow(
    {
      line,
      maxLanes,
      projectId,
      isSelected = false,
      isExpanded = false,
      searchQuery,
      onToggleExpand,
      onEdit,
      onRunAgent,
      onOpenSession,
      showActions = true,
      isMoveSource = false,
      isMoveOperationActive = false,
      onTypeChange,
      onStatusChange,
      onExecutionModeChange,
      onSelectFirstInstance,
      className,
      ...props
    },
    ref
  ) {
    const typeColor = getTypeColor(line.issueType)
    const hasSearchMatch =
      searchQuery && line.title.toLowerCase().includes(searchQuery.toLowerCase())

    // Fetch PR status if this issue has a linked PR
    const { data: prStatus } = useLinkedPrStatus(
      projectId,
      line.linkedPr ? line.issueId : undefined,
      true // Only fetch when component is visible
    )

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
          {/* Type badge with dropdown */}
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <button
                type="button"
                className="w-14 shrink-0 cursor-pointer rounded px-1.5 py-0.5 text-[10px] font-medium transition-opacity hover:opacity-80"
                style={{
                  backgroundColor: `${typeColor}20`,
                  color: typeColor,
                }}
                onClick={(e) => e.stopPropagation()}
                title="Click to change type"
              >
                {ISSUE_TYPE_LABELS[line.issueType as IssueType] ?? 'Task'}
              </button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="start" onClick={(e) => e.stopPropagation()}>
              {Object.entries(ISSUE_TYPE_LABELS).map(([value, label]) => (
                <DropdownMenuItem
                  key={value}
                  onClick={() => onTypeChange?.(line.issueId, value as IssueType)}
                  className={cn('text-xs', value === line.issueType && 'bg-accent')}
                >
                  <span
                    className="mr-2 inline-block h-2 w-2 rounded-full"
                    style={{ backgroundColor: getTypeColor(value as IssueType) }}
                  />
                  {label}
                </DropdownMenuItem>
              ))}
            </DropdownMenuContent>
          </DropdownMenu>

          {/* Status badge with dropdown */}
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <button
                type="button"
                className={cn(
                  'w-14 shrink-0 cursor-pointer rounded px-1.5 py-0.5 text-[10px] font-medium transition-opacity hover:opacity-80',
                  getStatusColor()
                )}
                onClick={(e) => e.stopPropagation()}
                title="Click to change status"
              >
                {ISSUE_STATUS_COMPACT_LABELS[line.status as IssueStatus] ?? 'Open'}
              </button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="start" onClick={(e) => e.stopPropagation()}>
              {Object.entries(ISSUE_STATUS_LABELS).map(([value, label]) => (
                <DropdownMenuItem
                  key={value}
                  onClick={() => onStatusChange?.(line.issueId, value as IssueStatus)}
                  className={cn('text-xs', value === line.status && 'bg-accent')}
                >
                  {label}
                </DropdownMenuItem>
              ))}
            </DropdownMenuContent>
          </DropdownMenu>

          {/* Execution mode toggle */}
          <ExecutionModeToggle
            executionMode={line.executionMode}
            onToggle={() =>
              onExecutionModeChange?.(
                line.issueId,
                line.executionMode === ExecutionMode.SERIES
                  ? ExecutionMode.PARALLEL
                  : ExecutionMode.SERIES
              )
            }
          />

          {/* Multi-parent badge */}
          {line.multiParentTotal != null && line.multiParentIndex != null && (
            <button
              type="button"
              className="shrink-0 rounded bg-orange-500/20 px-1 py-0.5 text-[10px] font-medium text-orange-700 transition-colors hover:bg-orange-500/30 dark:text-orange-400"
              onClick={(e) => {
                e.stopPropagation()
                if (line.multiParentIndex !== 0) {
                  onSelectFirstInstance?.(line.issueId)
                }
              }}
              title={`Instance ${line.multiParentIndex + 1} of ${line.multiParentTotal}. Click to go to the first instance.`}
              data-testid="multi-parent-badge"
            >
              ({line.multiParentIndex + 1}/{line.multiParentTotal})
            </button>
          )}

          {/* Title - no truncation to allow full horizontal scroll */}
          <span className="text-sm whitespace-nowrap">{line.title || 'Untitled'}</span>

          {/* Assignee badge */}
          {line.assignedTo && (
            <Badge variant="outline" className="shrink-0 text-[10px]">
              {getDisplayName(line.assignedTo)}
            </Badge>
          )}

          {/* Spacer */}
          <div className="flex-1" />

          {/* Linked PR indicator */}
          {line.linkedPr && (
            <div className="flex items-center gap-1.5">
              <a
                href={line.linkedPr.url ?? '#'}
                target="_blank"
                rel="noopener noreferrer"
                className="text-muted-foreground hover:text-foreground shrink-0 text-xs underline"
                onClick={(e) => e.stopPropagation()}
              >
                #{line.linkedPr.number}
              </a>
              {prStatus && (
                <PrStatusIndicator
                  checksPassing={prStatus.checksPassing ?? null}
                  hasConflicts={prStatus.hasConflicts ?? false}
                />
              )}
            </div>
          )}

          {/* Hover actions */}
          {showActions && (
            <IssueRowActions
              issueId={line.issueId}
              isExpanded={isExpanded}
              activeSessionId={line.agentStatus?.isActive ? line.agentStatus.sessionId : null}
              onEdit={onEdit}
              onRunAgent={onRunAgent}
              onOpenSession={onOpenSession}
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
