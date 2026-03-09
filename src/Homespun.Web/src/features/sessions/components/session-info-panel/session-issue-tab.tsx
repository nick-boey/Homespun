import { FileText, GitMerge } from 'lucide-react'
import type { ClaudeSession } from '@/api/generated'
import { useIssue } from '@/features/issues/hooks/use-issue'
import { getStatusLabel, getStatusColorClass, getTypeLabel } from '@/lib/issue-constants'
import { cn } from '@/lib/utils'
import { Skeleton } from '@/components/ui/skeleton'
import { Button } from '@/components/ui/button'
import { useState } from 'react'
import { ApplyAgentChangesDialog } from '@/features/issues/components/apply-agent-changes-dialog'

interface SessionIssueTabProps {
  session: ClaudeSession
}

export function SessionIssueTab({ session }: SessionIssueTabProps) {
  const { issue, isLoading, isError } = useIssue(session.entityId || '', session.projectId || '')
  const [showApplyDialog, setShowApplyDialog] = useState(false)

  if (!session.entityId || !session.projectId) {
    return (
      <div className="text-muted-foreground flex flex-col items-center justify-center py-8">
        <FileText className="mb-3 h-12 w-12 opacity-50" />
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
      <div className="text-muted-foreground flex flex-col items-center justify-center py-8">
        <FileText className="mb-3 h-12 w-12 opacity-50" />
        <p>Failed to load issue details</p>
      </div>
    )
  }

  return (
    <div className="space-y-4">
      {/* Issue ID and Badges */}
      <div className="flex flex-wrap items-center gap-2">
        <span className="font-mono text-sm font-medium">{issue.id}</span>

        {/* Type badge */}
        <span className="shrink-0 rounded bg-blue-500/20 px-1.5 py-0.5 text-xs font-medium text-blue-700 dark:text-blue-400">
          {getTypeLabel(issue.type)}
        </span>

        {/* Status badge */}
        <span
          className={cn(
            'shrink-0 rounded px-1.5 py-0.5 text-xs font-medium',
            getStatusColorClass(issue.status)
          )}
        >
          {getStatusLabel(issue.status)}
        </span>

        {/* Priority badge if high */}
        {issue.priority === 0 && (
          <span className="shrink-0 rounded bg-orange-500/20 px-1.5 py-0.5 text-xs font-medium text-orange-700 dark:text-orange-400">
            High Priority
          </span>
        )}
      </div>

      {/* Title */}
      <h3 className="text-lg font-semibold">{issue.title}</h3>

      {/* Description */}
      {issue.description && (
        <div className="prose prose-sm dark:prose-invert max-w-none">
          <p className="text-muted-foreground text-sm whitespace-pre-wrap">{issue.description}</p>
        </div>
      )}

      {/* Parent Issue */}
      {issue.parentIssues && issue.parentIssues.length > 0 && (
        <div className="space-y-1">
          <p className="text-muted-foreground text-xs font-medium">Parent Issue</p>
          <p className="text-sm">{issue.parentIssues[0].parentIssue}</p>
        </div>
      )}

      {/* Branch Info */}
      {issue.workingBranchId && (
        <div className="space-y-1">
          <p className="text-muted-foreground text-xs font-medium">Branch</p>
          <p className="font-mono text-sm">{issue.workingBranchId}</p>
        </div>
      )}

      {/* Apply Changes Button */}
      {session.status !== 'active' && (
        <div className="mt-6 pt-4 border-t">
          <Button
            onClick={() => setShowApplyDialog(true)}
            className="w-full"
            variant="outline"
          >
            <GitMerge className="mr-2 h-4 w-4" />
            Apply Agent Changes
          </Button>
          <p className="text-xs text-muted-foreground mt-2">
            Apply changes made by the agent back to the main branch
          </p>
        </div>
      )}

      {/* Apply Changes Dialog */}
      {showApplyDialog && session.projectId && (
        <ApplyAgentChangesDialog
          open={showApplyDialog}
          onOpenChange={setShowApplyDialog}
          sessionId={session.id}
          projectId={session.projectId}
          issueId={issue.id}
          issueTitle={issue.title}
        />
      )}
    </div>
  )
}
