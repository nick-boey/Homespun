import { useState, useCallback } from 'react'
import { createFileRoute, useNavigate } from '@tanstack/react-router'
import { TaskGraphView, ProjectToolbar, useToolbarShortcuts } from '@/features/issues'

export const Route = createFileRoute('/projects/$projectId/issues/')({
  component: IssuesList,
})

function IssuesList() {
  const { projectId } = Route.useParams()
  const navigate = useNavigate()

  // State
  const [selectedIssueId, setSelectedIssueId] = useState<string | null>(null)
  const [depth, setDepth] = useState(3)
  const [searchQuery, setSearchQuery] = useState('')

  // Compute search match count from rendered issues
  const [searchMatchCount] = useState(0)

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
    // TODO: Implement create above
    console.log('Create above', selectedIssueId)
  }, [selectedIssueId])

  const handleCreateBelow = useCallback(() => {
    // TODO: Implement create below
    console.log('Create below', selectedIssueId)
  }, [selectedIssueId])

  const handleMakeChild = useCallback(() => {
    // TODO: Implement make child
    console.log('Make child', selectedIssueId)
  }, [selectedIssueId])

  const handleMakeParent = useCallback(() => {
    // TODO: Implement make parent
    console.log('Make parent', selectedIssueId)
  }, [selectedIssueId])

  const handleOpenAgentLauncher = useCallback(() => {
    // TODO: Implement agent launcher
    console.log('Open agent launcher', selectedIssueId)
  }, [selectedIssueId])

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
        onEditIssue={() => handleEditIssue()}
        onOpenAgentLauncher={handleOpenAgentLauncher}
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
          projectId={projectId}
          depth={depth}
          searchQuery={searchQuery}
          selectedIssueId={selectedIssueId}
          onSelectIssue={setSelectedIssueId}
          onEditIssue={handleEditIssue}
        />
      </div>
    </div>
  )
}
