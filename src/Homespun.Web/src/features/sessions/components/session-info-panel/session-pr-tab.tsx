import { GitPullRequest, CheckCircle, X, Loader, ExternalLink, AlertCircle } from 'lucide-react'
import type { ClaudeSession } from '@/types/signalr'
import { useIssuePrStatus } from '@/features/sessions/hooks'
import { Skeleton } from '@/components/ui/skeleton'
import { PullRequestStatus } from '@/api'

interface SessionPrTabProps {
  session: ClaudeSession
}

export function SessionPrTab({ session }: SessionPrTabProps) {
  const { data: prStatus, isLoading, isError } = useIssuePrStatus(session)

  // Only show PR tab for clone entities
  const isClone = session.entityId?.startsWith('clone:')
  if (!isClone) {
    return (
      <div className="text-muted-foreground flex flex-col items-center justify-center py-8">
        <GitPullRequest className="mb-3 h-12 w-12 opacity-50" />
        <p>No PR available for this session</p>
      </div>
    )
  }

  if (isLoading) {
    return (
      <div className="space-y-4">
        <Skeleton className="h-4 w-24" />
        <Skeleton className="h-8 w-full" />
        <Skeleton className="h-4 w-32" />
        <div className="space-y-2">
          <Skeleton className="h-6 w-40" />
          <Skeleton className="h-6 w-40" />
        </div>
      </div>
    )
  }

  if (isError || !prStatus) {
    return (
      <div className="text-muted-foreground flex flex-col items-center justify-center py-8">
        <GitPullRequest className="mb-3 h-12 w-12 opacity-50" />
        <p>Failed to load PR details</p>
      </div>
    )
  }

  // If no PR exists yet
  if (!prStatus.prNumber) {
    return (
      <div className="text-muted-foreground flex flex-col items-center justify-center py-8">
        <GitPullRequest className="mb-3 h-12 w-12 opacity-50" />
        <p>No pull request created yet</p>
        {prStatus.branchName && (
          <p className="mt-2 font-mono text-xs">Branch: {prStatus.branchName}</p>
        )}
      </div>
    )
  }

  // Determine PR status
  const getStatusBadge = () => {
    if (prStatus.status === PullRequestStatus.MERGED) {
      return (
        <span className="rounded bg-purple-500/20 px-2 py-1 text-xs font-medium text-purple-700 dark:text-purple-400">
          Merged
        </span>
      )
    }
    if (prStatus.status === PullRequestStatus.CLOSED) {
      return (
        <span className="rounded bg-gray-500/20 px-2 py-1 text-xs font-medium text-gray-700 dark:text-gray-400">
          Closed
        </span>
      )
    }
    if (prStatus.isMergeable && prStatus.checksPassing && prStatus.isApproved) {
      return (
        <span className="rounded bg-green-500/20 px-2 py-1 text-xs font-medium text-green-700 dark:text-green-400">
          Ready to Merge
        </span>
      )
    }
    if (prStatus.hasConflicts) {
      return (
        <span className="rounded bg-red-500/20 px-2 py-1 text-xs font-medium text-red-700 dark:text-red-400">
          Conflicts
        </span>
      )
    }
    if (prStatus.checksRunning) {
      return (
        <span className="rounded bg-yellow-500/20 px-2 py-1 text-xs font-medium text-yellow-700 dark:text-yellow-400">
          Checks Running
        </span>
      )
    }
    if (prStatus.checksFailing) {
      return (
        <span className="rounded bg-red-500/20 px-2 py-1 text-xs font-medium text-red-700 dark:text-red-400">
          Checks Failed
        </span>
      )
    }
    return (
      <span className="rounded bg-blue-500/20 px-2 py-1 text-xs font-medium text-blue-700 dark:text-blue-400">
        Open
      </span>
    )
  }

  return (
    <div className="space-y-4">
      {/* PR Number and Link */}
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-2">
          <span className="text-lg font-semibold">#{prStatus.prNumber}</span>
          {getStatusBadge()}
        </div>
        {prStatus.prUrl && (
          <a
            href={prStatus.prUrl}
            target="_blank"
            rel="noopener noreferrer"
            className="text-primary hover:text-primary/80 flex items-center gap-1 text-sm"
          >
            View on GitHub
            <ExternalLink className="h-3 w-3" />
          </a>
        )}
      </div>

      {/* Branch Name */}
      {prStatus.branchName && (
        <div className="space-y-1">
          <p className="text-muted-foreground text-xs font-medium">Branch</p>
          <p className="font-mono text-sm">{prStatus.branchName}</p>
        </div>
      )}

      {/* Checks Status */}
      <div className="space-y-2">
        <p className="text-muted-foreground text-xs font-medium">Status Checks</p>
        <div className="flex items-center gap-2">
          {prStatus.checksRunning && (
            <div className="flex items-center gap-1 text-sm">
              <Loader className="h-4 w-4 animate-spin text-yellow-600 dark:text-yellow-400" />
              <span className="text-yellow-700 dark:text-yellow-400">Running</span>
            </div>
          )}
          {prStatus.checksPassing && !prStatus.checksRunning && (
            <div className="flex items-center gap-1 text-sm">
              <CheckCircle className="h-4 w-4 text-green-600 dark:text-green-400" />
              <span className="text-green-700 dark:text-green-400">Passing</span>
            </div>
          )}
          {prStatus.checksFailing && (
            <div className="flex items-center gap-1 text-sm">
              <X className="h-4 w-4 text-red-600 dark:text-red-400" />
              <span className="text-red-700 dark:text-red-400">Failed</span>
            </div>
          )}
        </div>
      </div>

      {/* Approval Status */}
      <div className="space-y-2">
        <p className="text-muted-foreground text-xs font-medium">Reviews</p>
        {prStatus.isApproved ? (
          <div className="flex items-center gap-1 text-sm">
            <CheckCircle className="h-4 w-4 text-green-600 dark:text-green-400" />
            <span className="text-green-700 dark:text-green-400">
              Approved ({prStatus.approvalCount || 0})
            </span>
          </div>
        ) : (
          <div className="text-muted-foreground text-sm">
            {prStatus.approvalCount || 0} approvals
            {(prStatus.changesRequestedCount || 0) > 0 && (
              <span className="ml-2 text-orange-600 dark:text-orange-400">
                ({prStatus.changesRequestedCount} changes requested)
              </span>
            )}
          </div>
        )}
      </div>

      {/* Merge Conflicts */}
      {prStatus.hasConflicts && (
        <div className="rounded-lg border border-red-500/20 bg-red-500/10 p-3">
          <div className="flex items-center gap-2">
            <AlertCircle className="h-4 w-4 text-red-600 dark:text-red-400" />
            <p className="text-sm font-medium text-red-700 dark:text-red-400">
              This PR has merge conflicts
            </p>
          </div>
        </div>
      )}

      {/* Mergeable State */}
      {prStatus.mergeableState && (
        <div className="space-y-1">
          <p className="text-muted-foreground text-xs font-medium">Mergeable State</p>
          <p className="text-sm">{prStatus.mergeableState}</p>
        </div>
      )}
    </div>
  )
}
