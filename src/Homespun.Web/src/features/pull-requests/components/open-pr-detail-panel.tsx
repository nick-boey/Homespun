import { memo, useCallback } from 'react'
import { X, ExternalLink, Play, GitBranch, GitMerge, AlertTriangle } from 'lucide-react'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/button'
import { Markdown } from '@/components/ui/markdown'
import { type PullRequestInfo } from '@/api'
import { PrStatusBadge } from './pr-status-badge'
import { CiStatusBadge } from './ci-status-badge'
import { ReviewStatusBadge } from './review-status-badge'

export interface OpenPrDetailPanelProps {
  pr: PullRequestInfo
  linkedIssueId?: string | null
  onClose?: () => void
  onViewIssue?: (issueId: string) => void
  onStartAgent?: (branchName: string) => void
  className?: string
}

/**
 * Detail panel for an open pull request.
 * Shows PR status, description, CI status, review status, and quick actions.
 */
export const OpenPrDetailPanel = memo(function OpenPrDetailPanel({
  pr,
  linkedIssueId,
  onClose,
  onViewIssue,
  onStartAgent,
  className,
}: OpenPrDetailPanelProps) {
  const handleViewIssue = useCallback(() => {
    if (linkedIssueId && onViewIssue) {
      onViewIssue(linkedIssueId)
    }
  }, [linkedIssueId, onViewIssue])

  const handleStartAgent = useCallback(() => {
    if (pr.branchName && onStartAgent) {
      onStartAgent(pr.branchName)
    }
  }, [pr.branchName, onStartAgent])

  return (
    <div
      className={cn(
        'bg-card text-card-foreground rounded-lg border p-4 shadow-sm',
        className
      )}
    >
      {/* Header */}
      <div className="mb-4 flex items-start justify-between gap-4">
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <span className="font-mono text-sm font-semibold">#{pr.number}</span>
            <h3 className="truncate text-lg font-medium">{pr.title}</h3>
          </div>
          <div className="mt-1 flex items-center gap-2 text-sm text-muted-foreground">
            <GitBranch className="h-4 w-4" />
            <code className="rounded bg-muted px-1.5 py-0.5 font-mono text-xs">
              {pr.branchName}
            </code>
          </div>
        </div>
        <Button
          variant="ghost"
          size="icon"
          onClick={onClose}
          aria-label="Close"
          className="shrink-0"
        >
          <X className="h-4 w-4" />
        </Button>
      </div>

      {/* Status badges */}
      <div className="mb-4 flex flex-wrap items-center gap-2">
        <PrStatusBadge status={pr.status} size="sm" />
        <CiStatusBadge checksPassing={pr.checksPassing} size="sm" />
        <ReviewStatusBadge
          isApproved={pr.isApproved}
          approvalCount={pr.approvalCount}
          changesRequestedCount={pr.changesRequestedCount}
          size="sm"
        />
        {pr.isMergeable === true && (
          <span className="inline-flex items-center gap-1 rounded bg-green-500/20 px-1.5 py-0.5 text-xs font-medium text-green-700 dark:text-green-400">
            <GitMerge className="h-3 w-3" />
            Ready to merge
          </span>
        )}
        {pr.isMergeable === false && (
          <span className="inline-flex items-center gap-1 rounded bg-red-500/20 px-1.5 py-0.5 text-xs font-medium text-red-700 dark:text-red-400">
            <AlertTriangle className="h-3 w-3" />
            Has conflicts
          </span>
        )}
      </div>

      {/* Description */}
      <div className="mb-4">
        <h4 className="text-muted-foreground mb-2 text-xs font-medium uppercase">
          Description
        </h4>
        {pr.body ? (
          <Markdown className="prose-sm max-w-none">{pr.body}</Markdown>
        ) : (
          <p className="text-muted-foreground text-sm italic">No description</p>
        )}
      </div>

      {/* Actions */}
      <div className="flex flex-wrap items-center gap-2">
        <a
          href={pr.htmlUrl ?? '#'}
          target="_blank"
          rel="noopener noreferrer"
          className="text-primary inline-flex items-center gap-1 text-sm hover:underline"
          aria-label="View on GitHub"
        >
          <ExternalLink className="h-3 w-3" />
          View on GitHub
        </a>

        {pr.branchName && onStartAgent && (
          <Button variant="outline" size="sm" onClick={handleStartAgent}>
            <Play className="mr-1 h-3 w-3" />
            Start Agent
          </Button>
        )}

        {linkedIssueId && onViewIssue && (
          <Button variant="ghost" size="sm" onClick={handleViewIssue}>
            View Linked Issue
          </Button>
        )}
      </div>
    </div>
  )
})
