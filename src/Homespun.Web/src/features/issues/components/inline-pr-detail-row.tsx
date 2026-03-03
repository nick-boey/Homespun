/**
 * InlinePrDetailRow - Expanded inline details for a merged/closed PR in the task graph.
 */

import { memo, useCallback } from 'react'
import { X, ExternalLink, GitCommit } from 'lucide-react'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/button'
import { Markdown } from '@/components/ui/markdown'
import type { TaskGraphPrRenderLine } from '../services'

export interface PrCommit {
  sha: string
  message: string
}

export interface RelatedIssue {
  id: string
  title: string
}

export interface InlinePrDetailRowProps {
  line: TaskGraphPrRenderLine
  maxLanes: number
  description?: string | null
  commits?: PrCommit[]
  relatedIssues?: RelatedIssue[]
  onClose?: () => void
  onViewIssue?: (issueId: string) => void
}

/**
 * Inline expanded details panel for a merged/closed PR.
 * Shows PR metadata, description, commits list, and related issues.
 */
export const InlinePrDetailRow = memo(function InlinePrDetailRow({
  line,
  maxLanes,
  description,
  commits = [],
  relatedIssues = [],
  onClose,
  onViewIssue,
}: InlinePrDetailRowProps) {
  // Calculate left padding to align with content (after SVG)
  const svgWidth = 24 * Math.max(maxLanes, 1) + 12

  const handleViewIssue = useCallback(
    (issueId: string) => {
      onViewIssue?.(issueId)
    },
    [onViewIssue]
  )

  return (
    <div
      className={cn(
        'animate-expand bg-muted/30 border-muted overflow-hidden border-t px-3 py-4',
        'transition-all duration-200 ease-out'
      )}
      style={{ marginLeft: svgWidth }}
    >
      {/* Header with PR info and close button */}
      <div className="mb-3 flex flex-wrap items-center gap-2">
        {/* PR number */}
        <span className="font-mono text-sm font-medium">#{line.prNumber}</span>

        {/* PR title */}
        <span className="truncate text-sm font-medium">{line.title}</span>

        {/* Status badge */}
        <span
          className={cn(
            'shrink-0 rounded px-1.5 py-0.5 text-xs font-medium',
            line.isMerged
              ? 'bg-purple-500/20 text-purple-700 dark:text-purple-400'
              : 'bg-red-500/20 text-red-700 dark:text-red-400'
          )}
        >
          {line.isMerged ? 'Merged' : 'Closed'}
        </span>

        {/* Spacer */}
        <div className="flex-1" />

        {/* Close button */}
        <Button
          variant="ghost"
          size="sm"
          className="h-6 w-6 p-0"
          onClick={onClose}
          aria-label="Close"
        >
          <X className="h-4 w-4" />
        </Button>
      </div>

      {/* Description */}
      <div className="mb-4">
        {description ? (
          <Markdown className="text-foreground prose-sm max-w-none">{description}</Markdown>
        ) : (
          <p className="text-muted-foreground text-sm italic">No description</p>
        )}
      </div>

      {/* Commits list */}
      <div className="mb-4">
        <h4 className="text-muted-foreground mb-2 text-xs font-medium uppercase">Commits</h4>
        {commits.length > 0 ? (
          <ul className="space-y-1">
            {commits.map((commit) => (
              <li key={commit.sha} className="flex items-start gap-2 text-sm">
                <GitCommit className="text-muted-foreground mt-0.5 h-3 w-3 shrink-0" />
                <code className="bg-muted shrink-0 rounded px-1 py-0.5 font-mono text-xs">
                  {commit.sha.substring(0, 7)}
                </code>
                <span className="text-foreground truncate">{commit.message}</span>
              </li>
            ))}
          </ul>
        ) : (
          <p className="text-muted-foreground text-sm italic">No commits</p>
        )}
      </div>

      {/* Related issues */}
      {relatedIssues.length > 0 && (
        <div className="mb-4">
          <h4 className="text-muted-foreground mb-2 text-xs font-medium uppercase">
            Related Issues
          </h4>
          <ul className="space-y-1">
            {relatedIssues.map((issue) => (
              <li key={issue.id}>
                <button
                  type="button"
                  className="text-primary text-sm hover:underline"
                  onClick={() => handleViewIssue(issue.id)}
                >
                  {issue.title}
                </button>
              </li>
            ))}
          </ul>
        </div>
      )}

      {/* View on GitHub link */}
      {line.url && (
        <a
          href={line.url}
          target="_blank"
          rel="noopener noreferrer"
          className="text-primary inline-flex items-center gap-1 text-sm hover:underline"
          aria-label="View on GitHub"
        >
          View on GitHub
          <ExternalLink className="h-3 w-3" />
        </a>
      )}
    </div>
  )
})
