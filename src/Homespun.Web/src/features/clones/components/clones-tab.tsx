import { useState, useMemo } from 'react'
import { GitBranch, Trash2, ListTodo } from 'lucide-react'
import { Button } from '@/components/ui/button'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from '@/components/ui/alert-dialog'
import { EnrichedCloneCard } from './enriched-clone-card'
import { ClonesEmptyState } from './clones-empty-state'
import { ClonesSkeleton } from './clones-skeleton'
import { ErrorFallback } from '@/components/error-boundary'
import { useEnrichedClones } from '../hooks/use-enriched-clones'
import { useBulkDeleteClones } from '../hooks/use-bulk-delete-clones'
import { useDeleteClone } from '@/features/branches/hooks/use-clones'
import type { EnrichedCloneInfo } from '@/api/generated/types.gen'

export interface ClonesTabProps {
  projectId: string
}

export function ClonesTab({ projectId }: ClonesTabProps) {
  const { data: clones, isLoading, isError, error, refetch } = useEnrichedClones(projectId)

  const deleteClone = useDeleteClone()
  const bulkDelete = useBulkDeleteClones()

  const [deletingClones, setDeletingClones] = useState<Set<string>>(new Set())

  // Separate clones into two categories
  const { featureClones, issuesAgentClones } = useMemo(() => {
    if (!clones) return { featureClones: [], issuesAgentClones: [] }

    return {
      featureClones: clones.filter((c) => !c.isIssuesAgentClone),
      issuesAgentClones: clones.filter((c) => c.isIssuesAgentClone),
    }
  }, [clones])

  // Get deletable clones
  const deletableClones = useMemo(() => {
    return clones?.filter((c) => c.isDeletable) ?? []
  }, [clones])

  const handleDeleteSingle = async (clone: EnrichedCloneInfo) => {
    if (!clone.clone.path) return
    setDeletingClones((prev) => new Set(prev).add(clone.clone.path!))
    try {
      await deleteClone.mutateAsync({
        projectId,
        clonePath: clone.clone.path,
      })
    } finally {
      setDeletingClones((prev) => {
        const next = new Set(prev)
        next.delete(clone.clone.path!)
        return next
      })
    }
  }

  const handleDeleteAll = async () => {
    const paths = deletableClones.map((c) => c.clone.path).filter((p): p is string => !!p)

    await bulkDelete.mutateAsync({
      projectId,
      clonePaths: paths,
    })
  }

  if (isLoading) {
    return <ClonesSkeleton />
  }

  if (isError) {
    return (
      <div className="space-y-6">
        <h2 className="text-lg font-semibold">Clones</h2>
        <ErrorFallback
          error={error}
          title="Failed to load clones"
          description="Unable to fetch clone data. Please try again."
          variant="inline"
          onRetry={() => refetch()}
        />
      </div>
    )
  }

  const hasClones = clones && clones.length > 0

  return (
    <div className="space-y-8">
      {/* Header with Delete All button */}
      <div className="flex items-center justify-between">
        <h2 className="text-lg font-semibold">Clones</h2>
        {deletableClones.length > 0 && (
          <AlertDialog>
            <AlertDialogTrigger asChild>
              <Button variant="destructive" size="sm" disabled={bulkDelete.isPending}>
                <Trash2 className="mr-2 h-4 w-4" />
                Delete All Stale ({deletableClones.length})
              </Button>
            </AlertDialogTrigger>
            <AlertDialogContent>
              <AlertDialogHeader>
                <AlertDialogTitle>Delete All Stale Clones</AlertDialogTitle>
                <AlertDialogDescription>
                  Are you sure you want to delete {deletableClones.length} stale clone(s)? This will
                  permanently remove their clone folders.
                  <br />
                  <br />
                  Clones are considered stale when:
                  <ul className="mt-2 list-disc pl-4">
                    <li>Their linked PR is merged or closed</li>
                    <li>Their linked issue is complete, archived, closed, or deleted</li>
                  </ul>
                </AlertDialogDescription>
              </AlertDialogHeader>
              <AlertDialogFooter>
                <AlertDialogCancel>Cancel</AlertDialogCancel>
                <AlertDialogAction onClick={handleDeleteAll}>Delete All</AlertDialogAction>
              </AlertDialogFooter>
            </AlertDialogContent>
          </AlertDialog>
        )}
      </div>

      {!hasClones && <ClonesEmptyState />}

      {/* Feature Clones Section */}
      {featureClones.length > 0 && (
        <section className="space-y-4">
          <h3 className="flex items-center gap-2 font-medium">
            <GitBranch className="h-5 w-5" />
            Feature Clones
            <span className="text-muted-foreground text-sm font-normal">
              ({featureClones.length})
            </span>
          </h3>
          <div className="grid gap-3">
            {featureClones.map((clone) => (
              <EnrichedCloneCard
                key={clone.clone.path}
                clone={clone}
                projectId={projectId}
                onDelete={() => handleDeleteSingle(clone)}
                isDeleting={deletingClones.has(clone.clone.path!)}
              />
            ))}
          </div>
        </section>
      )}

      {/* Issues Agent Clones Section */}
      {issuesAgentClones.length > 0 && (
        <section className="space-y-4">
          <h3 className="flex items-center gap-2 font-medium">
            <ListTodo className="h-5 w-5" />
            Issues Agent Clones
            <span className="text-muted-foreground text-sm font-normal">
              ({issuesAgentClones.length})
            </span>
          </h3>
          <div className="grid gap-3">
            {issuesAgentClones.map((clone) => (
              <EnrichedCloneCard
                key={clone.clone.path}
                clone={clone}
                projectId={projectId}
                onDelete={() => handleDeleteSingle(clone)}
                isDeleting={deletingClones.has(clone.clone.path!)}
              />
            ))}
          </div>
        </section>
      )}
    </div>
  )
}
