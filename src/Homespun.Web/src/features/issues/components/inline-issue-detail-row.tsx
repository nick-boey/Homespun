/**
 * InlineIssueDetailRow - Expanded inline details for an issue in the task graph.
 */

import { memo, useCallback, useState } from 'react'
import { Copy, Pencil, Play, X, ExternalLink } from 'lucide-react'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/button'
import { Markdown } from '@/components/ui/markdown'
import type { TaskGraphIssueRenderLine } from '../services'
import { getTypeColor } from './task-graph-svg'

/** Issue status labels */
const STATUS_LABELS: Record<number, string> = {
  0: 'Open',
  1: 'Complete',
  2: 'Closed',
  3: 'Archived',
  4: 'Progress',
  5: 'Review',
  6: 'Blocked',
}

/** Issue type labels */
const TYPE_LABELS: Record<number, string> = {
  0: 'Task',
  1: 'Feature',
  2: 'Bug',
  3: 'Chore',
  4: 'Epic',
}

/** Status color variants */
const STATUS_COLORS: Record<number, string> = {
  0: 'bg-blue-500/20 text-blue-700 dark:text-blue-400', // Open
  1: 'bg-green-500/20 text-green-700 dark:text-green-400', // Complete
  2: 'bg-gray-500/20 text-gray-700 dark:text-gray-400', // Closed
  3: 'bg-gray-500/20 text-gray-700 dark:text-gray-400', // Archived
  4: 'bg-yellow-500/20 text-yellow-700 dark:text-yellow-400', // Progress
  5: 'bg-purple-500/20 text-purple-700 dark:text-purple-400', // Review
  6: 'bg-red-500/20 text-red-700 dark:text-red-400', // Blocked
}

/** PR status color variants */
const PR_STATUS_COLORS: Record<string, string> = {
  open: 'bg-green-500/20 text-green-700 dark:text-green-400',
  merged: 'bg-purple-500/20 text-purple-700 dark:text-purple-400',
  closed: 'bg-red-500/20 text-red-700 dark:text-red-400',
}

export interface InlineIssueDetailRowProps {
  line: TaskGraphIssueRenderLine
  maxLanes: number
  onEdit?: (issueId: string) => void
  onRunAgent?: (issueId: string) => void
  onClose?: () => void
}

/**
 * Inline expanded details panel for an issue.
 * Shows full issue metadata, description, branch info, and action buttons.
 */
export const InlineIssueDetailRow = memo(function InlineIssueDetailRow({
  line,
  maxLanes,
  onEdit,
  onRunAgent,
  onClose,
}: InlineIssueDetailRowProps) {
  const [copied, setCopied] = useState(false)

  // Calculate left padding to align with content (after SVG)
  const svgWidth = 24 * Math.max(maxLanes, 1) + 12

  const typeColor = getTypeColor(line.issueType)

  const handleCopyBranch = useCallback(async () => {
    if (line.branchName) {
      await navigator.clipboard.writeText(line.branchName)
      setCopied(true)
      setTimeout(() => setCopied(false), 2000)
    }
  }, [line.branchName])

  const handleEdit = useCallback(() => {
    onEdit?.(line.issueId)
  }, [onEdit, line.issueId])

  const handleRunAgent = useCallback(() => {
    onRunAgent?.(line.issueId)
  }, [onRunAgent, line.issueId])

  return (
    <div
      className={cn(
        'animate-expand bg-muted/30 border-muted overflow-hidden border-t px-3 py-4',
        'transition-all duration-200 ease-out'
      )}
      style={{ marginLeft: svgWidth }}
    >
      {/* Header with badges and close button */}
      <div className="mb-3 flex flex-wrap items-center gap-2">
        {/* Issue ID */}
        <span className="font-mono text-sm font-medium">{line.issueId}</span>

        {/* Type badge */}
        <span
          className="shrink-0 rounded px-1.5 py-0.5 text-xs font-medium"
          style={{
            backgroundColor: `${typeColor}20`,
            color: typeColor,
          }}
        >
          {TYPE_LABELS[line.issueType] ?? 'Task'}
        </span>

        {/* Status badge */}
        <span
          className={cn(
            'shrink-0 rounded px-1.5 py-0.5 text-xs font-medium',
            STATUS_COLORS[line.status] ?? STATUS_COLORS[0]
          )}
        >
          {STATUS_LABELS[line.status] ?? 'Open'}
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

      {/* Branch name with copy */}
      {line.branchName && (
        <div className="mb-3 flex items-center gap-2">
          <span className="text-muted-foreground text-xs">Branch:</span>
          <code className="bg-muted rounded px-1.5 py-0.5 font-mono text-xs">
            {line.branchName}
          </code>
          <Button
            variant="ghost"
            size="sm"
            className="h-6 w-6 p-0"
            onClick={handleCopyBranch}
            aria-label="Copy branch name"
          >
            <Copy className="h-3 w-3" />
          </Button>
          {copied && <span className="text-muted-foreground text-xs">Copied!</span>}
        </div>
      )}

      {/* Linked PR */}
      {line.linkedPr && (
        <div className="mb-3 flex items-center gap-2">
          <span className="text-muted-foreground text-xs">Pull Request:</span>
          <a
            href={line.linkedPr.url ?? '#'}
            target="_blank"
            rel="noopener noreferrer"
            className="text-primary flex items-center gap-1 text-xs hover:underline"
          >
            #{line.linkedPr.number}
            <ExternalLink className="h-3 w-3" />
          </a>
          {line.linkedPr.status && (
            <span
              data-testid="pr-status-badge"
              className={cn(
                'shrink-0 rounded px-1.5 py-0.5 text-xs font-medium',
                PR_STATUS_COLORS[line.linkedPr.status] ?? PR_STATUS_COLORS.open
              )}
            >
              {line.linkedPr.status}
            </span>
          )}
        </div>
      )}

      {/* Agent status */}
      {line.agentStatus?.isActive && (
        <div className="mb-3 flex items-center gap-2" data-testid="agent-status-indicator">
          <span className="text-muted-foreground text-xs">Agent:</span>
          <span className="relative flex h-2 w-2 shrink-0">
            <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-green-400 opacity-75" />
            <span className="relative inline-flex h-2 w-2 rounded-full bg-green-500" />
          </span>
          <span className="text-xs text-green-600 dark:text-green-400">
            {line.agentStatus.status ?? 'running'}
          </span>
          {line.agentStatus.sessionId && (
            <a
              href={`/sessions/${line.agentStatus.sessionId}`}
              className="text-primary text-xs hover:underline"
              aria-label="View session"
            >
              View session
            </a>
          )}
        </div>
      )}

      {/* Description */}
      <div className="mb-4">
        {line.description ? (
          <Markdown className="text-foreground prose-sm max-w-none">{line.description}</Markdown>
        ) : (
          <p className="text-muted-foreground text-sm italic">No description</p>
        )}
      </div>

      {/* Actions */}
      <div className="flex items-center gap-2">
        <Button variant="outline" size="sm" onClick={handleEdit} aria-label="Edit">
          <Pencil className="mr-1 h-3 w-3" />
          Edit
        </Button>
        <Button variant="outline" size="sm" onClick={handleRunAgent} aria-label="Run Agent">
          <Play className="mr-1 h-3 w-3" />
          Run Agent
        </Button>
      </div>
    </div>
  )
})
