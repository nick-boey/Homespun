/**
 * TaskGraphRowContent - Content components for rendering inside foreignObject elements.
 *
 * These components extract the HTML content from the existing row components
 * for use inside the single SVG canvas architecture.
 */

import { memo, forwardRef, useCallback, useState, useMemo, type HTMLAttributes } from 'react'
import { Copy, Pencil, Play, X, ExternalLink } from 'lucide-react'
import { cn } from '@/lib/utils'
import { IssueType, IssueStatus, ExecutionMode } from '@/api'
import { ISSUE_STATUS_LABELS, ISSUE_TYPE_LABELS, ISSUE_STATUS_COLORS } from '@/lib/issue-constants'
import type { TaskGraphIssueRenderLine, TaskGraphPrRenderLine } from '../services'
import { TaskGraphMarkerType } from '../services'
import { IssueRowActions } from './issue-row-actions'
import { PrStatusIndicator } from './pr-status-indicator'
import { ExecutionModeToggle } from './execution-mode-toggle'
import { useLinkedPrStatus } from '../hooks/use-linked-pr-status'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Markdown } from '@/components/ui/markdown'
import { getTypeColor, ROW_HEIGHT } from './task-graph-svg'

/**
 * Extracts the username portion from an email address.
 */
function getDisplayName(email: string | null | undefined): string | null {
  if (!email) return null
  const atIndex = email.indexOf('@')
  return atIndex > 0 ? email.substring(0, atIndex) : email
}

/** PR status color variants */
const PR_STATUS_COLORS: Record<string, string> = {
  open: 'bg-green-500/20 text-green-700 dark:text-green-400',
  merged: 'bg-purple-500/20 text-purple-700 dark:text-purple-400',
  closed: 'bg-red-500/20 text-red-700 dark:text-red-400',
}

export interface TaskGraphIssueRowContentProps extends HTMLAttributes<HTMLDivElement> {
  line: TaskGraphIssueRenderLine
  projectId: string
  isSelected?: boolean
  isExpanded?: boolean
  searchQuery?: string
  onToggleExpand?: () => void
  onEdit?: (issueId: string) => void
  onRunAgent?: (issueId: string) => void
  onOpenSession?: (sessionId: string) => void
  showActions?: boolean
  isMoveSource?: boolean
  isMoveOperationActive?: boolean
  onTypeChange?: (issueId: string, newType: IssueType) => void
  onStatusChange?: (issueId: string, newStatus: IssueStatus) => void
  onExecutionModeChange?: (issueId: string, newMode: ExecutionMode) => void
}

/**
 * Issue row content for rendering inside foreignObject.
 * Contains all the HTML elements (badges, title, actions) without the SVG portion.
 */
