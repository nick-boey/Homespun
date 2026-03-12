import { useState, useCallback, useRef } from 'react'
import { createFileRoute, useNavigate } from '@tanstack/react-router'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import {
  TaskGraphView,
  ProjectToolbar,
  useToolbarShortcuts,
  taskGraphQueryKey,
  type TaskGraphViewRef,
} from '@/features/issues'
import { MoveOperationType } from '@/features/issues/types'
import { AgentLauncherDialog } from '@/features/agents'
import { AssignIssueDialog } from '@/features/issues/components/assign-issue-popover'
import { Issues } from '@/api'

export const Route = createFileRoute('/projects/$projectId/issues/')({
  component: IssuesList,
})

function IssuesList() {
  const { projectId } = Route.useParams()
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  // Ref to TaskGraphView for imperative actions
  const taskGraphRef = useRef<TaskGraphViewRef>(null)

  // State
  const [selectedIssueId, setSelectedIssueId] = useState<string | null>(null)
  const [depth, setDepth] = useState(3)
  const [searchQuery, setSearchQuery] = useState('')

  // Compute search match count from rendered issues
  const [searchMatchCount] = useState(0)

  // Agent launcher dialog state
  const [agentLauncherOpen, setAgentLauncherOpen] = useState(false)
  const [agentLauncherIssueId, setAgentLauncherIssueId] = useState<string | null>(null)

  // Assign issue popover state
  const [assignPopoverOpen, setAssignPopoverOpen] = useState(false)

  // Move operation state
  const [moveOperation, setMoveOperation] = useState<MoveOperationType | null>(null)
  const [moveSourceIssueId, setMoveSourceIssueId] = useState<string | null>(null)

  // Set parent mutation for move operations
  const setParentMutation = useMutation({
    mutationFn: ({ childId, parentIssueId }: { childId: string; parentIssueId: string | null }) =>
      Issues.postApiIssuesByChildIdSetParent({
        path: { childId },
        body: { projectId, parentIssueId },
      }),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: taskGraphQueryKey(projectId) })
    },
  })

  // Handlers
  const handleEditIssue = useCallback(
    (issueId?: string) => {
      const id = issueId ?? selectedIssueId
      if (id) {
        navigate({
          to: '/projects/$projectId/issues/$issueId/edit',
          params: { projectId, issueId: id },
        })
      }
    },
    [navigate, projectId, selectedIssueId]
  )

  const handleCreateAbove = useCallback(() => {
    taskGraphRef.current?.createAbove()
  }, [])

  const handleCreateBelow = useCallback(() => {
    taskGraphRef.current?.createBelow()
  }, [])

  const handleMakeChild = useCallback(() => {
    if (!selectedIssueId) return
    // Toggle: if already in child mode, cancel; otherwise start child mode
    if (moveOperation === MoveOperationType.AsChildOf) {
      setMoveOperation(null)
      setMoveSourceIssueId(null)
    } else {
      setMoveOperation(MoveOperationType.AsChildOf)
      setMoveSourceIssueId(selectedIssueId)
    }
  }, [selectedIssueId, moveOperation])

  const handleMakeParent = useCallback(() => {
    if (!selectedIssueId) return
    // Toggle: if already in parent mode, cancel; otherwise start parent mode
    if (moveOperation === MoveOperationType.AsParentOf) {
      setMoveOperation(null)
      setMoveSourceIssueId(null)
    } else {
      setMoveOperation(MoveOperationType.AsParentOf)
      setMoveSourceIssueId(selectedIssueId)
    }
  }, [selectedIssueId, moveOperation])

  const handleMoveComplete = useCallback(
    async (targetIssueId: string) => {
      if (!moveSourceIssueId || !moveOperation) return

      try {
        if (moveOperation === MoveOperationType.AsChildOf) {
          // Make source a child of target
          await setParentMutation.mutateAsync({
            childId: moveSourceIssueId,
            parentIssueId: targetIssueId,
          })
        } else if (moveOperation === MoveOperationType.AsParentOf) {
          // Make target a child of source
          await setParentMutation.mutateAsync({
            childId: targetIssueId,
            parentIssueId: moveSourceIssueId,
          })
        }
      } finally {
        // Reset move operation state
        setMoveOperation(null)
        setMoveSourceIssueId(null)
      }
    },
    [moveSourceIssueId, moveOperation, setParentMutation]
  )

  const handleMoveCancel = useCallback(() => {
    setMoveOperation(null)
    setMoveSourceIssueId(null)
  }, [])

  const handleOpenAgentLauncher = useCallback(() => {
    if (selectedIssueId) {
      setAgentLauncherIssueId(selectedIssueId)
      setAgentLauncherOpen(true)
    }
  }, [selectedIssueId])

  const handleAssignIssue = useCallback(() => {
    if (selectedIssueId) {
      setAssignPopoverOpen(true)
    }
  }, [selectedIssueId])

  // Handler for running agent on a specific issue (from row actions)
  const handleRunAgent = useCallback((issueId: string) => {
    setAgentLauncherIssueId(issueId)
    setAgentLauncherOpen(true)
  }, [])

  const handleFocusSearch = useCallback(() => {
    // Focus the search input in toolbar
    const searchInput = document.querySelector<HTMLInputElement>('input[type="search"]')
    searchInput?.focus()
  }, [])

  const handleNextMatch = useCallback(() => {
    // TODO: Implement next match
    console.log('Next match')
  }, [])

  const handlePreviousMatch = useCallback(() => {
    // TODO: Implement previous match
    console.log('Previous match')
  }, [])

  const handleEmbedSearch = useCallback(() => {
    // TODO: Implement embed search
    console.log('Embed search')
  }, [])

  // Register keyboard shortcuts
  useToolbarShortcuts({
    onCreateAbove: handleCreateAbove,
    onCreateBelow: handleCreateBelow,
    onUndo: () => {}, // Handled by useIssueHistory in toolbar
    onRedo: () => {}, // Handled by useIssueHistory in toolbar
    onOpenAgentLauncher: handleOpenAgentLauncher,
    onDecreaseDepth: () => setDepth((d) => Math.max(1, d - 1)),
    onIncreaseDepth: () => setDepth((d) => d + 1),
    onFocusSearch: handleFocusSearch,
    onNextMatch: handleNextMatch,
    onPreviousMatch: handlePreviousMatch,
    onEmbedSearch: handleEmbedSearch,
  })

  return (
    <div className="flex h-full flex-col">
      {/* Toolbar */}
      <ProjectToolbar
        projectId={projectId}
        selectedIssueId={selectedIssueId}
        onCreateAbove={handleCreateAbove}
        onCreateBelow={handleCreateBelow}
        onMakeChild={handleMakeChild}
        onMakeParent={handleMakeParent}
        childOfActive={moveOperation === MoveOperationType.AsChildOf}
        parentOfActive={moveOperation === MoveOperationType.AsParentOf}
        onEditIssue={() => handleEditIssue()}
        onOpenAgentLauncher={handleOpenAgentLauncher}
        onAssignIssue={handleAssignIssue}
        depth={depth}
        onDepthChange={setDepth}
        searchQuery={searchQuery}
        onSearchChange={setSearchQuery}
        searchMatchCount={searchMatchCount}
        onNextMatch={handleNextMatch}
        onPreviousMatch={handlePreviousMatch}
        onEmbedSearch={handleEmbedSearch}
      />

      {/* Task Graph View */}
      <div className="min-h-0 flex-1 overflow-auto p-4">
        <TaskGraphView
          ref={taskGraphRef}
          projectId={projectId}
          depth={depth}
          searchQuery={searchQuery}
          selectedIssueId={selectedIssueId}
          onSelectIssue={setSelectedIssueId}
          onEditIssue={handleEditIssue}
          onRunAgent={handleRunAgent}
          moveOperation={moveOperation}
          moveSourceIssueId={moveSourceIssueId}
          onMoveComplete={handleMoveComplete}
          onMoveCancel={handleMoveCancel}
        />
      </div>

      {/* Agent Launcher Dialog */}
      {agentLauncherIssueId && (
        <AgentLauncherDialog
          open={agentLauncherOpen}
          onOpenChange={setAgentLauncherOpen}
          projectId={projectId}
          issueId={agentLauncherIssueId}
        />
      )}

      {/* Assign Issue Dialog */}
      {selectedIssueId && (
        <AssignIssueDialog
          open={assignPopoverOpen}
          onOpenChange={setAssignPopoverOpen}
          projectId={projectId}
          issueId={selectedIssueId}
        />
      )}
    </div>
  )
}
