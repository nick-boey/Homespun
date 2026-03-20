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
import { computeLayout, isIssueRenderLine, type TaskGraphIssueRenderLine } from '../services'
import { ViewMode } from '../types'
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
  /** View mode for the task graph */
  viewMode?: ViewMode
  /** Additional CSS classes */
  className?: string
}

/**
 * Read-only task graph view that renders pre-fetched data.
 * Supports filtering and change-type styling for diff views.
 */
export const StaticTaskGraphView = memo(function StaticTaskGraphView({
  data,
  filterIssueIds,
  depth = 10,
  viewMode = ViewMode.Next,
  className,
}: StaticTaskGraphViewProps) {
  // Compute render lines from task graph data
  const renderLines = useMemo(() => {
    if (!data) return []
    return computeLayout(data, depth, viewMode)
  }, [data, depth, viewMode])

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

  // Apply filtering if filterIssueIds is provided
  const filteredIssueLines = useMemo(() => {
    if (!filterMap) return issueRenderLines

    return issueRenderLines.filter((line) => filterMap.has(line.issueId.toLowerCase()))
  }, [issueRenderLines, filterMap])

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
          key={line.issueId}
          line={line}
          maxLanes={maxLanes}
          changeType={filterMap?.get(line.issueId.toLowerCase())}
        />
      ))}
    </div>
  )
})

interface StaticIssueRowProps {
  line: TaskGraphIssueRenderLine
  maxLanes: number
  changeType?: ChangeType
}

/**
 * Read-only issue row component for static display.
 */
const StaticIssueRow = memo(function StaticIssueRow({
  line,
  maxLanes,
  changeType,
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

  return (
    <div
      data-testid="static-task-graph-issue-row"
      data-issue-id={line.issueId}
      className={cn('flex items-center gap-2', borderClass)}
      style={{ height: ROW_HEIGHT }}
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
          })}
        >
          {line.title || 'Untitled'}
        </span>
      </div>
    </div>
  )
})

export { StaticTaskGraphView as default }
