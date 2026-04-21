/**
 * Shared center content strip for an issue row.
 *
 * Renders the type/status pills, OpenSpec indicators, phase rollup,
 * execution-mode toggle, optional multi-parent badge, title, assignee,
 * and linked-PR. Used by the task-graph row (with SVG gutter + hover
 * actions) and by the orphan-link picker (no chrome, static pills).
 */

import type { ReactNode } from 'react'
import { cn } from '@/lib/utils'
import { ExecutionMode, IssueStatus, IssueType } from '@/api'
import type { IssueOpenSpecState } from '@/api/generated/types.gen'
import {
  ISSUE_STATUS_LABELS,
  ISSUE_STATUS_COMPACT_LABELS,
  ISSUE_TYPE_LABELS,
} from '@/lib/issue-constants'
import { OpenSpecIndicators } from './openspec-indicators'
import { PhaseRollupBadges } from './phase-rollup'
import { TaskGraphMarkerType, type TaskGraphIssueRenderLine } from '../services'
import { getTypeColor } from './task-graph-svg'
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

export interface IssueRowContentProps {
  line: TaskGraphIssueRenderLine
  projectId: string
  openSpecState?: IssueOpenSpecState | null
  searchQuery?: string
  /**
   * When false, type/status render as static pills and the execution-mode
   * toggle and multi-parent badge are disabled/hidden. Used by the
   * orphan-link picker shell.
   */
  editable?: boolean
  /** Render linked-PR badge + live status polling (default true). */
  showPrStatus?: boolean
  onTypeChange?: (issueId: string, newType: IssueType) => void
  onStatusChange?: (issueId: string, newStatus: IssueStatus) => void
  onExecutionModeChange?: (issueId: string, newMode: ExecutionMode) => void
  onSelectFirstInstance?: (issueId: string) => void
  /** Content to append at the end of the row (e.g. IssueRowActions). */
  trailing?: ReactNode
}

function getDisplayName(email: string | null | undefined): string | null {
  if (!email) return null
  const atIndex = email.indexOf('@')
  return atIndex > 0 ? email.substring(0, atIndex) : email
}

export function IssueRowContent({
  line,
  projectId,
  openSpecState,
  searchQuery: _searchQuery,
  editable = true,
  showPrStatus = true,
  onTypeChange,
  onStatusChange,
  onExecutionModeChange,
  onSelectFirstInstance,
  trailing,
}: IssueRowContentProps) {
  const typeColor = getTypeColor(line.issueType)

  const { data: prStatus } = useLinkedPrStatus(
    projectId,
    line.linkedPr && showPrStatus ? line.issueId : undefined,
    showPrStatus
  )

  const statusColor = (() => {
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
  })()

  const typeLabel = ISSUE_TYPE_LABELS[line.issueType as IssueType] ?? 'Task'
  const statusLabel = ISSUE_STATUS_COMPACT_LABELS[line.status as IssueStatus] ?? 'Open'

  return (
    <div className="flex flex-1 items-center gap-2 pr-2">
      {/* Type badge */}
      {editable ? (
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <button
              type="button"
              data-testid="issue-row-type-pill"
              className="w-14 shrink-0 cursor-pointer rounded px-1.5 py-0.5 text-[10px] font-medium transition-opacity hover:opacity-80"
              style={{ backgroundColor: `${typeColor}20`, color: typeColor }}
              onClick={(e) => e.stopPropagation()}
              title="Click to change type"
            >
              {typeLabel}
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
      ) : (
        <span
          data-testid="issue-row-type-pill"
          className="w-14 shrink-0 rounded px-1.5 py-0.5 text-center text-[10px] font-medium"
          style={{ backgroundColor: `${typeColor}20`, color: typeColor }}
        >
          {typeLabel}
        </span>
      )}

      {/* Status badge */}
      {editable ? (
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <button
              type="button"
              data-testid="issue-row-status-pill"
              className={cn(
                'w-14 shrink-0 cursor-pointer rounded px-1.5 py-0.5 text-[10px] font-medium transition-opacity hover:opacity-80',
                statusColor
              )}
              onClick={(e) => e.stopPropagation()}
              title="Click to change status"
            >
              {statusLabel}
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
      ) : (
        <span
          data-testid="issue-row-status-pill"
          className={cn(
            'w-14 shrink-0 rounded px-1.5 py-0.5 text-center text-[10px] font-medium',
            statusColor
          )}
        >
          {statusLabel}
        </span>
      )}

      {/* OpenSpec indicators */}
      {openSpecState ? <OpenSpecIndicators state={openSpecState} /> : null}

      {/* Phase roll-up badges */}
      {openSpecState?.phases && openSpecState.phases.length > 0 ? (
        <PhaseRollupBadges changeName={openSpecState.changeName} phases={openSpecState.phases} />
      ) : null}

      {/* Execution mode toggle */}
      <ExecutionModeToggle
        executionMode={line.executionMode}
        disabled={!editable}
        onToggle={() =>
          onExecutionModeChange?.(
            line.issueId,
            line.executionMode === ExecutionMode.SERIES
              ? ExecutionMode.PARALLEL
              : ExecutionMode.SERIES
          )
        }
      />

      {/* Multi-parent badge (only in editable/graph shell) */}
      {editable && line.multiParentTotal != null && line.multiParentIndex != null && (
        <button
          type="button"
          className="shrink-0 rounded bg-orange-500/20 px-1 py-0.5 text-[10px] font-medium text-orange-700 transition-colors hover:bg-orange-500/30 dark:text-orange-400"
          onClick={(e) => {
            e.stopPropagation()
            if (line.multiParentIndex !== 0) {
              onSelectFirstInstance?.(line.issueId)
            }
          }}
          title={`Instance ${line.multiParentIndex + 1} of ${line.multiParentTotal}. Click to go to the first instance.`}
          data-testid="multi-parent-badge"
        >
          ({line.multiParentIndex + 1}/{line.multiParentTotal})
        </button>
      )}

      {/* Title */}
      <span className="text-sm whitespace-nowrap">{line.title || 'Untitled'}</span>

      {/* Assignee badge */}
      {line.assignedTo && (
        <Badge variant="outline" className="shrink-0 text-[10px]">
          {getDisplayName(line.assignedTo)}
        </Badge>
      )}

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
          {showPrStatus && prStatus && (
            <PrStatusIndicator
              checksPassing={prStatus.checksPassing ?? null}
              hasConflicts={prStatus.hasConflicts ?? false}
            />
          )}
        </div>
      )}

      {trailing}
    </div>
  )
}