export const TaskGraphIssueRowContent = memo(
  forwardRef<HTMLDivElement, TaskGraphIssueRowContentProps>(function TaskGraphIssueRowContent(
    {
      line,
      projectId,
      isSelected = false,
      isExpanded = false,
      searchQuery,
      onToggleExpand,
      onEdit,
      onRunAgent,
      onOpenSession,
      showActions = true,
      isMoveSource = false,
      isMoveOperationActive = false,
      onTypeChange,
      onStatusChange,
      onExecutionModeChange,
      className,
      ...props
    },
    ref
  ) {
    const typeColor = getTypeColor(line.issueType)
    const hasSearchMatch =
      searchQuery && line.title.toLowerCase().includes(searchQuery.toLowerCase())

    // Fetch PR status if this issue has a linked PR
    const { data: prStatus } = useLinkedPrStatus(
      projectId,
      line.linkedPr ? line.issueId : undefined,
      true
    )

    // Status color based on marker
    const getStatusColor = () => {
      switch (line.marker) {
        case TaskGraphMarkerType.Complete:
          return 'bg-green-500/20 text-green-700 dark:text-green-400'
        case TaskGraphMarkerType.Closed:
          return 'bg-gray-500/20 text-gray-700 dark:text-gray-400'
        case TaskGraphMarkerType.Actionable:
          return 'bg-blue-500/20 text-blue-700 dark:text-blue-400'
        default:
          return 'bg-muted text-muted-foreground'
      }
    }

    return (
      <div
        ref={ref}
        role="row"
        tabIndex={0}
        aria-selected={isSelected}
        aria-expanded={isExpanded}
        data-issue-id={line.issueId}
        className={cn(
          'group flex cursor-pointer items-center gap-2 transition-colors',
          'hover:bg-muted/50 focus-visible:ring-ring focus-visible:ring-2 focus-visible:outline-none',
          isSelected && 'bg-muted',
          hasSearchMatch && 'ring-2 ring-yellow-400',
          isMoveSource && 'ring-primary opacity-70 ring-2',
          isMoveOperationActive && !isMoveSource && 'hover:ring-primary hover:ring-2',
          className
        )}
        style={{ height: ROW_HEIGHT }}
        onDoubleClick={onToggleExpand}
        {...props}
      >
        {/* Issue content - no SVG here, that's rendered separately */}
        <div className="flex flex-1 items-center gap-2 pr-2">
          {/* Type badge with dropdown */}
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <button
                type="button"
                className="shrink-0 cursor-pointer rounded px-1.5 py-0.5 text-[10px] font-medium transition-opacity hover:opacity-80"
                style={{
                  backgroundColor: `${typeColor}20`,
                  color: typeColor,
                }}
                onClick={(e) => e.stopPropagation()}
                title="Click to change type"
              >
                {ISSUE_TYPE_LABELS[line.issueType as IssueType] ?? 'Task'}
              </button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="start" onClick={(e) => e.stopPropagation()}>
              {Object.entries(ISSUE_TYPE_LABELS).map(([value, label]) => (
                <DropdownMenuItem
                  key={value}
                  onClick={() => onTypeChange?.(line.issueId, value as IssueType)}
                  className={cn('text-xs', value === line.issueType && 'bg-accent')}
                >
                  <span
                    className="mr-2 inline-block h-2 w-2 rounded-full"
                    style={{ backgroundColor: getTypeColor(value as IssueType) }}
                  />
                  {label}
                </DropdownMenuItem>
              ))}
            </DropdownMenuContent>
          </DropdownMenu>

          {/* Status badge with dropdown */}
          <DropdownMenu>
            <DropdownMenuTrigger asChild>
              <button
                type="button"
                className={cn(
                  'shrink-0 cursor-pointer rounded px-1.5 py-0.5 text-[10px] font-medium transition-opacity hover:opacity-80',
                  getStatusColor()
                )}
                onClick={(e) => e.stopPropagation()}
                title="Click to change status"
              >
                {ISSUE_STATUS_LABELS[line.status as IssueStatus] ?? 'Open'}
              </button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="start" onClick={(e) => e.stopPropagation()}>
              {Object.entries(ISSUE_STATUS_LABELS).map(([value, label]) => (
                <DropdownMenuItem
                  key={value}
                  onClick={() => onStatusChange?.(line.issueId, value as IssueStatus)}
                  className={cn('text-xs', value === line.status && 'bg-accent')}
                >
                  {label}
                </DropdownMenuItem>
              ))}
            </DropdownMenuContent>
          </DropdownMenu>

          {/* Execution mode toggle */}
          <ExecutionModeToggle
            executionMode={line.executionMode}
            onToggle={() =>
              onExecutionModeChange?.(
                line.issueId,
                line.executionMode === ExecutionMode.SERIES
                  ? ExecutionMode.PARALLEL
                  : ExecutionMode.SERIES
              )
            }
          />

          {/* Title */}
          <span className="text-sm whitespace-nowrap">{line.title || 'Untitled'}</span>

          {/* Assignee badge */}
          {line.assignedTo && (
            <Badge variant="outline" className="shrink-0 text-[10px]">
              {getDisplayName(line.assignedTo)}
            </Badge>
          )}

          {/* Spacer */}
          <div className="flex-1" />

          {/* Linked PR indicator */}
          {line.linkedPr && (
            <div className="flex items-center gap-1.5">
              <a
                href={line.linkedPr.url ?? '#'}
                target="_blank"
                rel="noopener noreferrer"
                className="text-muted-foreground hover:text-foreground shrink-0 text-xs underline"
                onClick={(e) => e.stopPropagation()}
              >
                #{line.linkedPr.number}
              </a>
              {prStatus && (
                <PrStatusIndicator
                  checksPassing={prStatus.checksPassing ?? null}
                  hasConflicts={prStatus.hasConflicts ?? false}
                />
              )}
            </div>
          )}

          {/* Hover actions */}
          {showActions && (
            <IssueRowActions
              issueId={line.issueId}
              isExpanded={isExpanded}
              activeSessionId={line.agentStatus?.isActive ? line.agentStatus.sessionId : null}
              onEdit={onEdit}
              onRunAgent={onRunAgent}
              onOpenSession={onOpenSession}
              onExpand={onToggleExpand}
            />
          )}
        </div>
      </div>
    )
  })
)

