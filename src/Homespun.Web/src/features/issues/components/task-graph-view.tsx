/**
 * TaskGraphView - Primary visualization for issues on a project.
 *
 * Renders issues in a lane-based hierarchical graph with SVG connectors
 * showing parent-child relationships.
 */

import { memo, useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { cn } from '@/lib/utils'
import { Skeleton } from '@/components/ui/skeleton'
import { useSignalR } from '@/hooks/use-signalr'
import { registerNotificationHubEvents } from '@/lib/signalr/notification-hub'
import {
  computeLayout,
  isIssueRenderLine,
  isPrRenderLine,
  isSeparatorRenderLine,
  isLoadMoreRenderLine,
} from '../services'
import { useTaskGraph, taskGraphQueryKey } from '../hooks'
import {
  TaskGraphIssueRow,
  TaskGraphPrRow,
  TaskGraphSeparatorRow,
  TaskGraphLoadMoreRow,
  TaskGraphExpandedDetails,
} from './task-graph-row'
import { ROW_HEIGHT } from './task-graph-svg'

export interface TaskGraphViewProps {
  projectId: string
  depth?: number
  searchQuery?: string
  selectedIssueId?: string | null
  onSelectIssue?: (issueId: string | null) => void
  onEditIssue?: (issueId: string) => void
  onRunAgent?: (issueId: string) => void
  className?: string
}

/**
 * Main TaskGraphView component.
 *
 * Displays issues in a lane-based graph visualization with:
 * - SVG connectors showing parent-child relationships
 * - Series vs parallel execution mode indicators
 * - Real-time updates via SignalR
 * - Keyboard navigation
 * - Expand/collapse for inline details
 */
export const TaskGraphView = memo(function TaskGraphView({
  projectId,
  depth = 3,
  searchQuery = '',
  selectedIssueId,
  onSelectIssue,
  onEditIssue,
  onRunAgent,
  className,
}: TaskGraphViewProps) {
  const { taskGraph, isLoading, isError, refetch } = useTaskGraph(projectId)
  const queryClient = useQueryClient()

  // Expanded rows state
  const [expandedIds, setExpandedIds] = useState<Set<string>>(new Set())

  // Refs for keyboard navigation
  const containerRef = useRef<HTMLDivElement>(null)
  const rowRefs = useRef<Map<string, HTMLDivElement>>(new Map())

  // Compute render lines from task graph
  const renderLines = useMemo(() => {
    if (!taskGraph) return []
    return computeLayout(taskGraph, depth)
  }, [taskGraph, depth])

  // Compute max lanes for SVG sizing
  const maxLanes = useMemo(() => {
    return Math.max(1, ...renderLines.filter(isIssueRenderLine).map((line) => line.lane + 1))
  }, [renderLines])

  // Issue IDs for keyboard navigation
  const issueIds = useMemo(() => {
    return renderLines.filter(isIssueRenderLine).map((line) => line.issueId)
  }, [renderLines])

  // Search match count
  const searchMatchCount = useMemo(() => {
    if (!searchQuery) return 0
    const lowerQuery = searchQuery.toLowerCase()
    return renderLines.filter(
      (line) => isIssueRenderLine(line) && line.title.toLowerCase().includes(lowerQuery)
    ).length
  }, [renderLines, searchQuery])

  // SignalR connection for real-time updates
  const { connection } = useSignalR({
    hubUrl: '/hubs/notifications',
    autoConnect: true,
  })

  // Register SignalR event handlers
  useEffect(() => {
    if (!connection) return

    const cleanup = registerNotificationHubEvents(connection, {
      onIssuesChanged: (changedProjectId) => {
        if (changedProjectId === projectId) {
          // Invalidate task graph query to trigger refetch
          queryClient.invalidateQueries({
            queryKey: taskGraphQueryKey(projectId),
          })
        }
      },
    })

    return cleanup
  }, [connection, projectId, queryClient])

  // Toggle expanded state for an issue
  const toggleExpanded = useCallback((issueId: string) => {
    setExpandedIds((prev) => {
      const next = new Set(prev)
      if (next.has(issueId)) {
        next.delete(issueId)
      } else {
        next.add(issueId)
      }
      return next
    })
  }, [])

  // Handle row click
  const handleRowClick = useCallback(
    (issueId: string) => {
      onSelectIssue?.(issueId)
    },
    [onSelectIssue]
  )

  // Handle keyboard navigation
  const handleKeyDown = useCallback(
    (event: React.KeyboardEvent<HTMLDivElement>) => {
      if (!selectedIssueId && issueIds.length > 0) {
        // If nothing selected, select first issue on any nav key
        if (['ArrowDown', 'ArrowUp', 'j', 'k'].includes(event.key)) {
          event.preventDefault()
          onSelectIssue?.(issueIds[0])
          return
        }
      }

      if (!selectedIssueId) return

      const currentIndex = issueIds.indexOf(selectedIssueId)
      if (currentIndex === -1) return

      switch (event.key) {
        case 'ArrowDown':
        case 'j': {
          event.preventDefault()
          const nextIndex = Math.min(currentIndex + 1, issueIds.length - 1)
          const nextId = issueIds[nextIndex]
          onSelectIssue?.(nextId)
          rowRefs.current.get(nextId)?.scrollIntoView({ block: 'nearest' })
          break
        }

        case 'ArrowUp':
        case 'k': {
          event.preventDefault()
          const prevIndex = Math.max(currentIndex - 1, 0)
          const prevId = issueIds[prevIndex]
          onSelectIssue?.(prevId)
          rowRefs.current.get(prevId)?.scrollIntoView({ block: 'nearest' })
          break
        }

        case ' ': {
          // Space to toggle expand/collapse
          event.preventDefault()
          toggleExpanded(selectedIssueId)
          break
        }

        case 'Enter':
        case 'e': {
          // Enter/e to edit
          event.preventDefault()
          onEditIssue?.(selectedIssueId)
          break
        }

        case 'Escape': {
          // Escape to close expanded row or deselect
          event.preventDefault()
          if (expandedIds.has(selectedIssueId)) {
            toggleExpanded(selectedIssueId)
          } else {
            onSelectIssue?.(null)
          }
          break
        }
      }
    },
    [selectedIssueId, issueIds, onSelectIssue, onEditIssue, toggleExpanded, expandedIds]
  )

  // Render loading skeleton
  if (isLoading) {
    return (
      <div className={cn('space-y-1', className)}>
        {Array.from({ length: 5 }).map((_, i) => (
          <div key={i} className="flex items-center gap-2" style={{ height: ROW_HEIGHT }}>
            <Skeleton className="h-6 w-12" />
            <Skeleton className="h-4 w-16" />
            <Skeleton className="h-4 flex-1" />
            <Skeleton className="h-4 w-16" />
          </div>
        ))}
      </div>
    )
  }

  // Render error state
  if (isError) {
    return (
      <div className={cn('border-border rounded-lg border p-8 text-center', className)}>
        <p className="text-muted-foreground mb-2">Failed to load issues.</p>
        <button type="button" onClick={() => refetch()} className="text-primary hover:underline">
          Retry
        </button>
      </div>
    )
  }

  // Render empty state
  if (renderLines.length === 0) {
    return (
      <div className={cn('border-border rounded-lg border p-8 text-center', className)}>
        <p className="text-muted-foreground">
          No issues found. Create your first issue to get started.
        </p>
      </div>
    )
  }

  // Render task graph
  return (
    <div
      ref={containerRef}
      role="grid"
      tabIndex={0}
      aria-label="Task graph"
      aria-rowcount={renderLines.length}
      className={cn('focus-visible:outline-none', className)}
      onKeyDown={handleKeyDown}
    >
      {renderLines.map((line, index) => {
        if (isIssueRenderLine(line)) {
          const isSelected = selectedIssueId === line.issueId
          const isExpanded = expandedIds.has(line.issueId)

          return (
            <div key={line.issueId}>
              <TaskGraphIssueRow
                ref={(el) => {
                  if (el) {
                    rowRefs.current.set(line.issueId, el)
                  } else {
                    rowRefs.current.delete(line.issueId)
                  }
                }}
                line={line}
                maxLanes={maxLanes}
                isSelected={isSelected}
                isExpanded={isExpanded}
                searchQuery={searchQuery}
                onToggleExpand={() => toggleExpanded(line.issueId)}
                onEdit={onEditIssue}
                onRunAgent={onRunAgent}
                onClick={() => handleRowClick(line.issueId)}
                aria-rowindex={index + 1}
              />
              {isExpanded && (
                <TaskGraphExpandedDetails
                  line={line}
                  maxLanes={maxLanes}
                  onEdit={onEditIssue}
                  onRunAgent={onRunAgent}
                  onClose={() => toggleExpanded(line.issueId)}
                />
              )}
            </div>
          )
        }

        if (isPrRenderLine(line)) {
          return (
            <TaskGraphPrRow
              key={`pr-${line.prNumber}`}
              line={line}
              maxLanes={maxLanes}
              aria-rowindex={index + 1}
            />
          )
        }

        if (isSeparatorRenderLine(line)) {
          return <TaskGraphSeparatorRow key={`separator-${index}`} maxLanes={maxLanes} />
        }

        if (isLoadMoreRenderLine(line)) {
          return (
            <TaskGraphLoadMoreRow
              key="load-more"
              maxLanes={maxLanes}
              onLoadMore={() => {
                // TODO: Implement load more PRs
                console.log('Load more PRs')
              }}
            />
          )
        }

        return null
      })}

      {/* Search match count indicator (hidden, for accessibility) */}
      {searchQuery && (
        <div className="sr-only" role="status" aria-live="polite">
          {searchMatchCount} issues match "{searchQuery}"
        </div>
      )}
    </div>
  )
})

export { TaskGraphView as default }
