/**
 * IssueRowActions - Hover action buttons for issue rows in the task graph.
 */

import { memo, useCallback } from 'react'
import { Pencil, Play, ChevronDown, ChevronUp, ExternalLink } from 'lucide-react'
import { cn } from '@/lib/utils'
import { Button } from '@/components/ui/button'

export interface IssueRowActionsProps {
  issueId: string
  isExpanded?: boolean
  /** Session ID if an agent is active on this issue */
  activeSessionId?: string | null
  onEdit?: (issueId: string) => void
  onRunAgent?: (issueId: string) => void
  /** Called when clicking the Open Session button */
  onOpenSession?: (sessionId: string) => void
  onExpand?: () => void
  className?: string
}

/**
 * Hover action buttons for issue rows.
 * Shows Edit, Run Agent (or Open Session if active), and Expand/Collapse buttons.
 */
export const IssueRowActions = memo(function IssueRowActions({
  issueId,
  isExpanded = false,
  activeSessionId,
  onEdit,
  onRunAgent,
  onOpenSession,
  onExpand,
  className,
}: IssueRowActionsProps) {
  const handleEdit = useCallback(
    (e: React.MouseEvent) => {
      e.stopPropagation()
      onEdit?.(issueId)
    },
    [onEdit, issueId]
  )

  const handleRunAgent = useCallback(
    (e: React.MouseEvent) => {
      e.stopPropagation()
      onRunAgent?.(issueId)
    },
    [onRunAgent, issueId]
  )

  const handleOpenSession = useCallback(
    (e: React.MouseEvent) => {
      e.stopPropagation()
      if (activeSessionId) {
        onOpenSession?.(activeSessionId)
      }
    },
    [onOpenSession, activeSessionId]
  )

  const handleExpand = useCallback(
    (e: React.MouseEvent) => {
      e.stopPropagation()
      onExpand?.()
    },
    [onExpand]
  )

  return (
    <div
      className={cn(
        'flex items-center gap-0.5 opacity-0 transition-opacity',
        'group-focus-within:opacity-100 group-hover:opacity-100',
        className
      )}
    >
      <Button
        variant="ghost"
        size="sm"
        className="h-6 w-6 p-0"
        onClick={handleEdit}
        aria-label="Edit"
        data-variant="ghost"
      >
        <Pencil className="h-3 w-3" />
      </Button>

      {activeSessionId ? (
        <Button
          variant="ghost"
          size="sm"
          className="h-6 w-6 p-0"
          onClick={handleOpenSession}
          aria-label="Open Session"
          data-variant="ghost"
        >
          <ExternalLink className="h-3 w-3" />
        </Button>
      ) : (
        <Button
          variant="ghost"
          size="sm"
          className="h-6 w-6 p-0"
          onClick={handleRunAgent}
          aria-label="Run Agent"
          data-variant="ghost"
        >
          <Play className="h-3 w-3" />
        </Button>
      )}

      <Button
        variant="ghost"
        size="sm"
        className="h-6 w-6 p-0"
        onClick={handleExpand}
        aria-label={isExpanded ? 'Collapse' : 'Expand'}
        data-variant="ghost"
      >
        {isExpanded ? <ChevronUp className="h-3 w-3" /> : <ChevronDown className="h-3 w-3" />}
      </Button>
    </div>
  )
})
