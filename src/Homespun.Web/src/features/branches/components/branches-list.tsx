import { useState } from 'react'
import { RefreshCw, GitBranch } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { BranchCard } from './branch-card'
import { BranchCardSkeleton } from './branch-card-skeleton'
import { RemoteBranchRow } from './remote-branch-row'
import { BranchesEmptyState } from './branches-empty-state'
import {
  useClones,
  useDeleteClone,
  usePullClone,
  useCreateClone,
} from '../hooks/use-clones'
import {
  useBranches,
  getRemoteOnlyBranches,
} from '../hooks/use-branches'
import type { BranchInfo, CloneInfo } from '@/api/generated/types.gen'

export interface BranchesListProps {
  projectId: string
  repoPath: string
}

export function BranchesList({ projectId, repoPath }: BranchesListProps) {
  const {
    data: clones,
    isLoading: clonesLoading,
    refetch: refetchClones,
  } = useClones(projectId)

  const {
    data: branches,
    isLoading: branchesLoading,
    refetch: refetchBranches,
  } = useBranches(repoPath)

  const deleteClone = useDeleteClone()
  const pullClone = usePullClone()
  const createClone = useCreateClone()

  const [pullingClones, setPullingClones] = useState<Set<string>>(new Set())
  const [deletingClones, setDeletingClones] = useState<Set<string>>(new Set())
  const [creatingBranches, setCreatingBranches] = useState<Set<string>>(new Set())
  const [isRefreshingAll, setIsRefreshingAll] = useState(false)

  const isLoading = clonesLoading || branchesLoading

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
        projectId,
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
      const pullPromises = (clones ?? [])
        .filter((c) => c.path)
        .map((clone) => handlePull(clone))
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
          <Button variant="outline" size="sm" disabled>
            <RefreshCw className="mr-2 h-4 w-4" />
            Refresh All
          </Button>
        </div>
        <div className="grid gap-3">
          <BranchCardSkeleton />
          <BranchCardSkeleton />
          <BranchCardSkeleton />
        </div>
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
              <span className="text-muted-foreground text-sm font-normal">
                ({clones.length})
              </span>
            )}
          </h2>
          <Button
            variant="outline"
            size="sm"
            onClick={handleRefreshAll}
            disabled={isRefreshingAll || !hasLocalWorktrees}
          >
            <RefreshCw
              className={`mr-2 h-4 w-4 ${isRefreshingAll ? 'animate-spin' : ''}`}
            />
            Refresh All
          </Button>
        </div>

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
