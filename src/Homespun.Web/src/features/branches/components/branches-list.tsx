import { useState } from 'react'
import { useNavigate } from '@tanstack/react-router'
import { RefreshCw, GitBranch, Plus } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { BranchCard } from './branch-card'
import { BranchCardSkeleton } from './branch-card-skeleton'
import { RemoteBranchRow } from './remote-branch-row'
import { BranchesEmptyState } from './branches-empty-state'
import { CreateBranchSessionDialog } from './create-branch-session-dialog'
import { ErrorFallback } from '@/components/error-boundary'
import { useClones, useDeleteClone, usePullClone, useCreateClone } from '../hooks/use-clones'
import { useBranches, getRemoteOnlyBranches } from '../hooks/use-branches'
import type { CreateBranchSessionResult } from '../hooks/use-create-branch-session'
import type { BranchInfo, CloneInfo } from '@/api/generated/types.gen'

export interface BranchesListProps {
  projectId: string
  repoPath: string
}

export function BranchesList({ projectId, repoPath }: BranchesListProps) {
  const navigate = useNavigate()

  const {
    data: clones,
    isLoading: clonesLoading,
    isError: clonesError,
    error: clonesErrorObj,
    refetch: refetchClones,
  } = useClones(projectId)

  const {
    data: branches,
    isLoading: branchesLoading,
    isError: branchesError,
    error: branchesErrorObj,
    refetch: refetchBranches,
  } = useBranches(repoPath)

  const deleteClone = useDeleteClone()
  const pullClone = usePullClone()
  const createClone = useCreateClone(projectId)

  const [pullingClones, setPullingClones] = useState<Set<string>>(new Set())
  const [deletingClones, setDeletingClones] = useState<Set<string>>(new Set())
  const [creatingBranches, setCreatingBranches] = useState<Set<string>>(new Set())
  const [isRefreshingAll, setIsRefreshingAll] = useState(false)
  const [isNewSessionDialogOpen, setIsNewSessionDialogOpen] = useState(false)

  const handleSessionCreated = (result: CreateBranchSessionResult) => {
    // Navigate to the session page
    navigate({ to: '/sessions/$sessionId', params: { sessionId: result.sessionId } })
  }

  const isLoading = clonesLoading || branchesLoading
  const isError = clonesError || branchesError
  const error = clonesErrorObj || branchesErrorObj

  // Map clones to their branch info for richer display
  const cloneWithBranchInfo = (clones ?? []).map((clone) => {
    const branchName = clone.expectedBranch ?? clone.branch?.replace('refs/heads/', '')
    const branchInfo = (branches ?? []).find(
      (b) => b.shortName === branchName || b.name === clone.branch
    )
    return { clone, branchInfo }
  })

  // Get remote-only branches
  const remoteBranches = branches ? getRemoteOnlyBranches(branches) : []

  const handlePull = async (clone: CloneInfo) => {
    if (!clone.path) return
    setPullingClones((prev) => new Set(prev).add(clone.path!))
    try {
      await pullClone.mutateAsync({
        projectId,
        clonePath: clone.path,
      })
    } finally {
      setPullingClones((prev) => {
        const next = new Set(prev)
        next.delete(clone.path!)
        return next
      })
    }
  }

  const handleDelete = async (clone: CloneInfo) => {
    if (!clone.path) return
    setDeletingClones((prev) => new Set(prev).add(clone.path!))
    try {
      await deleteClone.mutateAsync({
        projectId,
        clonePath: clone.path,
      })
    } finally {
      setDeletingClones((prev) => {
        const next = new Set(prev)
        next.delete(clone.path!)
        return next
      })
    }
  }

  const handleCreateWorktree = async (branch: BranchInfo) => {
    if (!branch.shortName) return
    setCreatingBranches((prev) => new Set(prev).add(branch.shortName!))
    try {
      await createClone.mutateAsync({
        branchName: branch.shortName,
        createBranch: false,
      })
    } finally {
      setCreatingBranches((prev) => {
        const next = new Set(prev)
        next.delete(branch.shortName!)
        return next
      })
    }
  }

  const handleRefreshAll = async () => {
    setIsRefreshingAll(true)
    try {
      // Refetch data
      await Promise.all([refetchClones(), refetchBranches()])
      // Pull all clones
      const pullPromises = (clones ?? []).filter((c) => c.path).map((clone) => handlePull(clone))
      await Promise.allSettled(pullPromises)
    } finally {
      setIsRefreshingAll(false)
    }
  }

  if (isLoading) {
    return (
      <div className="space-y-6">
        <div className="flex items-center justify-between">
          <h2 className="text-lg font-semibold">Local Worktrees</h2>
          <div className="flex items-center gap-2">
            <Button variant="outline" size="sm" disabled>
              <Plus className="mr-2 h-4 w-4" />
              New Session
            </Button>
            <Button variant="outline" size="sm" disabled>
              <RefreshCw className="mr-2 h-4 w-4" />
              Refresh All
            </Button>
          </div>
        </div>
        <div className="grid gap-3">
          <BranchCardSkeleton />
          <BranchCardSkeleton />
          <BranchCardSkeleton />
        </div>
      </div>
    )
  }

  if (isError) {
    return (
      <div className="space-y-6">
        <div className="flex items-center justify-between">
          <h2 className="text-lg font-semibold">Local Worktrees</h2>
        </div>
        <ErrorFallback
          error={error}
          title="Failed to load branches"
          description="Unable to fetch worktrees and branches. Please try again."
          variant="inline"
          onRetry={() => {
            refetchClones()
            refetchBranches()
          }}
        />
      </div>
    )
  }

  const hasLocalWorktrees = clones && clones.length > 0
  const hasRemoteBranches = remoteBranches.length > 0

  return (
    <div className="space-y-8">
      {/* Local Worktrees Section */}
      <section className="space-y-4">
        <div className="flex items-center justify-between">
          <h2 className="flex items-center gap-2 text-lg font-semibold">
            <GitBranch className="h-5 w-5" />
            Local Worktrees
            {hasLocalWorktrees && (
              <span className="text-muted-foreground text-sm font-normal">({clones.length})</span>
            )}
          </h2>
          <div className="flex items-center gap-2">
            <Button variant="outline" size="sm" onClick={() => setIsNewSessionDialogOpen(true)}>
              <Plus className="mr-2 h-4 w-4" />
              New Session
            </Button>
            <Button
              variant="outline"
              size="sm"
              onClick={handleRefreshAll}
              disabled={isRefreshingAll || !hasLocalWorktrees}
            >
              <RefreshCw className={`mr-2 h-4 w-4 ${isRefreshingAll ? 'animate-spin' : ''}`} />
              Refresh All
            </Button>
          </div>
        </div>

        <CreateBranchSessionDialog
          open={isNewSessionDialogOpen}
          onOpenChange={setIsNewSessionDialogOpen}
          projectId={projectId}
          onSessionCreated={handleSessionCreated}
        />

        {hasLocalWorktrees ? (
          <div className="grid gap-3">
            {cloneWithBranchInfo.map(({ clone, branchInfo }) => (
              <BranchCard
                key={clone.path}
                branch={
                  branchInfo ?? {
                    shortName: clone.expectedBranch ?? clone.branch?.replace('refs/heads/', ''),
                    commitSha: clone.headCommit,
                  }
                }
                projectId={projectId}
                onPull={() => handlePull(clone)}
                onDelete={() => handleDelete(clone)}
                isPulling={pullingClones.has(clone.path!)}
                isDeleting={deletingClones.has(clone.path!)}
                isMerged={branchInfo?.isMerged}
              />
            ))}
          </div>
        ) : (
          <BranchesEmptyState />
        )}
      </section>

      {/* Remote Branches Section */}
      {hasRemoteBranches && (
        <section className="space-y-4">
          <h2 className="flex items-center gap-2 text-lg font-semibold">
            <GitBranch className="text-muted-foreground h-5 w-5" />
            Remote Branches
            <span className="text-muted-foreground text-sm font-normal">
              ({remoteBranches.length})
            </span>
          </h2>
          <div className="grid gap-2">
            {remoteBranches.map((branch) => (
              <RemoteBranchRow
                key={branch.name}
                branch={branch}
                onCreateWorktree={() => handleCreateWorktree(branch)}
                isCreating={creatingBranches.has(branch.shortName!)}
              />
            ))}
          </div>
        </section>
      )}

      {/* Empty state when no worktrees and no remote branches */}
      {!hasLocalWorktrees && !hasRemoteBranches && (
        <BranchesEmptyState
          title="No branches found"
          description="This repository has no feature branches. Create a new branch to start working on a feature."
        />
      )}
    </div>
  )
}
