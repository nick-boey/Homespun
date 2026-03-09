import { useState, useMemo } from 'react'
import { AlertCircle, CheckCircle2, GitMerge, Info, XCircle, ChevronDown, ChevronRight } from 'lucide-react'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Button } from '@/components/ui/button'
import { ScrollArea } from '@/components/ui/scroll-area'
import { Badge } from '@/components/ui/badge'
import { Skeleton } from '@/components/ui/skeleton'
import { Alert, AlertDescription } from '@/components/ui/alert'
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from '@/components/ui/collapsible'
import { cn } from '@/lib/utils'
import { useApplyAgentChanges, usePreviewAgentChanges } from '../hooks/use-apply-agent-changes'
import type { ChangeType, IssueChangeDto, IssueConflictDto } from '@/api/generated'

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
        conflictStrategy: preview?.conflicts?.length ? 'Manual' : 'AgentWins',
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
      createdCount: preview.changes.filter((c) => c.changeType === 'Created').length,
      updatedCount: preview.changes.filter((c) => c.changeType === 'Updated').length,
      deletedCount: preview.changes.filter((c) => c.changeType === 'Deleted').length,
    }
  }, [preview])

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-3xl max-h-[90vh]">
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
            <Alert variant="destructive">
              <AlertCircle className="h-4 w-4" />
              <AlertDescription>{error.message}</AlertDescription>
            </Alert>
          )}

          {preview && !preview.changes?.length && (
            <Alert>
              <Info className="h-4 w-4" />
              <AlertDescription>No changes detected from the agent session.</AlertDescription>
            </Alert>
          )}

          {preview?.conflicts && preview.conflicts.length > 0 && (
            <Alert variant="destructive">
              <AlertCircle className="h-4 w-4" />
              <AlertDescription>
                {preview.conflicts.length} conflict{preview.conflicts.length > 1 ? 's' : ''} detected.
                Manual resolution required.
              </AlertDescription>
            </Alert>
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

              <ScrollArea className="h-[400px] rounded-md border p-4">
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
              </ScrollArea>
            </>
          )}

          {preview?.conflicts && preview.conflicts.length > 0 && (
            <div className="space-y-2">
              <h4 className="text-sm font-semibold">Conflicts</h4>
              <ScrollArea className="h-[200px] rounded-md border p-4">
                <div className="space-y-3">
                  {preview.conflicts.map((conflict, index) => (
                    <ConflictItem key={conflict.issueId || index} conflict={conflict} />
                  ))}
                </div>
              </ScrollArea>
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
              (preview.conflicts && preview.conflicts.length > 0)
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
      case 'Created':
        return <CheckCircle2 className="h-4 w-4 text-green-600" />
      case 'Updated':
        return <GitMerge className="h-4 w-4 text-blue-600" />
      case 'Deleted':
        return <XCircle className="h-4 w-4 text-red-600" />
      default:
        return null
    }
  }

  const getChangeBadge = (type: ChangeType | undefined) => {
    switch (type) {
      case 'Created':
        return <Badge className="bg-green-100 text-green-700">Created</Badge>
      case 'Updated':
        return <Badge className="bg-blue-100 text-blue-700">Updated</Badge>
      case 'Deleted':
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
          'flex w-full items-center gap-2 rounded-md border p-3 text-left transition-colors hover:bg-muted/50',
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
          <div className="mt-2 space-y-1 border-l-2 border-muted pl-4">
            {change.fieldChanges.map((field, index) => (
              <div key={index} className="text-sm">
                <span className="font-medium">{field.fieldName}:</span>
                {field.oldValue && (
                  <span className="text-muted-foreground line-through mx-1">
                    {field.oldValue}
                  </span>
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
    <div className="rounded-md border border-destructive/50 p-3">
      <div className="mb-2 flex items-center gap-2">
        <AlertCircle className="h-4 w-4 text-destructive" />
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