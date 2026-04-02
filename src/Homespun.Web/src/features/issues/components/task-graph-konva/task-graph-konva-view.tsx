/**
 * TaskGraphKonvaView - Canvas-based visualization for issues on a project.
 *
 * Uses React Konva for canvas rendering with full edge paths between nodes.
 * Supports panning, keyboard navigation, and HTML overlays for issue content.
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
import { Stage, Layer } from 'react-konva'
import { useQueryClient } from '@tanstack/react-query'
import { cn } from '@/lib/utils'
import { IssueType, IssueStatus, ExecutionMode } from '@/api'
import { useSignalR } from '@/hooks/use-signalr'
import { registerNotificationHubEvents } from '@/lib/signalr/notification-hub'
import { ErrorFallback } from '@/components/error-boundary'
import { IssueRowSkeleton } from '../issue-row-skeleton'
import { IssuesEmptyState } from '../issues-empty-state'
import {
  computeLayout,
  isIssueRenderLine,
  getRenderKey,
  computeInheritedParentInfo,
  applyFilter,
  TaskGraphMarkerType,
  type ParsedFilter,
  type TaskGraphIssueRenderLine,
} from '../../services'
import { useTaskGraph, taskGraphQueryKey, useCreateIssue, useUpdateIssue } from '../../hooks'
import {
  KeyboardEditMode,
  EditCursorPosition,
  MoveOperationType,
  ViewMode,
  type PendingNewIssue,
  type InlineEditState,
} from '../../types'
import { InlineIssueEditor } from '../inline-issue-editor'
import { InlineIssueDetailRow } from '../inline-issue-detail-row'
import {
  KonvaIssueNode,
  KonvaVirtualNode,
  KonvaEdge,
  KonvaDiagonalEdge,
  LANE_WIDTH,
  ROW_HEIGHT,
  getTypeColor,
} from './konva-nodes'
import { KonvaHtmlRow } from './konva-html-row'
import { useCamera } from './use-camera'
import { useEdgePaths, useDiagonalEdges } from './use-edge-paths'
import { computeVirtualNodeData, getDisplayRowIndex } from './compute-virtual-node'

export interface TaskGraphKonvaViewProps {
  projectId: string
  depth?: number
  searchQuery?: string
  selectedIssueId?: string | null
  onSelectIssue?: (issueId: string | null) => void
  onEditIssue?: (issueId: string) => void
  onRunAgent?: (issueId: string) => void
  onOpenSession?: (sessionId: string) => void
  moveOperation?: MoveOperationType | null
  moveSourceIssueId?: string | null
  onMoveComplete?: (targetIssueId: string) => void
  onMoveCancel?: () => void
  appliedFilter?: ParsedFilter | null
  onFilterMatchCountChange?: (count: number) => void
  viewMode?: ViewMode
  className?: string
}

export interface TaskGraphKonvaViewRef {
  createAbove: () => void
  createBelow: () => void
}

const DEFAULT_VIEWPORT_HEIGHT = 600

/** Height for expanded detail panels (matches InlineIssueDetailRow height) */
export const DETAIL_PANEL_HEIGHT = 700

/**
 * Main TaskGraphKonvaView component.
 *
 * Renders issues on a canvas with full edge paths and HTML overlays.
 */
