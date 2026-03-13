import { GitBranch, GitCommit, AlertCircle, CheckCircle } from 'lucide-react'
import type { ClaudeSession } from '@/types/signalr'
import { useSessionBranchInfo } from '@/features/sessions/hooks'
import { Skeleton } from '@/components/ui/skeleton'
import { Badge } from '@/components/ui/badge'

function formatRelativeTime(dateString: string): string {
  const date = new Date(dateString)
  const now = new Date()
  const diffMs = now.getTime() - date.getTime()
  const diffSecs = Math.floor(diffMs / 1000)
  const diffMins = Math.floor(diffSecs / 60)
  const diffHours = Math.floor(diffMins / 60)
  const diffDays = Math.floor(diffHours / 24)

  if (diffDays > 0) {
    return `${diffDays} day${diffDays > 1 ? 's' : ''} ago`
  }
  if (diffHours > 0) {
    return `${diffHours} hour${diffHours > 1 ? 's' : ''} ago`
  }
  if (diffMins > 0) {
    return `${diffMins} minute${diffMins > 1 ? 's' : ''} ago`
  }
  return 'just now'
}

interface SessionBranchTabProps {
  session: ClaudeSession
}

export function SessionBranchTab({ session }: SessionBranchTabProps) {
  const { data, isLoading, isError, isCloneSession } = useSessionBranchInfo(session)

  if (!isCloneSession) {
    return (
      <div className="text-muted-foreground flex flex-col items-center justify-center py-8">
        <GitBranch className="mb-3 h-12 w-12 opacity-50" />
        <p className="font-medium">Branch info not available</p>
        <p className="mt-1 text-sm opacity-75">This is only available for clone sessions</p>
      </div>
    )
  }

  if (isLoading) {
    return (
      <div className="space-y-4">
        <div className="space-y-2">
          <Skeleton className="h-4 w-20" />
          <Skeleton className="h-6 w-48" />
        </div>
        <div className="space-y-2">
          <Skeleton className="h-4 w-16" />
          <Skeleton className="h-6 w-32" />
        </div>
        <div className="space-y-2">
          <Skeleton className="h-4 w-24" />
          <Skeleton className="h-6 w-full" />
        </div>
      </div>
    )
  }

  if (isError) {
    return (
      <div className="text-muted-foreground flex flex-col items-center justify-center py-8">
        <GitBranch className="mb-3 h-12 w-12 opacity-50" />
        <p>Failed to load branch info</p>
      </div>
    )
  }

  if (!data) {
    return (
      <div className="text-muted-foreground flex flex-col items-center justify-center py-8">
        <GitBranch className="mb-3 h-12 w-12 opacity-50" />
        <p>No branch info available</p>
      </div>
    )
  }

  const isDetachedHead = !data.branchName
  const hasAheadOrBehind = (data.aheadCount ?? 0) > 0 || (data.behindCount ?? 0) > 0
  const isUpToDate = !hasAheadOrBehind && !data.hasUncommittedChanges

  return (
    <div className="space-y-6">
      {/* Branch */}
      <div className="space-y-2">
        <div className="text-muted-foreground flex items-center gap-2 text-sm font-medium">
          <GitBranch className="h-4 w-4" />
          <span>Branch</span>
        </div>
        {isDetachedHead ? (
          <div className="flex items-center gap-2">
            <span className="font-mono text-sm text-yellow-600 dark:text-yellow-400">
              detached HEAD
            </span>
          </div>
        ) : (
          <p className="font-mono text-sm break-all">{data.branchName}</p>
        )}
      </div>

      {/* Commit */}
      <div className="space-y-2">
        <div className="text-muted-foreground flex items-center gap-2 text-sm font-medium">
          <GitCommit className="h-4 w-4" />
          <span>Latest Commit</span>
        </div>
        <div className="bg-muted/50 rounded-lg border p-3">
          <div className="flex items-start justify-between gap-2">
            <p className="text-primary font-mono text-sm">{data.commitSha}</p>
            {data.commitDate && (
              <span className="text-muted-foreground text-xs whitespace-nowrap">
                {formatRelativeTime(data.commitDate)}
              </span>
            )}
          </div>
          {data.commitMessage && (
            <p className="text-muted-foreground mt-2 text-sm">{data.commitMessage}</p>
          )}
        </div>
      </div>

      {/* Status */}
      <div className="space-y-3">
        <div className="text-muted-foreground text-sm font-medium">Status</div>

        {/* Ahead/Behind badges */}
        <div className="flex flex-wrap items-center gap-2">
          {(data.aheadCount ?? 0) > 0 && (
            <Badge
              variant="secondary"
              className="bg-green-500/10 text-green-600 dark:text-green-400"
            >
              {data.aheadCount} ahead
            </Badge>
          )}
          {(data.behindCount ?? 0) > 0 && (
            <Badge
              variant="secondary"
              className="bg-yellow-500/10 text-yellow-600 dark:text-yellow-400"
            >
              {data.behindCount} behind
            </Badge>
          )}
          {isUpToDate && (
            <div className="flex items-center gap-1.5 text-sm text-green-600 dark:text-green-400">
              <CheckCircle className="h-4 w-4" />
              <span>Up to date with upstream</span>
            </div>
          )}
        </div>

        {/* Uncommitted changes warning */}
        {data.hasUncommittedChanges && (
          <div className="flex items-center gap-2 rounded-lg bg-yellow-500/10 p-3 text-sm text-yellow-600 dark:text-yellow-400">
            <AlertCircle className="h-4 w-4 shrink-0" />
            <span>Uncommitted changes in working directory</span>
          </div>
        )}
      </div>
    </div>
  )
}
