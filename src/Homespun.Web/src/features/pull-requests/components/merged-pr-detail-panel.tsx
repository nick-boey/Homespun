import { memo, useCallback } from 'react'
import { X, ExternalLink, Clock, Link2 } from 'lucide-react'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/button'
import { Markdown } from '@/components/ui/markdown'
import { type PullRequestInfo, type IssueResponse } from '@/api'
import { PrStatusBadge } from './pr-status-badge'

export interface MergedPrDetailPanelProps {
  pr: PullRequestInfo
  linkedIssue?: IssueResponse | null
  timeSpentMinutes?: number
  onClose?: () => void
  onViewIssue?: (issueId: string) => void
  className?: string
}

/**
 * Formats minutes into a human-readable time string.
 */
function formatTimeSpent(minutes: number): string {
  const hours = Math.floor(minutes / 60)
  const mins = minutes % 60

  if (hours > 0 && mins > 0) {
    return `${hours}h ${mins}m`
  } else if (hours > 0) {
    return `${hours}h 0m`
  } else {
    return `${mins}m`
  }
}

/**
 * Formats a date string to a localized date/time.
 */
function formatDate(dateString?: string | null): string {
  if (!dateString) return 'Unknown'
  return new Date(dateString).toLocaleDateString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

/**
 * Detail panel for a merged pull request.
 * Shows PR status, description, merge info, linked issue, and time spent.
 */
export const MergedPrDetailPanel = memo(function MergedPrDetailPanel({
  pr,
  linkedIssue,
  timeSpentMinutes,
  onClose,
  onViewIssue,
  className,
}: MergedPrDetailPanelProps) {
  const handleViewIssue = useCallback(() => {
    if (linkedIssue?.id && onViewIssue) {
      onViewIssue(linkedIssue.id)
    }
  }, [linkedIssue, onViewIssue])

  return (
    <div className={cn('bg-card text-card-foreground rounded-lg border p-4 shadow-sm', className)}>
      {/* Header */}
      <div className="mb-4 flex items-start justify-between gap-4">
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            <span className="font-mono text-sm font-semibold">#{pr.number}</span>
            <h3 className="truncate text-lg font-medium">{pr.title}</h3>
            <PrStatusBadge status={pr.status} size="sm" />
          </div>
          <p className="text-muted-foreground mt-1 text-sm">Merged on {formatDate(pr.mergedAt)}</p>
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

      {/* Time spent */}
      {timeSpentMinutes !== undefined && timeSpentMinutes > 0 && (
        <div className="text-muted-foreground mb-4 flex items-center gap-2 text-sm">
          <Clock className="h-4 w-4" />
          <span>Time spent: {formatTimeSpent(timeSpentMinutes)}</span>
        </div>
      )}

      {/* Description */}
      <div className="mb-4">
        <h4 className="text-muted-foreground mb-2 text-xs font-medium uppercase">Description</h4>
        {pr.body ? (
          <Markdown className="prose-sm max-w-none">{pr.body}</Markdown>
        ) : (
          <p className="text-muted-foreground text-sm italic">No description</p>
        )}
      </div>

      {/* Linked Issue */}
      {linkedIssue && (
        <div className="mb-4">
          <h4 className="text-muted-foreground mb-2 text-xs font-medium uppercase">Linked Issue</h4>
          <button
            type="button"
            className="text-primary inline-flex items-center gap-1 text-sm hover:underline"
            onClick={handleViewIssue}
          >
            <Link2 className="h-3 w-3" />
            {linkedIssue.title}
          </button>
        </div>
      )}

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
      </div>
    </div>
  )
})
