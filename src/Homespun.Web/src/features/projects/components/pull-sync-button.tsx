import { useState } from 'react'
import { Cloud, ChevronDown, RefreshCw, Loader2 } from 'lucide-react'
import { toast } from 'sonner'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import { FleeceIssueSync, PullRequests } from '@/api'
import { taskGraphQueryKey } from '@/features/issues/hooks/use-task-graph'
import { openPullRequestsQueryKey } from '@/features/pull-requests/hooks/use-open-pull-requests'
import { mergedPullRequestsQueryKey } from '@/features/pull-requests/hooks/use-merged-pull-requests'

interface PullSyncButtonProps {
  projectId: string
}

export function PullSyncButton({ projectId }: PullSyncButtonProps) {
  const queryClient = useQueryClient()
  const [isDropdownOpen, setIsDropdownOpen] = useState(false)
  const [conflictFiles, setConflictFiles] = useState<string[] | null>(null)

  const invalidateQueries = () => {
    queryClient.invalidateQueries({ queryKey: taskGraphQueryKey(projectId) })
    queryClient.invalidateQueries({ queryKey: openPullRequestsQueryKey(projectId) })
    queryClient.invalidateQueries({ queryKey: mergedPullRequestsQueryKey(projectId) })
  }

  const pullMutation = useMutation({
    mutationFn: async () => {
      const [fleeceResponse, prResponse] = await Promise.all([
        FleeceIssueSync.postApiFleeceSyncByProjectIdPull({
          path: { projectId },
        }),
        PullRequests.postApiProjectsByProjectIdSync({
          path: { projectId },
        }),
      ])

      if (fleeceResponse.error || !fleeceResponse.data) {
        throw new Error(fleeceResponse.error?.detail ?? 'Failed to pull fleece issues')
      }

      return {
        fleece: fleeceResponse.data,
        prs: prResponse.data,
      }
    },
    onSuccess: (data) => {
      invalidateQueries()

      // Check for soft failure with non-fleece conflicts
      if (!data.fleece.success && data.fleece.hasNonFleeceChanges) {
        setConflictFiles(data.fleece.nonFleeceChangedFiles ?? [])
        return
      }

      const messages: string[] = []

      if (data.fleece.wasBehindRemote) {
        messages.push(`Pulled ${data.fleece.commitsPulled ?? 0} commit(s)`)
        if (data.fleece.issuesMerged && data.fleece.issuesMerged > 0) {
          messages.push(`Merged ${data.fleece.issuesMerged} issue(s)`)
        }
      } else {
        messages.push('Already up to date')
      }

      if (data.prs) {
        if (data.prs.imported && data.prs.imported > 0) {
          messages.push(`Imported ${data.prs.imported} PR(s)`)
        }
        if (data.prs.updated && data.prs.updated > 0) {
          messages.push(`Updated ${data.prs.updated} PR(s)`)
        }
      }

      toast.success('Pull complete', {
        description: messages.join('. '),
      })
    },
    onError: (error) => {
      toast.error('Pull failed', {
        description: error instanceof Error ? error.message : 'An error occurred',
      })
    },
  })

  const discardAndPullMutation = useMutation({
    mutationFn: async () => {
      const [fleeceResponse, prResponse] = await Promise.all([
        FleeceIssueSync.postApiFleeceSyncByProjectIdDiscardNonFleeceAndPull({
          path: { projectId },
        }),
        PullRequests.postApiProjectsByProjectIdSync({
          path: { projectId },
        }),
      ])

      if (fleeceResponse.error || !fleeceResponse.data) {
        throw new Error(fleeceResponse.error?.detail ?? 'Failed to discard and pull')
      }

      return {
        fleece: fleeceResponse.data,
        prs: prResponse.data,
      }
    },
    onSuccess: (data) => {
      setConflictFiles(null)
      invalidateQueries()

      const messages: string[] = []
      if (data.fleece.wasBehindRemote) {
        messages.push(`Pulled ${data.fleece.commitsPulled ?? 0} commit(s)`)
      } else {
        messages.push('Already up to date')
      }

      toast.success('Pull complete', {
        description: messages.join('. '),
      })
    },
    onError: (error) => {
      setConflictFiles(null)
      toast.error('Failed to discard changes and pull', {
        description: error instanceof Error ? error.message : 'An error occurred',
      })
    },
  })

  const syncMutation = useMutation({
    mutationFn: async () => {
      const [fleeceResponse, prResponse] = await Promise.all([
        FleeceIssueSync.postApiFleeceSyncByProjectIdSync({
          path: { projectId },
        }),
        PullRequests.postApiProjectsByProjectIdSync({
          path: { projectId },
        }),
      ])

      if (fleeceResponse.error || !fleeceResponse.data) {
        throw new Error(fleeceResponse.error?.detail ?? 'Failed to sync fleece issues')
      }

      return {
        fleece: fleeceResponse.data,
        prs: prResponse.data,
      }
    },
    onSuccess: (data) => {
      invalidateQueries()

      const messages: string[] = []

      if (data.fleece.filesCommitted && data.fleece.filesCommitted > 0) {
        messages.push(`Committed ${data.fleece.filesCommitted} file(s)`)
      }

      if (data.fleece.pushSucceeded) {
        messages.push('Pushed to remote')
      } else if (data.fleece.filesCommitted === 0) {
        messages.push('No changes to push')
      }

      if (data.prs) {
        if (data.prs.imported && data.prs.imported > 0) {
          messages.push(`Imported ${data.prs.imported} PR(s)`)
        }
        if (data.prs.updated && data.prs.updated > 0) {
          messages.push(`Updated ${data.prs.updated} PR(s)`)
        }
      }

      toast.success('Sync complete', {
        description: messages.join('. '),
      })
    },
    onError: (error) => {
      toast.error('Sync failed', {
        description: error instanceof Error ? error.message : 'An error occurred',
      })
    },
  })

  const isLoading =
    pullMutation.isPending || syncMutation.isPending || discardAndPullMutation.isPending

  const handlePull = () => {
    pullMutation.mutate()
  }

  const handleSync = () => {
    setIsDropdownOpen(false)
    syncMutation.mutate()
  }

  return (
    <>
      <div className="flex">
        <Button
          variant="outline"
          size="sm"
          onClick={handlePull}
          disabled={isLoading}
          className="rounded-r-none border-r-0"
          aria-label="Pull"
        >
          {isLoading ? <Loader2 className="h-4 w-4 animate-spin" /> : <Cloud className="h-4 w-4" />}
          <span className="ml-1">Pull</span>
        </Button>
        <DropdownMenu open={isDropdownOpen} onOpenChange={setIsDropdownOpen}>
          <DropdownMenuTrigger asChild>
            <Button
              variant="outline"
              size="sm"
              disabled={isLoading}
              className="rounded-l-none px-2"
              aria-label="More sync options"
            >
              <ChevronDown className="h-4 w-4" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            <DropdownMenuItem onClick={handleSync} disabled={isLoading}>
              <RefreshCw className="mr-2 h-4 w-4" />
              Sync
            </DropdownMenuItem>
          </DropdownMenuContent>
        </DropdownMenu>
      </div>

      <AlertDialog
        open={conflictFiles !== null}
        onOpenChange={(open) => !open && setConflictFiles(null)}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Uncommitted changes conflict</AlertDialogTitle>
            <AlertDialogDescription asChild>
              <div>
                <p className="mb-2">
                  The pull failed because the following uncommitted files conflict with incoming
                  changes:
                </p>
                <ul className="mb-2 list-inside list-disc text-sm">
                  {conflictFiles?.map((file) => (
                    <li key={file} className="font-mono text-xs">
                      {file}
                    </li>
                  ))}
                </ul>
                <p>
                  You can discard these changes and retry the pull, or cancel to resolve them
                  manually.
                </p>
              </div>
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction
              onClick={() => discardAndPullMutation.mutate()}
              disabled={discardAndPullMutation.isPending}
            >
              {discardAndPullMutation.isPending ? 'Discarding...' : 'Discard Changes & Retry'}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  )
}