export interface TaskGraphIssueExpandedContentProps {
  line: TaskGraphIssueRenderLine
  onEdit?: (issueId: string) => void
  onRunAgent?: (issueId: string) => void
  onOpenSession?: (sessionId: string) => void
  onClose?: () => void
}

/**
 * Expanded details content for an issue.
 * Shows full issue metadata, description, branch info, and action buttons.
 */
export const TaskGraphIssueExpandedContent = memo(function TaskGraphIssueExpandedContent({
  line,
  onEdit,
  onRunAgent,
  onOpenSession,
  onClose,
}: TaskGraphIssueExpandedContentProps) {
  const [copied, setCopied] = useState(false)
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

  const handleOpenSession = useCallback(() => {
    if (line.agentStatus?.sessionId) {
      onOpenSession?.(line.agentStatus.sessionId)
    }
  }, [onOpenSession, line.agentStatus])

  const hasActiveSession = useMemo(
    () => line.agentStatus?.isActive && line.agentStatus.sessionId,
    [line.agentStatus?.isActive, line.agentStatus?.sessionId]
  )

  return (
    <div
      className={cn(
        'animate-expand bg-muted/30 border-muted overflow-hidden border-t px-3 py-4',
        'transition-all duration-200 ease-out'
      )}
    >
      {/* Header with badges and close button */}
      <div className="mb-3 flex flex-wrap items-center gap-2">
        <span className="font-mono text-sm font-medium">{line.issueId}</span>

        <span
          className="shrink-0 rounded px-1.5 py-0.5 text-xs font-medium"
          style={{
            backgroundColor: `${typeColor}20`,
            color: typeColor,
          }}
        >
          {ISSUE_TYPE_LABELS[line.issueType] ?? 'Task'}
        </span>

        <span
          className={cn(
            'shrink-0 rounded px-1.5 py-0.5 text-xs font-medium',
            ISSUE_STATUS_COLORS[line.status as IssueStatus] ?? ISSUE_STATUS_COLORS[IssueStatus.OPEN]
          )}
        >
          {ISSUE_STATUS_LABELS[line.status as IssueStatus] ?? 'Open'}
        </span>

        <div className="flex-1" />

        <Button
          variant="ghost"
          size="sm"
          className="h-10 w-10 p-0"
          onClick={onClose}
          aria-label="Close"
        >
          <X className="h-5 w-5" />
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
      <div className="flex flex-wrap items-center gap-2">
        <Button
          variant="outline"
          size="sm"
          onClick={handleEdit}
          aria-label="Edit"
          className="min-h-[44px] px-4"
        >
          <Pencil className="mr-1.5 h-4 w-4" />
          Edit
        </Button>
        {hasActiveSession ? (
          <Button
            variant="outline"
            size="sm"
            onClick={handleOpenSession}
            aria-label="Open Session"
            className="min-h-[44px] px-4"
          >
            <ExternalLink className="mr-1.5 h-4 w-4" />
            Open Session
          </Button>
        ) : (
          <Button
            variant="outline"
            size="sm"
            onClick={handleRunAgent}
            aria-label="Run Agent"
            className="min-h-[44px] px-4"
          >
            <Play className="mr-1.5 h-4 w-4" />
            Run Agent
          </Button>
        )}
      </div>
    </div>
  )
})

