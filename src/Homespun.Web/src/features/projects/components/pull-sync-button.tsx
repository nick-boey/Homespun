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

  const isLoading = pullMutation.isPending || syncMutation.isPending

  const handlePull = () => {
    pullMutation.mutate()
  }

  const handleSync = () => {
    setIsDropdownOpen(false)
    syncMutation.mutate()
  }

  return (
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
  )
}
