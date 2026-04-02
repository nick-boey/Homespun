import { useState } from 'react'
import { type IssueChangeDto, ChangeType } from '@/api'
import { cn } from '@/lib/utils'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { X, ChevronDown, ChevronRight } from 'lucide-react'

export interface IssueChangeDetailPanelProps {
  /** The change data to display */
  change: IssueChangeDto
  /** Callback when the close button is clicked */
  onClose?: () => void
}

/**
 * Expandable panel showing change details for a selected issue.
 * Displays change type badge, field changes for updates, or issue info for create/delete.
 */
export function IssueChangeDetailPanel({ change, onClose }: IssueChangeDetailPanelProps) {
  const [isExpanded, setIsExpanded] = useState(true)

  const changeType = change.changeType
  const fieldChanges = change.fieldChanges ?? []
  const hasFieldChanges = fieldChanges.length > 0

  // Get the issue to display (modified for created/updated, original for deleted)
  const displayIssue =
    changeType === ChangeType.DELETED ? change.originalIssue : change.modifiedIssue

  const typeConfig = getChangeTypeConfig(changeType)

  return (
    <div
      className="rounded-lg border"
      data-testid="issue-change-detail-panel"
      data-issue-id={change.issueId}
    >
      {/* Header */}
      <div className="bg-muted/50 flex items-center justify-between border-b px-3 py-2">
        <div className="flex items-center gap-2 overflow-hidden">
          <span className="text-muted-foreground shrink-0 font-mono text-xs">
            [{change.issueId}]
          </span>
          <span className="truncate text-sm font-medium">{change.title ?? 'Untitled'}</span>
          <Badge
            variant="outline"
            className={cn('shrink-0 text-xs', typeConfig.badgeClass)}
            data-testid="change-type-badge"
          >
            {typeConfig.label}
          </Badge>
        </div>
        {onClose && (
          <Button
            variant="ghost"
            size="icon"
            className="h-6 w-6 shrink-0"
            onClick={onClose}
            data-testid="close-detail-panel"
          >
            <X className="h-4 w-4" />
            <span className="sr-only">Close</span>
          </Button>
        )}
      </div>

      {/* Content */}
      <div className="p-2">
        {changeType === ChangeType.UPDATED && hasFieldChanges && (
          <FieldChangesSection
            fieldChanges={fieldChanges}
            isExpanded={isExpanded}
            onToggle={() => setIsExpanded(!isExpanded)}
          />
        )}

        {changeType === ChangeType.CREATED && displayIssue && (
          <IssueInfoSection
            title="New Issue Details"
            issue={displayIssue}
            isExpanded={isExpanded}
            onToggle={() => setIsExpanded(!isExpanded)}
          />
        )}

        {changeType === ChangeType.DELETED && displayIssue && (
          <IssueInfoSection
            title="Deleted Issue Details"
            issue={displayIssue}
            isExpanded={isExpanded}
            onToggle={() => setIsExpanded(!isExpanded)}
          />
        )}

        {!hasFieldChanges && changeType === ChangeType.UPDATED && (
          <p className="text-muted-foreground text-sm">No field changes recorded</p>
        )}
      </div>
    </div>
  )
}

function getChangeTypeConfig(changeType: ChangeType) {
  switch (changeType) {
    case ChangeType.CREATED:
      return {
        label: 'Created',
        badgeClass: 'border-green-500 bg-green-50 text-green-700 dark:bg-green-950/50',
      }
    case ChangeType.UPDATED:
      return {
        label: 'Updated',
        badgeClass: 'border-yellow-500 bg-yellow-50 text-yellow-700 dark:bg-yellow-950/50',
      }
    case ChangeType.DELETED:
      return {
        label: 'Deleted',
        badgeClass: 'border-red-500 bg-red-50 text-red-700 dark:bg-red-950/50',
      }
    default:
      return {
        label: 'Unknown',
        badgeClass: '',
      }
  }
}

interface FieldChangesSectionProps {
  fieldChanges: Array<{
    fieldName: string | null
    oldValue?: string | null
    newValue?: string | null
  }>
  isExpanded: boolean
  onToggle: () => void
}

function FieldChangesSection({ fieldChanges, isExpanded, onToggle }: FieldChangesSectionProps) {
  return (
    <div>
      <button
        className="text-muted-foreground hover:text-foreground flex w-full items-center gap-1 text-sm"
        onClick={onToggle}
        data-testid="field-changes-toggle"
      >
        {isExpanded ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
        <span>Fields Changed ({fieldChanges.length})</span>
      </button>
      {isExpanded && (
        <ul className="mt-2 space-y-1 pl-5" data-testid="field-changes-list">
          {fieldChanges.map((fc, idx) => (
            <li key={idx} className="text-sm">
              <span className="text-muted-foreground font-medium">{fc.fieldName}:</span>{' '}
              <span className="break-all text-red-600 line-through">
                {formatValue(fc.oldValue)}
              </span>
              <span className="text-muted-foreground"> → </span>
              <span className="break-all text-green-600">{formatValue(fc.newValue)}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

interface IssueInfoSectionProps {
  title: string
  issue: {
    id?: string | null
    title?: string | null
    description?: string | null
    type?: string | null
    status?: string | null
  }
  isExpanded: boolean
  onToggle: () => void
}

function IssueInfoSection({ title, issue, isExpanded, onToggle }: IssueInfoSectionProps) {
  const fields = [
    { name: 'ID', value: issue.id },
    { name: 'Title', value: issue.title },
    { name: 'Type', value: issue.type },
    { name: 'Status', value: issue.status },
    { name: 'Description', value: issue.description },
  ].filter((f) => f.value != null)

  return (
    <div>
      <button
        className="text-muted-foreground hover:text-foreground flex w-full items-center gap-1 text-sm"
        onClick={onToggle}
        data-testid="issue-info-toggle"
      >
        {isExpanded ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
        <span>{title}</span>
      </button>
      {isExpanded && (
        <ul className="mt-2 space-y-1 pl-5" data-testid="issue-info-list">
          {fields.map((field) => (
            <li key={field.name} className="text-sm">
              <span className="text-muted-foreground font-medium">{field.name}:</span>{' '}
              <span>{field.value}</span>
            </li>
          ))}
        </ul>
      )}
    </div>
  )
}

function formatValue(value: string | null | undefined): string {
  if (value === null || value === undefined) {
    return '(empty)'
  }
  return value
}
