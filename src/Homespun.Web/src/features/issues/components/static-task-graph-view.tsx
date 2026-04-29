/**
 * StaticTaskGraphView - Read-only visualization for issues from pre-fetched data.
 *
 * Unlike TaskGraphView, this component:
 * - Does NOT fetch data from API (receives data as props)
 * - Does NOT connect to SignalR for updates
 * - Does NOT support keyboard navigation or editing
 * - Supports filtering to show only specific issues
 * - Supports visual styling based on change type (created/updated/deleted)
 */

import { memo, useMemo } from 'react'
import { cn } from '@/lib/utils'
import type { TaskGraphResponse } from '@/api'
import { ISSUE_TYPE_LABELS } from '@/lib/issue-constants'
import { IssueRowSkeleton } from './issue-row-skeleton'
import {
  computeLayout,
  isIssueRenderLine,
  getRenderKey,
  type TaskGraphIssueRenderLine,
} from '../services'
import { TaskGraphNodeSvg, ROW_HEIGHT, getTypeColor } from './task-graph-svg'

export type ChangeType = 'created' | 'updated' | 'deleted'

export interface FilteredIssue {
  issueId: string
  changeType: ChangeType
}

export interface StaticTaskGraphViewProps {
  /** Pre-fetched task graph data */
  data: TaskGraphResponse | undefined
  /** Only show these issues with their change type styling */
  filterIssueIds?: FilteredIssue[]
  /** Maximum depth to display */
  depth?: number
  /** Additional CSS classes */
  className?: string
  /** Currently selected issue ID */
  selectedIssueId?: string | null
  /** Callback when an issue is selected */
  onSelectIssue?: (issueId: string) => void
}

/**
 * Read-only task graph view that renders pre-fetched data.
 * Supports filtering and change-type styling for diff views.
 */
export const StaticTaskGraphView = memo(function StaticTaskGraphView({
  data,
  filterIssueIds,
  depth = 10,
  className,
  selectedIssueId,
  onSelectIssue,
}: StaticTaskGraphViewProps) {
  // Compute render lines from task graph data
  const { lines: renderLines } = useMemo(() => {
    if (!data) return { lines: [], edges: [] }
    return computeLayout(data, depth)
  }, [data, depth])

  // Filter to only issue render lines
  const issueRenderLines = useMemo(() => {
    return renderLines.filter(isIssueRenderLine)
  }, [renderLines])

  // Build filter map for quick lookup
  const filterMap = useMemo(() => {
    if (!filterIssueIds) return null
    const map = new Map<string, ChangeType>()
    for (const item of filterIssueIds) {
      map.set(item.issueId.toLowerCase(), item.changeType)
    }
    return map
  }, [filterIssueIds])

  // Show all issues (no filtering) — unchanged issues will be styled differently
  const filteredIssueLines = issueRenderLines

  // Compute max lanes for SVG sizing
  const maxLanes = useMemo(() => {
    return Math.max(1, ...filteredIssueLines.map((line) => line.lane + 1))
  }, [filteredIssueLines])

  // Render loading skeleton
  if (data === undefined) {
    return (
      <div className={cn('space-y-1', className)} data-testid="static-task-graph-loading">
        {Array.from({ length: 5 }).map((_, i) => (
          <IssueRowSkeleton key={i} />
        ))}
      </div>
    )
  }

  // Render empty state
  if (filteredIssueLines.length === 0) {
    return (
      <div
        className={cn('text-muted-foreground py-4 text-center text-sm', className)}
        data-testid="static-task-graph-empty"
      >
        No issues to display
      </div>
    )
  }

  return (
    <div
      className={cn('scrollbar-thin scrollbar-track-transparent scrollbar-thumb-muted', className)}
      data-testid="static-task-graph"
    >
      {filteredIssueLines.map((line) => (
        <StaticIssueRow
          key={getRenderKey(line)}
          line={line}
          maxLanes={maxLanes}
          changeType={filterMap?.get(line.issueId.toLowerCase())}
          isFiltering={filterMap !== null}
          isSelected={selectedIssueId?.toLowerCase() === line.issueId.toLowerCase()}
          onClick={onSelectIssue ? () => onSelectIssue(line.issueId) : undefined}
        />
      ))}
    </div>
  )
})

interface StaticIssueRowProps {
  line: TaskGraphIssueRenderLine
  maxLanes: number
  changeType?: ChangeType
  isFiltering?: boolean
  isSelected?: boolean
  onClick?: () => void
}

/**
 * Read-only issue row component for static display.
 */
const StaticIssueRow = memo(function StaticIssueRow({
  line,
  maxLanes,
  changeType,
  isFiltering,
  isSelected,
  onClick,
}: StaticIssueRowProps) {
  const typeColor = getTypeColor(line.issueType)

  // Determine border color based on change type
  const borderClass = useMemo(() => {
    switch (changeType) {
      case 'created':
        return 'border-l-4 border-green-500 bg-green-50/50 dark:bg-green-950/20'
      case 'updated':
        return 'border-l-4 border-yellow-500 bg-yellow-50/50 dark:bg-yellow-950/20'
      case 'deleted':
        return 'border-l-4 border-red-500 bg-red-50/50 dark:bg-red-950/20'
      default:
        return ''
    }
  }, [changeType])

  const isDeleted = changeType === 'deleted'
  const isUnchanged = isFiltering && changeType === undefined

  return (
    <div
      data-testid="static-task-graph-issue-row"
      data-issue-id={line.issueId}
      className={cn(
        'flex items-center gap-2',
        borderClass,
        isUnchanged && 'opacity-50',
        onClick && 'hover:bg-accent/50 cursor-pointer',
        isSelected && 'ring-primary ring-2 ring-inset'
      )}
      style={{ height: ROW_HEIGHT }}
      onClick={onClick}
      role={onClick ? 'button' : undefined}
      tabIndex={onClick ? 0 : undefined}
      onKeyDown={
        onClick
          ? (e) => {
              if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault()
                onClick()
              }
            }
          : undefined
      }
    >
      {/* SVG graph visualization */}
      <TaskGraphNodeSvg line={line} maxLanes={maxLanes} />

      {/* Issue content */}
      <div className="flex flex-1 items-center gap-2 overflow-hidden pr-2">
        {/* Type badge */}
        <span
          className={cn('shrink-0 rounded px-1.5 py-0.5 text-[10px] font-medium', {
            'opacity-50': isDeleted,
          })}
          style={{
            backgroundColor: `${typeColor}20`,
            color: typeColor,
          }}
        >
          {ISSUE_TYPE_LABELS[line.issueType] ?? 'Task'}
        </span>

        {/* Title */}
        <span
          className={cn('truncate text-sm', {
            'text-muted-foreground line-through': isDeleted,
            'text-muted-foreground italic': isUnchanged,
          })}
        >
          {line.title || 'Untitled'}
        </span>
      </div>
    </div>
  )
})

export { StaticTaskGraphView as default }
