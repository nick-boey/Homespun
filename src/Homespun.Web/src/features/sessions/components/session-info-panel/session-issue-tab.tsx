import { FileText } from 'lucide-react'
import type { ClaudeSession } from '@/api/generated'
import { useIssue } from '@/features/issues/hooks/use-issue'
import { getStatusLabel, getStatusColorClass, getTypeLabel } from '@/lib/issue-constants'
import { cn } from '@/lib/utils'
import { Skeleton } from '@/components/ui/skeleton'

interface SessionIssueTabProps {
  session: ClaudeSession
}

export function SessionIssueTab({ session }: SessionIssueTabProps) {
  const { issue, isLoading, isError } = useIssue(
    session.entityId || '',
    session.projectId || ''
  )

  if (!session.entityId || !session.projectId) {
    return (
      <div className="flex flex-col items-center justify-center py-8 text-muted-foreground">
        <FileText className="h-12 w-12 mb-3 opacity-50" />
        <p>No issue linked to this session</p>
      </div>
    )
  }

  if (isLoading) {
    return (
      <div className="space-y-3">
        <Skeleton className="h-4 w-24" />
        <Skeleton className="h-8 w-full" />
        <Skeleton className="h-4 w-32" />
        <Skeleton className="h-20 w-full" />
      </div>
    )
  }

  if (isError || !issue) {
    return (
      <div className="flex flex-col items-center justify-center py-8 text-muted-foreground">
        <FileText className="h-12 w-12 mb-3 opacity-50" />
        <p>Failed to load issue details</p>
      </div>
    )
  }

  return (
    <div className="space-y-4">
      {/* Issue ID and Badges */}
      <div className="flex flex-wrap items-center gap-2">
        <span className="font-mono text-sm font-medium">{issue.issueId}</span>

        {/* Type badge */}
        <span className="shrink-0 rounded px-1.5 py-0.5 text-xs font-medium bg-blue-500/20 text-blue-700 dark:text-blue-400">
          {getTypeLabel(issue.type)}
        </span>

        {/* Status badge */}
        <span className={cn(
          'shrink-0 rounded px-1.5 py-0.5 text-xs font-medium',
          getStatusColorClass(issue.status)
        )}>
          {getStatusLabel(issue.status)}
        </span>

        {/* Priority badge if high */}
        {issue.priority === 0 && (
          <span className="shrink-0 rounded px-1.5 py-0.5 text-xs font-medium bg-orange-500/20 text-orange-700 dark:text-orange-400">
            High Priority
          </span>
        )}
      </div>

      {/* Title */}
      <h3 className="text-lg font-semibold">
        {issue.title}
      </h3>

      {/* Description */}
      {issue.description && (
        <div className="prose prose-sm dark:prose-invert max-w-none">
          <p className="text-sm text-muted-foreground whitespace-pre-wrap">
            {issue.description}
          </p>
        </div>
      )}

      {/* Parent Issue */}
      {issue.parentIssues && issue.parentIssues.length > 0 && (
        <div className="space-y-1">
          <p className="text-xs font-medium text-muted-foreground">Parent Issue</p>
          <p className="text-sm">
            {issue.parentIssues[0].issueId}: {issue.parentIssues[0].title}
          </p>
        </div>
      )}

      {/* Branch Info */}
      {issue.branchInfo && (
        <div className="space-y-1">
          <p className="text-xs font-medium text-muted-foreground">Branch</p>
          <p className="text-sm font-mono">{issue.branchInfo.name}</p>
        </div>
      )}
    </div>
  )
}