export interface TaskGraphPrRowContentProps extends HTMLAttributes<HTMLDivElement> {
  line: TaskGraphPrRenderLine
}

/**
 * PR row content for rendering inside foreignObject.
 */
export const TaskGraphPrRowContent = memo(
  forwardRef<HTMLDivElement, TaskGraphPrRowContentProps>(function TaskGraphPrRowContent(
    { line, className, ...props },
    ref
  ) {
    return (
      <div
        ref={ref}
        role="row"
        className={cn('flex cursor-default items-center gap-2 opacity-70', className)}
        style={{ height: ROW_HEIGHT }}
        {...props}
      >
        <div className="flex flex-1 items-center gap-2 overflow-hidden pr-2">
          <span className="text-muted-foreground shrink-0 font-mono text-xs">#{line.prNumber}</span>

          <span
            className={cn(
              'shrink-0 rounded px-1.5 py-0.5 text-[10px] font-medium',
              line.isMerged
                ? 'bg-purple-500/20 text-purple-700 dark:text-purple-400'
                : 'bg-red-500/20 text-red-700 dark:text-red-400'
            )}
          >
            {line.isMerged ? 'Merged' : 'Closed'}
          </span>

          <span className="text-sm whitespace-nowrap" title={line.title}>
            {line.title || 'Untitled PR'}
          </span>

          <div className="flex-1" />

          {line.url && (
            <a
              href={line.url}
              target="_blank"
              rel="noopener noreferrer"
              className="text-muted-foreground hover:text-foreground shrink-0 text-xs underline"
            >
              View
            </a>
          )}
        </div>
      </div>
    )
  })
)

export type TaskGraphSeparatorContentProps = HTMLAttributes<HTMLDivElement>

/**
 * Separator content for rendering inside foreignObject.
 */
export const TaskGraphSeparatorContent = memo(
  forwardRef<HTMLDivElement, TaskGraphSeparatorContentProps>(function TaskGraphSeparatorContent(
    { className, ...props },
    ref
  ) {
    return (
      <div
        ref={ref}
        role="separator"
        className={cn('flex items-center', className)}
        style={{ height: ROW_HEIGHT / 2 }}
        {...props}
      >
        <div className="bg-border mx-2 h-px flex-1" />
      </div>
    )
  })
)

export interface TaskGraphLoadMoreContentProps extends HTMLAttributes<HTMLButtonElement> {
  onLoadMore?: () => void
}

/**
 * Load more button content for rendering inside foreignObject.
 */
export const TaskGraphLoadMoreContent = memo(
  forwardRef<HTMLButtonElement, TaskGraphLoadMoreContentProps>(function TaskGraphLoadMoreContent(
    { onLoadMore, className, ...props },
    ref
  ) {
    return (
      <button
        ref={ref}
        type="button"
        className={cn(
          'flex w-full cursor-pointer items-center gap-2 transition-colors',
          'hover:bg-muted/50 focus-visible:ring-ring focus-visible:ring-2 focus-visible:outline-none',
          className
        )}
        style={{ height: ROW_HEIGHT }}
        onClick={onLoadMore}
        {...props}
      >
        <span className="text-muted-foreground text-sm">Load more PRs...</span>
      </button>
    )
  })
)
