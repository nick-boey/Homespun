/**
 * TaskGraphView - Primary visualization for issues on a project.
 *
 * Renders issues in a lane-based hierarchical graph with SVG connectors
 * showing parent-child relationships.
 */

import {
  memo,
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  forwardRef,
  useImperativeHandle,
} from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { cn } from '@/lib/utils'
import { IssueType, IssueStatus, ExecutionMode } from '@/api'
import { useSignalR } from '@/hooks/use-signalr'
import { registerNotificationHubEvents } from '@/lib/signalr/notification-hub'
import { ErrorFallback } from '@/components/error-boundary'
import { IssueRowSkeleton } from './issue-row-skeleton'
import { IssuesEmptyState } from './issues-empty-state'
import {
  computeLayout,
  isIssueRenderLine,
  isPrRenderLine,
  isSeparatorRenderLine,
  isLoadMoreRenderLine,
  getRenderKey,
  computeInheritedParentInfo,
  applyFilter,
  TaskGraphMarkerType,
  type ParsedFilter,
} from '../services'
import { useTaskGraph, taskGraphQueryKey, useCreateIssue, useUpdateIssue } from '../hooks'
import {
  KeyboardEditMode,
  EditCursorPosition,
  MoveOperationType,
  ViewMode,
  type PendingNewIssue,
  type InlineEditState,
} from '../types'
import {
  TaskGraphIssueRow,
  TaskGraphPrRow,
  TaskGraphSeparatorRow,
  TaskGraphLoadMoreRow,
  TaskGraphExpandedDetails,
} from './task-graph-row'
import { InlineIssueEditor } from './inline-issue-editor'
import { ROW_HEIGHT, LANE_WIDTH, getTypeColor } from './task-graph-svg'

export interface TaskGraphViewProps {
  projectId: string
  depth?: number
  searchQuery?: string
  selectedIssueId?: string | null
  onSelectIssue?: (issueId: string | null) => void
  onEditIssue?: (issueId: string) => void
  onRunAgent?: (issueId: string) => void
  /** Called when clicking the Open Session button to navigate to an existing session */
  onOpenSession?: (sessionId: string) => void
  /** Active move operation type (AsChildOf or AsParentOf) */
  moveOperation?: MoveOperationType | null
  /** Source issue ID for the move operation */
  moveSourceIssueId?: string | null
  /** Called when a move target is selected */
  onMoveComplete?: (targetIssueId: string) => void
  /** Called when move operation is cancelled */
  onMoveCancel?: () => void
  /** Applied filter for client-side filtering */
  appliedFilter?: ParsedFilter | null
  /** Called when filter match count changes */
  onFilterMatchCountChange?: (count: number) => void
  /** View mode for the task graph (next or tree) */
  viewMode?: ViewMode
  className?: string
}

/** Ref handle for TaskGraphView - exposes imperative methods */
export interface TaskGraphViewRef {
  /** Create a new issue above the selected issue, or at the top if none selected */
  createAbove: () => void
  /** Create a new issue below the selected issue, or at the bottom if none selected */
  createBelow: () => void
}

/**
 * Main TaskGraphView component.
 *
 * Displays issues in a lane-based graph visualization with:
 * - SVG connectors showing parent-child relationships
 * - Series vs parallel execution mode indicators
 * - Real-time updates via SignalR
 * - Vim-like keyboard navigation
 * - Inline issue creation and editing
 * - Expand/collapse for inline details
 */
