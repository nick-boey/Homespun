/**
 * HTML row component for task graph Konva canvas.
 *
 * Renders issue content (badges, title, actions) as HTML positioned on the canvas.
 * Used with react-konva-utils Html component for DOM overlay.
 */

import { memo } from 'react'
import { cn } from '@/lib/utils'
import { IssueType, IssueStatus, ExecutionMode } from '@/api'
import { ISSUE_STATUS_COMPACT_LABELS, ISSUE_TYPE_LABELS } from '@/lib/issue-constants'
import { Badge } from '@/components/ui/badge'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import type { TaskGraphIssueRenderLine } from '../../services'
import { TaskGraphMarkerType } from '../../services'
import { ExecutionModeToggle } from '../execution-mode-toggle'
import { IssueRowActions } from '../issue-row-actions'
import { PrStatusIndicator } from '../pr-status-indicator'
import { useLinkedPrStatus } from '../../hooks/use-linked-pr-status'
import { ROW_HEIGHT, getTypeColor, calculateSvgWidth } from '../task-graph-svg'

/**
 * Extracts the username portion from an email address.
 */
function getDisplayName(email: string | null | undefined): string | null {
  if (!email) return null
  const atIndex = email.indexOf('@')
  return atIndex > 0 ? email.substring(0, atIndex) : email
}

export interface KonvaHtmlRowProps {
  /** The issue render line data */
  line: TaskGraphIssueRenderLine
  /** Project ID for API calls */
  projectId: string
  /** Maximum lanes for width calculation */
  maxLanes: number
  /** Whether this row is selected */
  isSelected?: boolean
  /** Whether this row is expanded */
  isExpanded?: boolean
  /** Search query for highlighting */
  searchQuery?: string
  /** Click handler */
  onClick?: () => void
  /** Double click handler */
  onDoubleClick?: () => void
  /** Edit handler */
  onEdit?: (issueId: string) => void
  /** Run agent handler */
  onRunAgent?: (issueId: string) => void
  /** Open session handler */
  onOpenSession?: (sessionId: string) => void
  /** Type change handler */
  onTypeChange?: (issueId: string, newType: IssueType) => void
  /** Status change handler */
  onStatusChange?: (issueId: string, newStatus: IssueStatus) => void
  /** Execution mode change handler */
  onExecutionModeChange?: (issueId: string, newMode: ExecutionMode) => void
  /** Whether this issue is the source of a move operation */
  isMoveSource?: boolean
  /** Whether a move operation is in progress */
  isMoveOperationActive?: boolean
  /** Whether to show actions */
  showActions?: boolean
}

/**
 * HTML row component for rendering issue content in the Konva canvas.
 */
export const KonvaHtmlRow = memo(function KonvaHtmlRow({
  line,
  projectId,
  maxLanes,
  isSelected = false,
  isExpanded = false,
  searchQuery,
  onClick,
  onDoubleClick,
  onEdit,
  onRunAgent,
  onOpenSession,
  onTypeChange,
  onStatusChange,
  onExecutionModeChange,
  isMoveSource = false,
  isMoveOperationActive = false,
  showActions = true,
}: KonvaHtmlRowProps) {
  const typeColor = getTypeColor(line.issueType)
  const hasSearchMatch = searchQuery && line.title.toLowerCase().includes(searchQuery.toLowerCase())

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

  // Width of the SVG area (for positioning content after it)
  const svgWidth = calculateSvgWidth(maxLanes)

  return (
    <div
      role="row"
      data-testid="konva-html-row"
      data-issue-id={line.issueId}
      className={cn(
        'group flex cursor-pointer items-center gap-2 transition-colors',
        'hover:ring-border hover:ring-1',
        isSelected && 'ring-primary ring-1',
        hasSearchMatch && 'ring-2 ring-yellow-400',
        isMoveSource && 'ring-primary opacity-70 ring-2',
        isMoveOperationActive && !isMoveSource && 'hover:ring-primary hover:ring-2'
      )}
      style={{
        height: ROW_HEIGHT,
        paddingLeft: svgWidth,
      }}
      onClick={onClick}
      onDoubleClick={onDoubleClick}
    >
      {/* Type badge with dropdown */}
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <button
            type="button"
            className="w-14 shrink-0 cursor-pointer rounded px-1.5 py-0.5 text-[10px] font-medium transition-opacity hover:opacity-80"
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
              'w-14 shrink-0 cursor-pointer rounded px-1.5 py-0.5 text-[10px] font-medium transition-opacity hover:opacity-80',
              getStatusColor()
            )}
            onClick={(e) => e.stopPropagation()}
            title="Click to change status"
          >
            {ISSUE_STATUS_COMPACT_LABELS[line.status as IssueStatus] ?? 'Open'}
          </button>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="start" onClick={(e) => e.stopPropagation()}>
          {Object.entries(ISSUE_STATUS_COMPACT_LABELS).map(([value, label]) => (
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

      {/* Issue ID */}
      <span className="text-muted-foreground shrink-0 font-mono text-xs">
        {line.issueId.substring(0, 6)}
      </span>

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
        />
      )}
    </div>
  )
})
