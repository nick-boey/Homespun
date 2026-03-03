import { memo, useCallback } from 'react'
import { ExternalLink, Link2 } from 'lucide-react'
import { cn } from '@/lib/utils'
import { type PullRequestInfo } from '@/api'
import { PrStatusBadge } from './pr-status-badge'
import { CiStatusBadge } from './ci-status-badge'
import { ReviewStatusBadge } from './review-status-badge'

export interface PrRowProps {
  pr: PullRequestInfo
  linkedIssueId?: string | null
  isSelected?: boolean
  onSelect: (pr: PullRequestInfo) => void
  className?: string
}

/**
 * A row component for displaying a pull request in a list.
 */
export const PrRow = memo(function PrRow({
  pr,
  linkedIssueId,
  isSelected,
  onSelect,
  className,
}: PrRowProps) {
  const handleClick = useCallback(() => {
    onSelect(pr)
  }, [onSelect, pr])

  const handleKeyDown = useCallback(
    (e: React.KeyboardEvent) => {
      if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault()
        onSelect(pr)
      }
    },
    [onSelect, pr]
  )

  const handleLinkClick = useCallback((e: React.MouseEvent) => {
    e.stopPropagation()
  }, [])

  return (
    <button
      type="button"
      className={cn(
        // Touch-friendly tap target (min 44px height)
        'flex min-h-[56px] w-full items-center gap-3 rounded-lg border px-4 py-3 text-left transition-colors',
        'hover:bg-accent/50 focus:ring-ring focus:ring-2 focus:outline-none',
        // Active touch feedback
        'active:bg-accent/70',
        isSelected && 'bg-accent',
        className
      )}
      onClick={handleClick}
      onKeyDown={handleKeyDown}
    >
      {/* PR Number */}
      <span className="text-muted-foreground shrink-0 font-mono text-sm font-semibold">
        #{pr.number}
      </span>

      {/* Title and badges */}
      <div className="min-w-0 flex-1">
        <div className="mb-1 flex items-center gap-2">
          <span className="truncate font-medium">{pr.title}</span>
          {linkedIssueId && (
            <Link2
              className="text-muted-foreground h-4 w-4 shrink-0"
              aria-label="Has linked issue"
            />
          )}
        </div>
        <div className="flex flex-wrap items-center gap-2">
          <PrStatusBadge status={pr.status} size="sm" />
          <CiStatusBadge checksPassing={pr.checksPassing} size="sm" />
          <ReviewStatusBadge
            isApproved={pr.isApproved}
            approvalCount={pr.approvalCount}
            changesRequestedCount={pr.changesRequestedCount}
            size="sm"
          />
        </div>
      </div>

      {/* View on GitHub link - larger touch target */}
      <a
        href={pr.htmlUrl ?? '#'}
        target="_blank"
        rel="noopener noreferrer"
        className="text-muted-foreground hover:text-primary active:bg-accent/50 flex h-10 w-10 shrink-0 items-center justify-center rounded-md"
        aria-label="View on GitHub"
        onClick={handleLinkClick}
      >
        <ExternalLink className="h-4 w-4" />
      </a>
    </button>
  )
})
