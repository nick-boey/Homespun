import { useMemo } from 'react'
import { type IssueDiffResponse, type IssueChangeDto, ChangeType } from '@/api'
import { cn } from '@/lib/utils'
import { Badge } from '@/components/ui/badge'
import { StaticTaskGraphView, type FilteredIssue } from '@/features/issues'

export interface IssueDiffViewProps {
  /** The diff data containing both graphs and changes */
  diff: IssueDiffResponse
}

/**
 * Side-by-side comparison of main branch and session branch issue graphs.
 * Shows changes highlighted with colors.
 *
 * Uses StaticTaskGraphView with pre-fetched graph data from the API response.
 * Filters each graph to only show relevant changes:
 * - Main branch: shows deleted issues (styled red)
 * - Session branch: shows created (green) and updated (yellow) issues
 */
export function IssueDiffView({ diff }: IssueDiffViewProps) {
  const { changes, summary, mainBranchGraph, sessionBranchGraph } = diff

  const createdCount = summary?.created ?? 0
  const updatedCount = summary?.updated ?? 0
  const deletedCount = summary?.deleted ?? 0

  // Build filter arrays for each graph
  const mainBranchFilter = useMemo((): FilteredIssue[] => {
    if (!changes) return []

    // Main branch shows deleted issues only
    return changes
      .filter((change) => change.changeType === ChangeType.DELETED)
      .map((change) => ({
        issueId: change.issueId ?? '',
        changeType: 'deleted' as const,
      }))
      .filter((item) => item.issueId)
  }, [changes])

  const sessionBranchFilter = useMemo((): FilteredIssue[] => {
    if (!changes) return []

    // Session branch shows created and updated issues
    return changes
      .filter(
        (change) =>
          change.changeType === ChangeType.CREATED || change.changeType === ChangeType.UPDATED
      )
      .map((change) => ({
        issueId: change.issueId ?? '',
        changeType:
          change.changeType === ChangeType.CREATED ? ('created' as const) : ('updated' as const),
      }))
      .filter((item) => item.issueId)
  }, [changes])

  return (
    <div className="flex h-full flex-col gap-4">
      {/* Summary bar */}
      <div className="flex items-center gap-2">
        <span className="text-muted-foreground text-sm">Changes:</span>
        {createdCount > 0 && (
          <Badge variant="outline" className="border-green-500 bg-green-50 text-green-700">
            +{createdCount} created
          </Badge>
        )}
        {updatedCount > 0 && (
          <Badge variant="outline" className="border-yellow-500 bg-yellow-50 text-yellow-700">
            {updatedCount} updated
          </Badge>
        )}
        {deletedCount > 0 && (
          <Badge variant="outline" className="border-red-500 bg-red-50 text-red-700">
            -{deletedCount} deleted
          </Badge>
        )}
        {createdCount === 0 && updatedCount === 0 && deletedCount === 0 && (
          <span className="text-muted-foreground text-sm">No changes</span>
        )}
      </div>

      {/* Side-by-side graphs */}
      <div className="grid min-h-0 flex-1 grid-cols-2 gap-4">
        {/* Main branch */}
        <div className="flex flex-col overflow-hidden rounded-lg border">
          <div className="bg-muted/50 border-b px-3 py-2">
            <h3 className="text-sm font-medium">Main Branch (current)</h3>
          </div>
          <div className="flex-1 overflow-auto p-2">
            <StaticTaskGraphView
              data={mainBranchGraph}
              filterIssueIds={mainBranchFilter}
              depth={10}
              data-testid="main-branch-graph"
            />
          </div>
        </div>

        {/* Session branch */}
        <div className="flex flex-col overflow-hidden rounded-lg border">
          <div className="bg-muted/50 border-b px-3 py-2">
            <h3 className="text-sm font-medium">Your Changes</h3>
          </div>
          <div className="flex-1 overflow-auto p-2">
            <StaticTaskGraphView
              data={sessionBranchGraph}
              filterIssueIds={sessionBranchFilter}
              depth={10}
              data-testid="session-branch-graph"
            />
          </div>
        </div>
      </div>

      {/* Change details */}
      {changes && changes.length > 0 && (
        <div className="rounded-lg border">
          <div className="bg-muted/50 border-b px-3 py-2">
            <h3 className="text-sm font-medium">Change Details</h3>
          </div>
          <div className="max-h-48 overflow-auto p-2">
            <ul className="space-y-1">
              {changes.map((change) => (
                <ChangeItem key={change.issueId} change={change} />
              ))}
            </ul>
          </div>
        </div>
      )}
    </div>
  )
}

function ChangeItem({ change }: { change: IssueChangeDto }) {
  const typeColors: Record<string, string> = {
    [ChangeType.CREATED]: 'bg-green-100 text-green-800 border-green-300',
    [ChangeType.UPDATED]: 'bg-yellow-100 text-yellow-800 border-yellow-300',
    [ChangeType.DELETED]: 'bg-red-100 text-red-800 border-red-300',
  }

  const typeLabels: Record<string, string> = {
    [ChangeType.CREATED]: 'Created',
    [ChangeType.UPDATED]: 'Updated',
    [ChangeType.DELETED]: 'Deleted',
  }

  const changeTypeKey = change.changeType ?? ''

  return (
    <li className="flex items-center gap-2 text-sm">
      <Badge variant="outline" className={cn('text-xs', typeColors[changeTypeKey] ?? '')}>
        {typeLabels[changeTypeKey] ?? 'Unknown'}
      </Badge>
      <span className="font-mono text-xs">{change.issueId}</span>
      {change.title && <span className="truncate">{change.title}</span>}
    </li>
  )
}
