import { useState, useMemo } from 'react'
import {
  AlertCircle,
  CheckCircle2,
  GitMerge,
  Info,
  XCircle,
  ChevronDown,
  ChevronRight,
} from 'lucide-react'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { Skeleton } from '@/components/ui/skeleton'
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from '@/components/ui/collapsible'
import { cn } from '@/lib/utils'
import { useApplyAgentChanges, usePreviewAgentChanges } from '../hooks/use-apply-agent-changes'
import { ChangeType, ConflictResolutionStrategy } from '@/api'
import type { IssueChangeDto, IssueConflictDto } from '@/api'

interface ApplyAgentChangesDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  sessionId: string
  projectId: string
  issueId: string
  issueTitle: string
}

export function ApplyAgentChangesDialog({
  open,
  onOpenChange,
  sessionId,
  projectId,
  issueId,
  issueTitle,
}: ApplyAgentChangesDialogProps) {
  const [expandedChanges, setExpandedChanges] = useState<Set<string>>(new Set())

  const { data: preview, isLoading, error } = usePreviewAgentChanges(issueId, sessionId, projectId)
  const applyChangesMutation = useApplyAgentChanges(issueId)

  const toggleExpanded = (changeId: string) => {
    setExpandedChanges((prev) => {
      const next = new Set(prev)
      if (next.has(changeId)) {
        next.delete(changeId)
      } else {
        next.add(changeId)
      }
      return next
    })
  }

  const handleApply = () => {
    applyChangesMutation.mutate(
      {
        projectId,
        sessionId,
        dryRun: false,
        conflictStrategy: preview?.conflicts?.length
          ? ConflictResolutionStrategy.MANUAL
          : ConflictResolutionStrategy.AGENT_WINS,
      },
      {
        onSuccess: (data) => {
          if (data.success && !data.conflicts?.length) {
            onOpenChange(false)
          }
          // If there are conflicts, keep the dialog open to show them
        },
      }
    )
  }

  const { createdCount, updatedCount, deletedCount } = useMemo(() => {
    if (!preview?.changes) return { createdCount: 0, updatedCount: 0, deletedCount: 0 }

    return {
      createdCount: preview.changes.filter((c) => c.changeType === ChangeType.CREATED).length,
      updatedCount: preview.changes.filter((c) => c.changeType === ChangeType.UPDATED).length,
      deletedCount: preview.changes.filter((c) => c.changeType === ChangeType.DELETED).length,
    }
  }, [preview])

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-h-[90vh] max-w-3xl">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <GitMerge className="h-5 w-5" />
            Apply Agent Changes
          </DialogTitle>
          <DialogDescription>
            Review changes made by the agent to issue: <strong>{issueTitle}</strong>
          </DialogDescription>
        </DialogHeader>

        <div className="space-y-4">
          {isLoading && (
            <div className="space-y-3">
              <Skeleton className="h-20 w-full" />
              <Skeleton className="h-20 w-full" />
              <Skeleton className="h-20 w-full" />
            </div>
          )}

          {error && (
            <div className="border-destructive/50 bg-destructive/10 text-destructive flex items-center gap-2 rounded-md border p-3">
              <AlertCircle className="h-4 w-4 shrink-0" />
              <p className="text-sm">{error.message}</p>
            </div>
          )}

          {preview && !preview.changes?.length && (
            <div className="bg-muted flex items-center gap-2 rounded-md border p-3">
              <Info className="text-muted-foreground h-4 w-4 shrink-0" />
              <p className="text-muted-foreground text-sm">
                No changes detected from the agent session.
              </p>
            </div>
          )}

          {preview?.conflicts && preview.conflicts.length > 0 && (
            <div className="border-destructive/50 bg-destructive/10 text-destructive flex items-center gap-2 rounded-md border p-3">
              <AlertCircle className="h-4 w-4 shrink-0" />
              <p className="text-sm">
                {preview.conflicts.length} conflict{preview.conflicts.length > 1 ? 's' : ''}{' '}
                detected. Manual resolution required.
              </p>
            </div>
          )}

          {preview?.changes && preview.changes.length > 0 && (
            <>
              <div className="flex gap-4 text-sm">
                {createdCount > 0 && (
                  <div className="flex items-center gap-1">
                    <CheckCircle2 className="h-4 w-4 text-green-600" />
                    <span>{createdCount} created</span>
                  </div>
                )}
                {updatedCount > 0 && (
                  <div className="flex items-center gap-1">
                    <GitMerge className="h-4 w-4 text-blue-600" />
                    <span>{updatedCount} updated</span>
                  </div>
                )}
                {deletedCount > 0 && (
                  <div className="flex items-center gap-1">
                    <XCircle className="h-4 w-4 text-red-600" />
                    <span>{deletedCount} deleted</span>
                  </div>
                )}
              </div>

              <div className="h-[400px] overflow-y-auto rounded-md border p-4">
                <div className="space-y-3">
                  {preview.changes.map((change, index) => (
                    <ChangeItem
                      key={change.issueId || index}
                      change={change}
                      expanded={expandedChanges.has(change.issueId || '')}
                      onToggle={() => toggleExpanded(change.issueId || '')}
                    />
                  ))}
                </div>
              </div>
            </>
          )}

          {preview?.conflicts && preview.conflicts.length > 0 && (
            <div className="space-y-2">
              <h4 className="text-sm font-semibold">Conflicts</h4>
              <div className="h-[200px] overflow-y-auto rounded-md border p-4">
                <div className="space-y-3">
                  {preview.conflicts.map((conflict, index) => (
                    <ConflictItem key={conflict.issueId || index} conflict={conflict} />
                  ))}
                </div>
              </div>
            </div>
          )}
        </div>

        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>
            Cancel
          </Button>
          <Button
            onClick={handleApply}
            disabled={
              isLoading ||
              !preview?.changes?.length ||
              applyChangesMutation.isPending ||
              Boolean(preview?.conflicts && preview.conflicts.length > 0)
            }
          >
            {applyChangesMutation.isPending ? 'Applying...' : 'Apply Changes'}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

interface ChangeItemProps {
  change: IssueChangeDto
  expanded: boolean
  onToggle: () => void
}

function ChangeItem({ change, expanded, onToggle }: ChangeItemProps) {
  const getChangeIcon = (type: ChangeType | undefined) => {
    switch (type) {
      case ChangeType.CREATED:
        return <CheckCircle2 className="h-4 w-4 text-green-600" />
      case ChangeType.UPDATED:
        return <GitMerge className="h-4 w-4 text-blue-600" />
      case ChangeType.DELETED:
        return <XCircle className="h-4 w-4 text-red-600" />
      default:
        return null
    }
  }

  const getChangeBadge = (type: ChangeType | undefined) => {
    switch (type) {
      case ChangeType.CREATED:
        return <Badge className="bg-green-100 text-green-700">Created</Badge>
      case ChangeType.UPDATED:
        return <Badge className="bg-blue-100 text-blue-700">Updated</Badge>
      case ChangeType.DELETED:
        return <Badge className="bg-red-100 text-red-700">Deleted</Badge>
      default:
        return null
    }
  }

  return (
    <Collapsible>
      <CollapsibleTrigger
        onClick={onToggle}
        className={cn(
          'hover:bg-muted/50 flex w-full items-center gap-2 rounded-md border p-3 text-left transition-colors',
          expanded && 'bg-muted/30'
        )}
      >
        {expanded ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
        {getChangeIcon(change.changeType)}
        <span className="flex-1 font-medium">{change.title || change.issueId}</span>
        {getChangeBadge(change.changeType)}
      </CollapsibleTrigger>
      <CollapsibleContent>
        {change.fieldChanges && change.fieldChanges.length > 0 && (
          <div className="border-muted mt-2 space-y-1 border-l-2 pl-4">
            {change.fieldChanges.map((field, index) => (
              <div key={index} className="text-sm">
                <span className="font-medium">{field.fieldName}:</span>
                {field.oldValue && (
                  <span className="text-muted-foreground mx-1 line-through">{field.oldValue}</span>
                )}
                {field.oldValue && field.newValue && <span className="mx-1">→</span>}
                {field.newValue && (
                  <span className="text-foreground font-medium">{field.newValue}</span>
                )}
              </div>
            ))}
          </div>
        )}
      </CollapsibleContent>
    </Collapsible>
  )
}

interface ConflictItemProps {
  conflict: IssueConflictDto
}

function ConflictItem({ conflict }: ConflictItemProps) {
  return (
    <div className="border-destructive/50 rounded-md border p-3">
      <div className="mb-2 flex items-center gap-2">
        <AlertCircle className="text-destructive h-4 w-4" />
        <span className="font-medium">{conflict.title || conflict.issueId}</span>
      </div>
      {conflict.fieldConflicts && conflict.fieldConflicts.length > 0 && (
        <div className="space-y-1 text-sm">
          {conflict.fieldConflicts.map((field, index) => (
            <div key={index} className="grid grid-cols-3 gap-2">
              <div className="font-medium">{field.fieldName}:</div>
              <div>
                <span className="text-muted-foreground">Main:</span> {field.mainValue}
              </div>
              <div>
                <span className="text-muted-foreground">Agent:</span> {field.agentValue}
              </div>
            </div>
          ))}
        </div>
      )}
    </div>
  )
}