export const TaskGraphView = memo(
  forwardRef<TaskGraphViewRef, TaskGraphViewProps>(function TaskGraphView(
    {
      projectId,
      depth = 3,
      searchQuery = '',
      selectedIssueId,
      onSelectIssue,
      onEditIssue,
      onRunAgent,
      onOpenSession,
      moveOperation,
      moveSourceIssueId,
      onMoveComplete,
      onMoveCancel,
      appliedFilter,
      onFilterMatchCountChange,
      viewMode = ViewMode.Tree,
      className,
    },
    ref
  ) {
    const { taskGraph, isLoading, isError, refetch } = useTaskGraph(projectId)
    const queryClient = useQueryClient()

    // Expanded rows state
    const [expandedIds, setExpandedIds] = useState<Set<string>>(new Set())

    // Edit mode state
    const [editMode, setEditMode] = useState<KeyboardEditMode>(KeyboardEditMode.Viewing)
    const [pendingNewIssue, setPendingNewIssue] = useState<PendingNewIssue | null>(null)
    const [pendingEdit, setPendingEdit] = useState<InlineEditState | null>(null)

    // Refs for keyboard navigation
    const containerRef = useRef<HTMLDivElement>(null)
    const rowRefs = useRef<Map<string, HTMLDivElement>>(new Map())

    // Create issue mutation
    const { createIssue, isCreating } = useCreateIssue({
      projectId,
      onSuccess: () => {
        // Reset edit mode after successful creation
        setEditMode(KeyboardEditMode.Viewing)
        setPendingNewIssue(null)
      },
    })

    // Update issue mutation
    const { mutateAsync: updateIssue } = useUpdateIssue({
      onSuccess: () => {
        // Reset edit mode after successful update
        setEditMode(KeyboardEditMode.Viewing)
        setPendingEdit(null)
      },
    })

    // Compute render lines from task graph
    const unfilteredRenderLines = useMemo(() => {
      if (!taskGraph) return []
      return computeLayout(taskGraph, depth, viewMode)
    }, [taskGraph, depth, viewMode])

    // Build a lookup of issue IDs to their full issue data for filtering
    const issueDataMap = useMemo(() => {
      if (!taskGraph?.nodes) return new Map()
      const map = new Map()
      for (const node of taskGraph.nodes) {
        if (node.issue) {
          map.set(node.issue.id, node.issue)
        }
      }
      return map
    }, [taskGraph])

    // Apply filter to render lines (keeping separators and PRs for context)
    const renderLines = useMemo(() => {
      if (!appliedFilter) return unfilteredRenderLines

      return unfilteredRenderLines.filter((line) => {
        // Always keep non-issue lines (PRs, separators, load more)
        if (!isIssueRenderLine(line)) return true

        // Handle isNext filter - check if issue is actionable via marker
        if (appliedFilter.isNext) {
          if (line.marker !== TaskGraphMarkerType.Actionable) {
            return false
          }
        }

        // Get the full issue data for filtering
        const issueData = issueDataMap.get(line.issueId)
        if (!issueData) return true // Keep if we can't find the issue

        return applyFilter(issueData, appliedFilter)
      })
    }, [unfilteredRenderLines, appliedFilter, issueDataMap])

    // Report filter match count
    useEffect(() => {
      if (!appliedFilter || !onFilterMatchCountChange) return

      const matchCount = renderLines.filter(isIssueRenderLine).length
      onFilterMatchCountChange(matchCount)
    }, [renderLines, appliedFilter, onFilterMatchCountChange])

    // Compute max lanes for SVG sizing
    const maxLanes = useMemo(() => {
      return Math.max(1, ...renderLines.filter(isIssueRenderLine).map((line) => line.lane + 1))
    }, [renderLines])

    // Issue render lines only
    const issueRenderLines = useMemo(() => {
      return renderLines.filter(isIssueRenderLine)
    }, [renderLines])

    // Search match count
    const searchMatchCount = useMemo(() => {
      if (!searchQuery) return 0
      const lowerQuery = searchQuery.toLowerCase()
      return renderLines.filter(
        (line) => isIssueRenderLine(line) && line.title.toLowerCase().includes(lowerQuery)
      ).length
    }, [renderLines, searchQuery])

    // Get selected issue index (find first render line matching the selected issue ID)
    const selectedIndex = useMemo(() => {
      if (!selectedIssueId) return -1
      return issueRenderLines.findIndex((line) => line.issueId === selectedIssueId)
    }, [selectedIssueId, issueRenderLines])

    // Get selected render line
    const selectedRenderLine = useMemo(() => {
      if (selectedIndex < 0) return null
      return issueRenderLines[selectedIndex] ?? null
    }, [selectedIndex, issueRenderLines])

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

    // Handle navigating to first instance of a multi-parent issue
    const handleSelectFirstInstance = useCallback(
      (issueId: string) => {
        onSelectIssue?.(issueId)
        const firstLine = issueRenderLines.find((l) => l.issueId === issueId)
        if (firstLine) {
          const key = getRenderKey(firstLine)
          rowRefs.current.get(key)?.scrollIntoView({ block: 'nearest' })
        }
      },
      [onSelectIssue, issueRenderLines]
    )

    // Handle row click
    const handleRowClick = useCallback(
      (issueId: string) => {
        // If in creating mode, clicking elsewhere cancels
        if (editMode === KeyboardEditMode.CreatingNew) {
          setEditMode(KeyboardEditMode.Viewing)
          setPendingNewIssue(null)
        }

        // If a move operation is active and clicking a different issue, complete the move
        if (moveOperation && moveSourceIssueId && issueId !== moveSourceIssueId) {
          onMoveComplete?.(issueId)
          return
        }

        onSelectIssue?.(issueId)
      },
      [onSelectIssue, editMode, moveOperation, moveSourceIssueId, onMoveComplete]
    )

    // ============================================================================
    // Inline Issue Creation Handlers
    // ============================================================================

    const handleCreateBelow = useCallback(() => {
      if (selectedIndex < 0) return
      const referenceIssue = issueRenderLines[selectedIndex]
      if (!referenceIssue) return

      // Compute inherited parent info for sibling creation
      const inheritedParent = computeInheritedParentInfo(
        taskGraph,
        referenceIssue.issueId,
        false // isAbove = false for creating below
      )

      setPendingNewIssue({
        insertAtIndex: selectedIndex + 1,
        title: '',
        isAbove: false,
        referenceIssueId: referenceIssue.issueId,
        inheritedParentIssueId: inheritedParent?.parentIssueId ?? undefined,
        siblingIssueId: inheritedParent?.siblingIssueId ?? undefined,
        insertBefore: inheritedParent?.insertBefore ?? false,
      })
      setEditMode(KeyboardEditMode.CreatingNew)
    }, [selectedIndex, issueRenderLines, taskGraph])

    const handleCreateAbove = useCallback(() => {
      if (selectedIndex < 0) return
      const referenceIssue = issueRenderLines[selectedIndex]
      if (!referenceIssue) return

      // Compute inherited parent info for sibling creation
      const inheritedParent = computeInheritedParentInfo(
        taskGraph,
        referenceIssue.issueId,
        true // isAbove = true for creating above
      )

      setPendingNewIssue({
        insertAtIndex: selectedIndex,
        title: '',
        isAbove: true,
        referenceIssueId: referenceIssue.issueId,
        inheritedParentIssueId: inheritedParent?.parentIssueId ?? undefined,
        siblingIssueId: inheritedParent?.siblingIssueId ?? undefined,
        insertBefore: inheritedParent?.insertBefore ?? false,
      })
      setEditMode(KeyboardEditMode.CreatingNew)
    }, [selectedIndex, issueRenderLines, taskGraph])

    // Handler for creating at top of list (no selection)
    const handleCreateAtTop = useCallback(() => {
      const firstIssue = issueRenderLines[0]

      // Compute inherited parent info if there's a reference issue
      const inheritedParent = firstIssue
        ? computeInheritedParentInfo(taskGraph, firstIssue.issueId, true)
        : null

      setPendingNewIssue({
        insertAtIndex: 0,
        title: '',
        isAbove: true,
        referenceIssueId: firstIssue?.issueId,
        inheritedParentIssueId: inheritedParent?.parentIssueId ?? undefined,
        siblingIssueId: inheritedParent?.siblingIssueId ?? undefined,
        insertBefore: inheritedParent?.insertBefore ?? false,
      })
      setEditMode(KeyboardEditMode.CreatingNew)
    }, [issueRenderLines, taskGraph])

    // Handler for creating at bottom of list (no selection)
    const handleCreateAtBottom = useCallback(() => {
      const lastIssue = issueRenderLines[issueRenderLines.length - 1]

      // Compute inherited parent info if there's a reference issue
      const inheritedParent = lastIssue
        ? computeInheritedParentInfo(taskGraph, lastIssue.issueId, false)
        : null

      setPendingNewIssue({
        insertAtIndex: issueRenderLines.length,
        title: '',
        isAbove: false,
        referenceIssueId: lastIssue?.issueId,
        inheritedParentIssueId: inheritedParent?.parentIssueId ?? undefined,
        siblingIssueId: inheritedParent?.siblingIssueId ?? undefined,
        insertBefore: inheritedParent?.insertBefore ?? false,
      })
      setEditMode(KeyboardEditMode.CreatingNew)
    }, [issueRenderLines, taskGraph])

    // Expose imperative methods via ref
    useImperativeHandle(
      ref,
      () => ({
        createAbove: () => {
          if (selectedIndex >= 0) {
            handleCreateAbove()
          } else {
            handleCreateAtTop()
          }
        },
        createBelow: () => {
          if (selectedIndex >= 0) {
            handleCreateBelow()
          } else {
            handleCreateAtBottom()
          }
        },
      }),
      [selectedIndex, handleCreateAbove, handleCreateBelow, handleCreateAtTop, handleCreateAtBottom]
    )

    const handleCancelEdit = useCallback(() => {
      setEditMode(KeyboardEditMode.Viewing)
      setPendingNewIssue(null)
      setPendingEdit(null)
      // Return focus to container
      containerRef.current?.focus()
    }, [])

    const handleTitleChange = useCallback((title: string) => {
      setPendingNewIssue((prev) => (prev ? { ...prev, title } : null))
      setPendingEdit((prev) => (prev ? { ...prev, title } : null))
    }, [])

    const handleIndent = useCallback(() => {
      if (!pendingNewIssue?.referenceIssueId) return
      // Only allow indent if not already indented/unindented
      if (pendingNewIssue.pendingChildId || pendingNewIssue.pendingParentId) return

      // Tab = make this new issue a PARENT of the reference (reference becomes child)
      setPendingNewIssue((prev) =>
        prev
          ? {
              ...prev,
              pendingChildId: prev.referenceIssueId,
              pendingParentId: undefined,
            }
          : null
      )
    }, [pendingNewIssue])

    const handleUnindent = useCallback(() => {
      if (!pendingNewIssue?.referenceIssueId) return
      // Only allow unindent if not already indented/unindented
      if (pendingNewIssue.pendingChildId || pendingNewIssue.pendingParentId) return

      // Shift+Tab = make this new issue a CHILD of the reference (reference becomes parent)
      setPendingNewIssue((prev) =>
        prev
          ? {
              ...prev,
              pendingParentId: prev.referenceIssueId,
              pendingChildId: undefined,
            }
          : null
      )
    }, [pendingNewIssue])

    const handleSave = useCallback(async () => {
      if (!pendingNewIssue?.title.trim()) {
        handleCancelEdit()
        return
      }

      try {
        // Determine parent ID and sort order:
        // - If Tab/Shift+Tab was pressed, use pendingParentId/pendingChildId (explicit hierarchy)
        // - Otherwise, use inherited parent for sibling creation
        const hasExplicitHierarchy =
          pendingNewIssue.pendingParentId || pendingNewIssue.pendingChildId
        const parentIssueId = hasExplicitHierarchy
          ? pendingNewIssue.pendingParentId
          : pendingNewIssue.inheritedParentIssueId
        const siblingIssueId = hasExplicitHierarchy ? undefined : pendingNewIssue.siblingIssueId
        const insertBefore = hasExplicitHierarchy ? undefined : pendingNewIssue.insertBefore

        await createIssue({
          title: pendingNewIssue.title.trim(),
          parentIssueId,
          childIssueId: pendingNewIssue.pendingChildId,
          siblingIssueId,
          insertBefore,
        })
        // Return focus to container after save
        containerRef.current?.focus()
      } catch {
        // Keep edit mode on error so user can retry
      }
    }, [pendingNewIssue, createIssue, handleCancelEdit])

    const handleSaveAndEdit = useCallback(async () => {
      if (!pendingNewIssue?.title.trim()) {
        handleCancelEdit()
        return
      }

      try {
        // Determine parent ID and sort order:
        // - If Tab/Shift+Tab was pressed, use pendingParentId/pendingChildId (explicit hierarchy)
        // - Otherwise, use inherited parent for sibling creation
        const hasExplicitHierarchy =
          pendingNewIssue.pendingParentId || pendingNewIssue.pendingChildId
        const parentIssueId = hasExplicitHierarchy
          ? pendingNewIssue.pendingParentId
          : pendingNewIssue.inheritedParentIssueId
        const siblingIssueId = hasExplicitHierarchy ? undefined : pendingNewIssue.siblingIssueId
        const insertBefore = hasExplicitHierarchy ? undefined : pendingNewIssue.insertBefore

        const issue = await createIssue({
          title: pendingNewIssue.title.trim(),
          parentIssueId,
          childIssueId: pendingNewIssue.pendingChildId,
          siblingIssueId,
          insertBefore,
        })
        // Navigate to edit page for description
        if (issue?.id) {
          onEditIssue?.(issue.id)
        }
      } catch {
        // Keep edit mode on error so user can retry
      }
    }, [pendingNewIssue, createIssue, handleCancelEdit, onEditIssue])

    // ============================================================================
    // Inline Editing Handlers (for existing issues)
    // ============================================================================

    const handleStartEditAtStart = useCallback(() => {
      if (!selectedRenderLine) return
      setPendingEdit({
        issueId: selectedRenderLine.issueId,
        title: selectedRenderLine.title,
        originalTitle: selectedRenderLine.title,
        cursorPosition: EditCursorPosition.Start,
      })
      setEditMode(KeyboardEditMode.EditingExisting)
    }, [selectedRenderLine])

    const handleStartEditAtEnd = useCallback(() => {
      if (!selectedRenderLine) return
      setPendingEdit({
        issueId: selectedRenderLine.issueId,
        title: selectedRenderLine.title,
        originalTitle: selectedRenderLine.title,
        cursorPosition: EditCursorPosition.End,
      })
      setEditMode(KeyboardEditMode.EditingExisting)
    }, [selectedRenderLine])

    const handleStartReplace = useCallback(() => {
      if (!selectedRenderLine) return
      setPendingEdit({
        issueId: selectedRenderLine.issueId,
        title: '',
        originalTitle: selectedRenderLine.title,
        cursorPosition: EditCursorPosition.Replace,
      })
      setEditMode(KeyboardEditMode.EditingExisting)
    }, [selectedRenderLine])

    // ============================================================================
    // Keyboard Navigation
    // ============================================================================

    const handleKeyDown = useCallback(
      (event: React.KeyboardEvent<HTMLDivElement>) => {
        // If a move operation is active, Escape cancels it
        if (moveOperation && event.key === 'Escape') {
          event.preventDefault()
          onMoveCancel?.()
          return
        }

        // If in editing mode, don't handle navigation keys
        if (editMode !== KeyboardEditMode.Viewing) {
          // Only handle Escape to cancel
          if (event.key === 'Escape') {
            event.preventDefault()
            handleCancelEdit()
          }
          return
        }

        // If nothing selected, select first issue on any nav key
        if (!selectedIssueId && issueRenderLines.length > 0) {
          if (['ArrowDown', 'ArrowUp', 'j', 'k'].includes(event.key)) {
            event.preventDefault()
            onSelectIssue?.(issueRenderLines[0].issueId)
            return
          }
        }

        if (!selectedIssueId) return

        const currentIndex = issueRenderLines.findIndex((line) => line.issueId === selectedIssueId)
        if (currentIndex === -1) return

        switch (event.key) {
          // Navigation
          case 'ArrowDown':
          case 'j': {
            event.preventDefault()
            const nextIndex = Math.min(currentIndex + 1, issueRenderLines.length - 1)
            const nextLine = issueRenderLines[nextIndex]
            onSelectIssue?.(nextLine.issueId)
            rowRefs.current.get(getRenderKey(nextLine))?.scrollIntoView({ block: 'nearest' })
            break
          }

          case 'ArrowUp':
          case 'k': {
            event.preventDefault()
            const prevIndex = Math.max(currentIndex - 1, 0)
            const prevLine = issueRenderLines[prevIndex]
            onSelectIssue?.(prevLine.issueId)
            rowRefs.current.get(getRenderKey(prevLine))?.scrollIntoView({ block: 'nearest' })
            break
          }

          // Parent/child navigation
          case 'ArrowLeft':
          case 'h': {
            event.preventDefault()
            // Navigate to parent - find issue at parent lane
            const currentLine = issueRenderLines[currentIndex]
            if (currentLine?.parentLane !== undefined) {
              const parentLine = issueRenderLines.find(
                (line) => line.lane === currentLine.parentLane
              )
              if (parentLine) {
                onSelectIssue?.(parentLine.issueId)
                rowRefs.current.get(getRenderKey(parentLine))?.scrollIntoView({ block: 'nearest' })
              }
            }
            break
          }

          case 'ArrowRight':
          case 'l': {
            event.preventDefault()
            // Navigate to first child - find first issue with parentLane === current lane
            const currentLine = issueRenderLines[currentIndex]
            if (currentLine) {
              const childLine = issueRenderLines.find(
                (line) => line.parentLane === currentLine.lane
              )
              if (childLine) {
                onSelectIssue?.(childLine.issueId)
                rowRefs.current.get(getRenderKey(childLine))?.scrollIntoView({ block: 'nearest' })
              }
            }
            break
          }

          // Jump to first/last
          case 'g': {
            if (!event.shiftKey) {
              event.preventDefault()
              const firstLine = issueRenderLines[0]
              if (firstLine) {
                onSelectIssue?.(firstLine.issueId)
                rowRefs.current.get(getRenderKey(firstLine))?.scrollIntoView({ block: 'nearest' })
              }
            }
            break
          }

          case 'G': {
            event.preventDefault()
            const lastLine = issueRenderLines[issueRenderLines.length - 1]
            if (lastLine) {
              onSelectIssue?.(lastLine.issueId)
              rowRefs.current.get(getRenderKey(lastLine))?.scrollIntoView({ block: 'nearest' })
            }
            break
          }

          // Creation
          case 'o': {
            if (!event.shiftKey) {
              event.preventDefault()
              handleCreateBelow()
            }
            break
          }

          case 'O': {
            event.preventDefault()
            handleCreateAbove()
            break
          }

          // Editing existing
          case 'i': {
            event.preventDefault()
            handleStartEditAtStart()
            break
          }

          case 'a': {
            event.preventDefault()
            handleStartEditAtEnd()
            break
          }

          case 'r': {
            event.preventDefault()
            handleStartReplace()
            break
          }

          // Expand/collapse
          case ' ': {
            event.preventDefault()
            toggleExpanded(selectedIssueId)
            break
          }

          // Open full edit page
          case 'Enter':
          case 'e': {
            event.preventDefault()
            onEditIssue?.(selectedIssueId)
            break
          }

          // Deselect
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
      [
        editMode,
        selectedIssueId,
        issueRenderLines,
        onSelectIssue,
        onEditIssue,
        toggleExpanded,
        expandedIds,
        handleCancelEdit,
        handleCreateBelow,
        handleCreateAbove,
        handleStartEditAtStart,
        handleStartEditAtEnd,
        handleStartReplace,
        moveOperation,
        onMoveCancel,
      ]
    )

    // ============================================================================
    // Type and Status Change Handlers
    // ============================================================================

    const handleTypeChange = useCallback(
      async (issueId: string, newType: IssueType) => {
        await updateIssue({
          issueId,
          data: { projectId, type: newType },
        })
      },
      [updateIssue, projectId]
    )

    const handleStatusChange = useCallback(
      async (issueId: string, newStatus: IssueStatus) => {
        await updateIssue({
          issueId,
          data: { projectId, status: newStatus },
        })
      },
      [updateIssue, projectId]
    )

    const handleExecutionModeChange = useCallback(
      async (issueId: string, newMode: ExecutionMode) => {
        await updateIssue({
          issueId,
          data: { projectId, executionMode: newMode },
        })
      },
      [updateIssue, projectId]
    )

    // ============================================================================
    // Inline Editor Row Rendering
    // ============================================================================

    const renderInlineEditor = useCallback(
      (_lane: number) => {
        if (!pendingNewIssue) return null

        // Compute SVG width for lane offset
        const svgWidth = LANE_WIDTH * maxLanes + 12

        return (
          <div
            data-testid="task-graph-inline-create-row"
            className={cn(
              'flex items-center gap-2 transition-colors',
              'bg-muted ring-primary/50 ring-2'
            )}
            style={{ height: ROW_HEIGHT }}
          >
            {/* SVG placeholder for alignment */}
            <div style={{ width: svgWidth, flexShrink: 0 }} />

            {/* Type badge */}
            <span
              className="shrink-0 rounded px-1.5 py-0.5 text-[10px] font-medium"
              style={{
                backgroundColor: `${getTypeColor(IssueType.TASK)}20`,
                color: getTypeColor(IssueType.TASK),
              }}
            >
              Task
            </span>

            {/* Inline editor */}
            <InlineIssueEditor
              title={pendingNewIssue.title}
              onTitleChange={handleTitleChange}
              onSave={handleSave}
              onSaveAndEdit={handleSaveAndEdit}
              onCancel={handleCancelEdit}
              onIndent={handleIndent}
              onUnindent={handleUnindent}
              placeholder="Enter new issue title..."
              cursorPosition={EditCursorPosition.Start}
              showParentIndicator={!!pendingNewIssue.pendingChildId}
              showChildIndicator={!!pendingNewIssue.pendingParentId}
              isAbove={pendingNewIssue.isAbove}
            />
          </div>
        )
      },
      [
        pendingNewIssue,
        maxLanes,
        handleTitleChange,
        handleSave,
        handleSaveAndEdit,
        handleCancelEdit,
        handleIndent,
        handleUnindent,
      ]
    )

    // ============================================================================
    // Rendering
    // ============================================================================

    // Render loading skeleton
    if (isLoading) {
      return (
        <div className={cn('space-y-1', className)} data-testid="task-graph-loading">
          {Array.from({ length: 5 }).map((_, i) => (
            <IssueRowSkeleton key={i} />
          ))}
        </div>
      )
    }

    // Render error state
    if (isError) {
      return (
        <ErrorFallback
          title="Failed to load issues"
          description="Unable to fetch the task graph. Please try again."
          variant="inline"
          onRetry={() => refetch()}
          className={className}
        />
      )
    }

    // Render empty state
    if (renderLines.length === 0) {
      return (
        <IssuesEmptyState
          onCreateIssue={() => createIssue({ title: 'New issue' })}
          isCreating={isCreating}
          className={className}
        />
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
        data-testid="task-graph"
        className={cn(
          'scrollbar-thin scrollbar-track-transparent scrollbar-thumb-muted overflow-x-auto focus-visible:outline-none',
          className
        )}
        onKeyDown={handleKeyDown}
      >
        {renderLines.map((line, index) => {
          if (isIssueRenderLine(line)) {
            const isSelected = selectedIssueId === line.issueId
            const isExpanded = expandedIds.has(line.issueId)
            const isEditing =
              editMode === KeyboardEditMode.EditingExisting && pendingEdit?.issueId === line.issueId

            // Check if we should insert inline editor ABOVE this issue
            const shouldInsertAbove =
              editMode === KeyboardEditMode.CreatingNew &&
              pendingNewIssue?.isAbove &&
              pendingNewIssue?.referenceIssueId === line.issueId

            // Check if we should insert inline editor BELOW this issue
            const shouldInsertBelow =
              editMode === KeyboardEditMode.CreatingNew &&
              !pendingNewIssue?.isAbove &&
              pendingNewIssue?.referenceIssueId === line.issueId

            const renderKey = getRenderKey(line)

            return (
              <div key={renderKey}>
                {/* Insert inline editor ABOVE if creating above this issue */}
                {shouldInsertAbove && renderInlineEditor(line.lane)}

                {/* Issue row (or inline edit if editing existing) */}
                {isEditing && pendingEdit ? (
                  <div
                    data-testid="task-graph-issue-row"
                    data-issue-id={line.issueId}
                    className={cn(
                      'flex items-center gap-2 transition-colors',
                      'bg-muted ring-primary/50 ring-2'
                    )}
                    style={{ height: ROW_HEIGHT }}
                  >
                    {/* SVG placeholder for alignment */}
                    <div style={{ width: LANE_WIDTH * maxLanes + 12, flexShrink: 0 }} />

                    {/* Type badge */}
                    <span
                      className="shrink-0 rounded px-1.5 py-0.5 text-[10px] font-medium"
                      style={{
                        backgroundColor: `${getTypeColor(IssueType.TASK)}20`,
                        color: getTypeColor(IssueType.TASK),
                      }}
                    >
                      Task
                    </span>

                    {/* ID */}
                    <span className="text-muted-foreground shrink-0 font-mono text-xs">
                      {line.issueId.substring(0, 6)}
                    </span>

                    {/* Inline editor */}
                    <InlineIssueEditor
                      title={pendingEdit.title}
                      onTitleChange={handleTitleChange}
                      onSave={async () => {
                        if (!pendingEdit.title.trim()) {
                          handleCancelEdit()
                          return
                        }
                        // Only save if title changed
                        if (pendingEdit.title.trim() !== pendingEdit.originalTitle) {
                          try {
                            await updateIssue({
                              issueId: line.issueId,
                              data: { projectId, title: pendingEdit.title.trim() },
                            })
                          } catch {
                            // Keep edit mode on error
                          }
                        } else {
                          handleCancelEdit()
                        }
                        containerRef.current?.focus()
                      }}
                      onSaveAndEdit={async () => {
                        if (!pendingEdit.title.trim()) {
                          handleCancelEdit()
                          return
                        }
                        // Save title if changed, then navigate to edit
                        if (pendingEdit.title.trim() !== pendingEdit.originalTitle) {
                          try {
                            await updateIssue({
                              issueId: line.issueId,
                              data: { projectId, title: pendingEdit.title.trim() },
                            })
                            onEditIssue?.(line.issueId)
                          } catch {
                            // Keep edit mode on error
                          }
                        } else {
                          onEditIssue?.(line.issueId)
                        }
                      }}
                      onCancel={handleCancelEdit}
                      onIndent={() => {}}
                      onUnindent={() => {}}
                      placeholder="Enter issue title..."
                      cursorPosition={pendingEdit.cursorPosition}
                    />
                  </div>
                ) : (
                  <TaskGraphIssueRow
                    ref={(el) => {
                      if (el) {
                        rowRefs.current.set(renderKey, el)
                      } else {
                        rowRefs.current.delete(renderKey)
                      }
                    }}
                    line={line}
                    maxLanes={maxLanes}
                    projectId={projectId}
                    isSelected={isSelected}
                    isExpanded={isExpanded}
                    searchQuery={searchQuery}
                    onToggleExpand={() => toggleExpanded(line.issueId)}
                    onEdit={onEditIssue}
                    onRunAgent={onRunAgent}
                    onOpenSession={onOpenSession}
                    onClick={() => handleRowClick(line.issueId)}
                    onTypeChange={handleTypeChange}
                    onStatusChange={handleStatusChange}
                    onExecutionModeChange={handleExecutionModeChange}
                    onSelectFirstInstance={handleSelectFirstInstance}
                    isMoveSource={moveSourceIssueId === line.issueId}
                    isMoveOperationActive={!!moveOperation}
                    aria-rowindex={index + 1}
                    data-testid="task-graph-issue-row"
                    data-issue-id={line.issueId}
                  />
                )}

                {/* Expanded details */}
                {isExpanded && !isEditing && (
                  <TaskGraphExpandedDetails
                    line={line}
                    maxLanes={maxLanes}
                    onEdit={onEditIssue}
                    onRunAgent={onRunAgent}
                    onOpenSession={onOpenSession}
                    onClose={() => toggleExpanded(line.issueId)}
                  />
                )}

                {/* Insert inline editor BELOW if creating below this issue */}
                {shouldInsertBelow && renderInlineEditor(line.lane)}
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
)

export { TaskGraphView as default }