export const TaskGraphKonvaView = memo(
  forwardRef<TaskGraphKonvaViewRef, TaskGraphKonvaViewProps>(function TaskGraphKonvaView(
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
      viewMode = ViewMode.Next,
      className,
    },
    ref
  ) {
    const { taskGraph, isLoading, isError, refetch } = useTaskGraph(projectId)
    const queryClient = useQueryClient()

    // Container ref for measuring viewport
    const containerRef = useRef<HTMLDivElement>(null)
    const [viewportSize, setViewportSize] = useState({
      width: 800,
      height: DEFAULT_VIEWPORT_HEIGHT,
    })

    // Background color for Konva nodes (read from computed CSS)
    const [backgroundColor, setBackgroundColor] = useState('#09090b')
    useEffect(() => {
      const container = containerRef.current
      if (!container) return
      const bg = getComputedStyle(container).backgroundColor
      if (bg && bg !== 'rgba(0, 0, 0, 0)' && bg !== 'transparent') {
        setBackgroundColor(bg)
      }
    }, [])

    // Expanded rows state
    const [expandedIds, setExpandedIds] = useState<Set<string>>(new Set())

    // Edit mode state
    const [editMode, setEditMode] = useState<KeyboardEditMode>(KeyboardEditMode.Viewing)
    const [pendingNewIssue, setPendingNewIssue] = useState<PendingNewIssue | null>(null)
    const [pendingEdit, setPendingEdit] = useState<InlineEditState | null>(null)

    // Create/Update issue mutations
    const { createIssue, isCreating } = useCreateIssue({
      projectId,
      onSuccess: () => {
        setEditMode(KeyboardEditMode.Viewing)
        setPendingNewIssue(null)
      },
    })

    const { mutateAsync: updateIssue } = useUpdateIssue({
      onSuccess: () => {
        setEditMode(KeyboardEditMode.Viewing)
        setPendingEdit(null)
      },
    })

    // Compute render lines from task graph
    const unfilteredRenderLines = useMemo(() => {
      if (!taskGraph) return []
      return computeLayout(taskGraph, depth, viewMode)
    }, [taskGraph, depth, viewMode])

    // Build issue data map for filtering
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

    // Apply filter
    const renderLines = useMemo(() => {
      if (!appliedFilter) return unfilteredRenderLines

      return unfilteredRenderLines.filter((line) => {
        if (!isIssueRenderLine(line)) return true

        if (appliedFilter.isNext) {
          if (line.marker !== TaskGraphMarkerType.Actionable) {
            return false
          }
        }

        const issueData = issueDataMap.get(line.issueId)
        if (!issueData) return true

        return applyFilter(issueData, appliedFilter)
      })
    }, [unfilteredRenderLines, appliedFilter, issueDataMap])

    // Report filter match count
    useEffect(() => {
      if (!appliedFilter || !onFilterMatchCountChange) return
      const matchCount = renderLines.filter(isIssueRenderLine).length
      onFilterMatchCountChange(matchCount)
    }, [renderLines, appliedFilter, onFilterMatchCountChange])

    // Compute max lanes for canvas sizing
    const maxLanes = useMemo(() => {
      return Math.max(1, ...renderLines.filter(isIssueRenderLine).map((line) => line.lane + 1))
    }, [renderLines])

    // Issue render lines only
    const issueRenderLines = useMemo(() => {
      return renderLines.filter(isIssueRenderLine)
    }, [renderLines])

    // Compute cumulative Y positions for each row, accounting for expanded detail panels
    const rowYPositions = useMemo(() => {
      const positions: number[] = []
      let y = 0
      for (let i = 0; i < issueRenderLines.length; i++) {
        positions.push(y)
        y += ROW_HEIGHT
        if (expandedIds.has(issueRenderLines[i].issueId)) {
          y += DETAIL_PANEL_HEIGHT
        }
      }
      return positions
    }, [issueRenderLines, expandedIds])

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

    // Compute virtual node data for pending new issue
    const virtualNodeData = useMemo(
      () => computeVirtualNodeData(pendingNewIssue, editMode, issueRenderLines),
      [pendingNewIssue, editMode, issueRenderLines]
    )

    // Insertion row index (null when not creating)
    const insertionRowIndex = virtualNodeData?.insertionRowIndex ?? null

    // Content size for camera (extra row when creating a new issue)
    const contentSize = useMemo(() => {
      const svgWidth = LANE_WIDTH * maxLanes + LANE_WIDTH / 2
      const minContentArea = 800
      const availableContentArea = viewportSize.width - svgWidth
      const contentWidth = svgWidth + Math.max(minContentArea, availableContentArea)
      const lastRowY = rowYPositions.length > 0 ? rowYPositions[rowYPositions.length - 1] : 0
      const extraHeight = insertionRowIndex !== null ? ROW_HEIGHT : 0
      const contentHeight = lastRowY + ROW_HEIGHT + extraHeight
      return { width: contentWidth, height: contentHeight }
    }, [maxLanes, rowYPositions, insertionRowIndex, viewportSize.width])

    // Camera state
    const { camera, scrollToRow, handleDragMove, handleDragEnd, handleWheel, touchHandlers } =
      useCamera(contentSize, viewportSize)

    // Compute edge paths
    const edgePaths = useEdgePaths(renderLines, rowYPositions)
    const diagonalEdges = useDiagonalEdges(renderLines)

    // Attach non-passive touch event listeners for mobile panning.
    // Must use addEventListener (not React onTouchMove) to set { passive: false }
    // which is required for preventDefault() to stop page scrolling.
    useEffect(() => {
      const container = containerRef.current
      if (!container) return

      const { handleTouchStart, handleTouchMove, handleTouchEnd } = touchHandlers
      container.addEventListener('touchstart', handleTouchStart, { passive: true })
      container.addEventListener('touchmove', handleTouchMove, { passive: false })
      container.addEventListener('touchend', handleTouchEnd)

      return () => {
        container.removeEventListener('touchstart', handleTouchStart)
        container.removeEventListener('touchmove', handleTouchMove)
        container.removeEventListener('touchend', handleTouchEnd)
      }
    }, [touchHandlers])

    // Measure container on resize
    useEffect(() => {
      const container = containerRef.current
      if (!container) return

      const updateSize = () => {
        setViewportSize({
          width: container.clientWidth,
          height: container.clientHeight || DEFAULT_VIEWPORT_HEIGHT,
        })
      }

      updateSize()

      const observer = new ResizeObserver(updateSize)
      observer.observe(container)

      return () => observer.disconnect()
    }, [])

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
            queryClient.invalidateQueries({
              queryKey: taskGraphQueryKey(projectId),
            })
          }
        },
      })

      return cleanup
    }, [connection, projectId, queryClient])

    // Toggle expanded state
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
        const firstIndex = issueRenderLines.findIndex((l) => l.issueId === issueId)
        if (firstIndex >= 0) {
          scrollToRow(firstIndex, ROW_HEIGHT)
        }
      },
      [onSelectIssue, issueRenderLines, scrollToRow]
    )

    // Handle row click
    const handleRowClick = useCallback(
      (issueId: string) => {
        if (editMode === KeyboardEditMode.CreatingNew) {
          setEditMode(KeyboardEditMode.Viewing)
          setPendingNewIssue(null)
        }

        if (moveOperation && moveSourceIssueId && issueId !== moveSourceIssueId) {
          onMoveComplete?.(issueId)
          return
        }

        onSelectIssue?.(issueId)
      },
      [onSelectIssue, editMode, moveOperation, moveSourceIssueId, onMoveComplete]
    )

    // Inline creation handlers
    const handleCreateBelow = useCallback(() => {
      if (selectedIndex < 0) return
      const referenceIssue = issueRenderLines[selectedIndex]
      if (!referenceIssue) return

      const inheritedParent = computeInheritedParentInfo(taskGraph, referenceIssue.issueId, false)

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

      const inheritedParent = computeInheritedParentInfo(taskGraph, referenceIssue.issueId, true)

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

    const handleCreateAtTop = useCallback(() => {
      const firstIssue = issueRenderLines[0]
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

    const handleCreateAtBottom = useCallback(() => {
      const lastIssue = issueRenderLines[issueRenderLines.length - 1]
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

    // Expose imperative methods
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
      containerRef.current?.focus()
    }, [])

    const handleTitleChange = useCallback((title: string) => {
      setPendingNewIssue((prev) => (prev ? { ...prev, title } : null))
      setPendingEdit((prev) => (prev ? { ...prev, title } : null))
    }, [])

    // Inline editing handlers
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

    // Type/Status change handlers
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

    // Keyboard navigation
    const handleKeyDown = useCallback(
      (event: React.KeyboardEvent<HTMLDivElement>) => {
        if (moveOperation && event.key === 'Escape') {
          event.preventDefault()
          onMoveCancel?.()
          return
        }

        if (editMode !== KeyboardEditMode.Viewing) {
          if (event.key === 'Escape') {
            event.preventDefault()
            handleCancelEdit()
          }
          return
        }

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
          case 'ArrowDown':
          case 'j': {
            event.preventDefault()
            const nextIndex = Math.min(currentIndex + 1, issueRenderLines.length - 1)
            const nextLine = issueRenderLines[nextIndex]
            onSelectIssue?.(nextLine.issueId)
            scrollToRow(nextIndex, ROW_HEIGHT)
            break
          }

          case 'ArrowUp':
          case 'k': {
            event.preventDefault()
            const prevIndex = Math.max(currentIndex - 1, 0)
            const prevLine = issueRenderLines[prevIndex]
            onSelectIssue?.(prevLine.issueId)
            scrollToRow(prevIndex, ROW_HEIGHT)
            break
          }

          case 'ArrowLeft':
          case 'h': {
            event.preventDefault()
            const currentLine = issueRenderLines[currentIndex]
            if (currentLine?.parentLane !== undefined && currentLine.parentLane !== null) {
              const parentLine = issueRenderLines.find(
                (line) => line.lane === currentLine.parentLane
              )
              if (parentLine) {
                onSelectIssue?.(parentLine.issueId)
                const parentIndex = issueRenderLines.indexOf(parentLine)
                if (parentIndex >= 0) scrollToRow(parentIndex, ROW_HEIGHT)
              }
            }
            break
          }

          case 'ArrowRight':
          case 'l': {
            event.preventDefault()
            const currentLine = issueRenderLines[currentIndex]
            if (currentLine) {
              const childLine = issueRenderLines.find(
                (line) => line.parentLane === currentLine.lane
              )
              if (childLine) {
                onSelectIssue?.(childLine.issueId)
                const childIndex = issueRenderLines.indexOf(childLine)
                if (childIndex >= 0) scrollToRow(childIndex, ROW_HEIGHT)
              }
            }
            break
          }

          case 'g': {
            if (!event.shiftKey) {
              event.preventDefault()
              const firstLine = issueRenderLines[0]
              if (firstLine) {
                onSelectIssue?.(firstLine.issueId)
                scrollToRow(0, ROW_HEIGHT)
              }
            }
            break
          }

          case 'G': {
            event.preventDefault()
            const lastLine = issueRenderLines[issueRenderLines.length - 1]
            if (lastLine) {
              onSelectIssue?.(lastLine.issueId)
              scrollToRow(issueRenderLines.length - 1, ROW_HEIGHT)
            }
            break
          }

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

          case ' ': {
            event.preventDefault()
            toggleExpanded(selectedIssueId)
            break
          }

          case 'Enter':
          case 'e': {
            event.preventDefault()
            onEditIssue?.(selectedIssueId)
            break
          }

          case 'Escape': {
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
        scrollToRow,
      ]
    )

    // Render loading skeleton
    if (isLoading) {
      return (
        <div className={cn('space-y-1', className)} data-testid="task-graph-konva-loading">
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

    // Render task graph canvas
    return (
      <div
        ref={containerRef}
        tabIndex={0}
        aria-label="Task graph canvas"
        data-testid="task-graph-konva"
        className={cn(
          'relative overflow-hidden focus-visible:outline-none',
          'scrollbar-thin scrollbar-track-transparent scrollbar-thumb-muted',
          className
        )}
        onKeyDown={handleKeyDown}
        onWheel={handleWheel}
      >
        {/* Konva Stage */}
        <Stage
          width={viewportSize.width}
          height={viewportSize.height}
          x={-camera.x}
          y={-camera.y}
          draggable
          onDragMove={handleDragMove}
          onDragEnd={handleDragEnd}
        >
          {/* Edges layer */}
          <Layer>
            {edgePaths.map((edge) => {
              // Shift edge Y-coordinates when a virtual row is inserted
              const displayPoints =
                insertionRowIndex !== null && edge.rowIndex >= insertionRowIndex
                  ? edge.points.map((v, i) => (i % 2 === 1 ? v + ROW_HEIGHT : v))
                  : edge.points
              return (
                <KonvaEdge key={edge.id} id={edge.id} points={displayPoints} color={edge.color} />
              )
            })}
            {diagonalEdges.map((edge) => {
              // Find source issue row index to determine if shifting is needed
              const sourceRowIndex = issueRenderLines.findIndex(
                (l) => l.issueId === edge.fromIssueId
              )
              const displayPoints =
                insertionRowIndex !== null && sourceRowIndex >= insertionRowIndex
                  ? edge.points.map((v, i) => (i % 2 === 1 ? v + ROW_HEIGHT : v))
                  : edge.points
              return (
                <KonvaDiagonalEdge
                  key={edge.id}
                  id={edge.id}
                  points={displayPoints}
                  color={edge.color}
                />
              )
            })}
          </Layer>

          {/* Nodes layer */}
          <Layer>
            {issueRenderLines.map((line, rowIndex) => (
              <KonvaIssueNode
                key={`node-${getRenderKey(line)}`}
                line={line}
                rowIndex={getDisplayRowIndex(rowIndex, insertionRowIndex)}
                rowY={
                  insertionRowIndex !== null && rowIndex >= insertionRowIndex
                    ? (rowYPositions[rowIndex] ?? rowIndex * ROW_HEIGHT) + ROW_HEIGHT
                    : rowYPositions[rowIndex]
                }
                onClick={() => handleRowClick(line.issueId)}
                backgroundColor={backgroundColor}
              />
            ))}
            {virtualNodeData && (
              <KonvaVirtualNode
                lane={virtualNodeData.virtualLane}
                rowY={
                  rowYPositions[virtualNodeData.insertionRowIndex] ??
                  virtualNodeData.insertionRowIndex * ROW_HEIGHT
                }
              />
            )}
          </Layer>
        </Stage>

        {/* HTML overlays for issue content */}
        <div
          className="pointer-events-none absolute top-0 left-0"
          style={{
            transform: `translate(${-camera.x}px, ${-camera.y}px)`,
          }}
        >
          {/* Inline editor row at insertion point */}
          {virtualNodeData && pendingNewIssue && (
            <div
              style={{
                position: 'absolute',
                top:
                  rowYPositions[virtualNodeData.insertionRowIndex] ??
                  virtualNodeData.insertionRowIndex * ROW_HEIGHT,
                left: 0,
                width: contentSize.width,
                pointerEvents: 'auto',
              }}
            >
              <InlineEditorRow
                pendingNewIssue={pendingNewIssue}
                maxLanes={maxLanes}
                onTitleChange={handleTitleChange}
                onSave={async () => {
                  if (!pendingNewIssue.title.trim()) {
                    handleCancelEdit()
                    return
                  }
                  const hasExplicitHierarchy =
                    pendingNewIssue.pendingParentId || pendingNewIssue.pendingChildId
                  const parentIssueId = hasExplicitHierarchy
                    ? pendingNewIssue.pendingParentId
                    : pendingNewIssue.inheritedParentIssueId
                  const siblingIssueId = hasExplicitHierarchy
                    ? undefined
                    : pendingNewIssue.siblingIssueId
                  const insertBefore = hasExplicitHierarchy
                    ? undefined
                    : pendingNewIssue.insertBefore
                  await createIssue({
                    title: pendingNewIssue.title.trim(),
                    parentIssueId,
                    childIssueId: pendingNewIssue.pendingChildId,
                    siblingIssueId,
                    insertBefore,
                  })
                  containerRef.current?.focus()
                }}
                onCancel={handleCancelEdit}
              />
            </div>
          )}

          {/* Issue rows */}
          {issueRenderLines.map((line, rowIndex) => {
            const renderKey = getRenderKey(line)
            const isSelected = selectedIssueId === line.issueId
            const isExpanded = expandedIds.has(line.issueId)
            const isEditing =
              editMode === KeyboardEditMode.EditingExisting && pendingEdit?.issueId === line.issueId

            return (
              <div
                key={renderKey}
                style={{
                  position: 'absolute',
                  top:
                    insertionRowIndex !== null && rowIndex >= insertionRowIndex
                      ? (rowYPositions[rowIndex] ?? rowIndex * ROW_HEIGHT) + ROW_HEIGHT
                      : (rowYPositions[rowIndex] ?? rowIndex * ROW_HEIGHT),
                  left: 0,
                  width: contentSize.width,
                  pointerEvents: 'auto',
                }}
              >
                {/* Issue row or inline edit */}
                {isEditing && pendingEdit ? (
                  <InlineEditRow
                    line={line}
                    pendingEdit={pendingEdit}
                    maxLanes={maxLanes}
                    onTitleChange={handleTitleChange}
                    onSave={async () => {
                      if (!pendingEdit.title.trim()) {
                        handleCancelEdit()
                        return
                      }
                      if (pendingEdit.title.trim() !== pendingEdit.originalTitle) {
                        await updateIssue({
                          issueId: line.issueId,
                          data: { projectId, title: pendingEdit.title.trim() },
                        })
                      } else {
                        handleCancelEdit()
                      }
                      containerRef.current?.focus()
                    }}
                    onCancel={handleCancelEdit}
                  />
                ) : (
                  <KonvaHtmlRow
                    line={line}
                    projectId={projectId}
                    maxLanes={maxLanes}
                    isSelected={isSelected}
                    isExpanded={isExpanded}
                    searchQuery={searchQuery}
                    onClick={() => handleRowClick(line.issueId)}
                    onDoubleClick={() => toggleExpanded(line.issueId)}
                    onEdit={onEditIssue}
                    onRunAgent={onRunAgent}
                    onOpenSession={onOpenSession}
                    onTypeChange={handleTypeChange}
                    onStatusChange={handleStatusChange}
                    onExecutionModeChange={handleExecutionModeChange}
                    onSelectFirstInstance={handleSelectFirstInstance}
                    isMoveSource={moveSourceIssueId === line.issueId}
                    isMoveOperationActive={!!moveOperation}
                  />
                )}

                {/* Expanded details */}
                {isExpanded && !isEditing && (
                  <div>
                    <InlineIssueDetailRow
                      line={line}
                      maxLanes={maxLanes}
                      onEdit={onEditIssue}
                      onRunAgent={onRunAgent}
                      onOpenSession={onOpenSession}
                      onClose={() => toggleExpanded(line.issueId)}
                    />
                  </div>
                )}
              </div>
            )
          })}
        </div>
      </div>
    )
  })
)

// Helper component for inline editing
interface InlineEditorRowProps {
  pendingNewIssue: PendingNewIssue
  maxLanes: number
  onTitleChange: (title: string) => void
  onSave: () => Promise<void>
  onCancel: () => void
}

function InlineEditorRow({
  pendingNewIssue,
  maxLanes,
  onTitleChange,
  onSave,
  onCancel,
}: InlineEditorRowProps) {
  const svgWidth = LANE_WIDTH * maxLanes + LANE_WIDTH / 2 + 12

  return (
    <div
      className="bg-muted ring-primary/50 flex items-center gap-2 ring-2"
      style={{ height: ROW_HEIGHT }}
    >
      <div style={{ width: svgWidth, flexShrink: 0 }} />
      <span
        className="shrink-0 rounded px-1.5 py-0.5 text-[10px] font-medium"
        style={{
          backgroundColor: `${getTypeColor(IssueType.TASK)}20`,
          color: getTypeColor(IssueType.TASK),
        }}
      >
        Task
      </span>
      <InlineIssueEditor
        title={pendingNewIssue.title}
        onTitleChange={onTitleChange}
        onSave={onSave}
        onSaveAndEdit={onSave}
        onCancel={onCancel}
        onIndent={() => {}}
        onUnindent={() => {}}
        placeholder="Enter new issue title..."
        cursorPosition={EditCursorPosition.Start}
        showParentIndicator={!!pendingNewIssue.pendingChildId}
        showChildIndicator={!!pendingNewIssue.pendingParentId}
        isAbove={pendingNewIssue.isAbove}
      />
    </div>
  )
}

// Helper component for inline edit row
interface InlineEditRowProps {
  line: TaskGraphIssueRenderLine
  pendingEdit: InlineEditState
  maxLanes: number
  onTitleChange: (title: string) => void
  onSave: () => Promise<void>
  onCancel: () => void
}

function InlineEditRow({
  line,
  pendingEdit,
  maxLanes,
  onTitleChange,
  onSave,
  onCancel,
}: InlineEditRowProps) {
  const svgWidth = LANE_WIDTH * maxLanes + LANE_WIDTH / 2 + 12

  return (
    <div
      className="bg-muted ring-primary/50 flex items-center gap-2 ring-2"
      style={{ height: ROW_HEIGHT }}
      data-testid="konva-html-row"
      data-issue-id={line.issueId}
    >
      <div style={{ width: svgWidth, flexShrink: 0 }} />
      <span
        className="shrink-0 rounded px-1.5 py-0.5 text-[10px] font-medium"
        style={{
          backgroundColor: `${getTypeColor(IssueType.TASK)}20`,
          color: getTypeColor(IssueType.TASK),
        }}
      >
        Task
      </span>
      <span className="text-muted-foreground shrink-0 font-mono text-xs">
        {line.issueId.substring(0, 6)}
      </span>
      <InlineIssueEditor
        title={pendingEdit.title}
        onTitleChange={onTitleChange}
        onSave={onSave}
        onSaveAndEdit={onSave}
        onCancel={onCancel}
        onIndent={() => {}}
        onUnindent={() => {}}
        placeholder="Enter issue title..."
        cursorPosition={pendingEdit.cursorPosition}
      />
    </div>
  )
}

export { TaskGraphKonvaView as default }
