import { useState, useMemo, useCallback } from 'react'
import { type IssueDiffResponse, type IssueChangeDto, ChangeType } from '@/api'
import { Badge } from '@/components/ui/badge'
import { StaticTaskGraphView, type FilteredIssue } from '@/features/issues'
import { IssueChangeDetailPanel } from './issue-change-detail-panel'

export interface IssueDiffViewProps {
  /** The diff data containing both graphs and changes */
  diff: IssueDiffResponse
}

/**
 * Single graph view showing all changed issues with color-coded styling.
 * Click on an issue to see detailed change information below.
 *
 * Uses StaticTaskGraphView with pre-fetched graph data from the API response.
 * Shows all changes in one graph with visual indicators:
 * - Green border: created issues
 * - Yellow border: updated issues
 * - Red border with strikethrough: deleted issues
 */
export function IssueDiffView({ diff }: IssueDiffViewProps) {
  const { changes, summary, sessionBranchGraph } = diff
  const [selectedIssueId, setSelectedIssueId] = useState<string | null>(null)

  const createdCount = summary?.created ?? 0
  const updatedCount = summary?.updated ?? 0
  const deletedCount = summary?.deleted ?? 0

  // Build filter array for all changed issues
  const allChangesFilter = useMemo((): FilteredIssue[] => {
    if (!changes) return []

    return changes
      .map((change) => ({
        issueId: change.issueId ?? '',
        changeType: mapChangeType(change.changeType),
      }))
      .filter((item) => item.issueId)
  }, [changes])

  // Find the selected change for the detail panel
  const selectedChange = useMemo((): IssueChangeDto | null => {
    if (!selectedIssueId || !changes) return null
    return changes.find((c) => c.issueId?.toLowerCase() === selectedIssueId.toLowerCase()) ?? null
  }, [selectedIssueId, changes])

  const handleSelectIssue = useCallback((issueId: string) => {
    setSelectedIssueId((prev) => (prev === issueId ? null : issueId))
  }, [])

  const handleCloseDetail = useCallback(() => {
    setSelectedIssueId(null)
  }, [])

  return (
    <div className="flex h-full flex-col gap-4">
      {/* Summary bar */}
      <div className="flex items-center gap-2">
        <span className="text-muted-foreground text-sm">Changes:</span>
        {createdCount > 0 && (
          <Badge
            variant="outline"
            className="border-green-500 bg-green-50 text-green-700 dark:bg-green-950/50"
          >
            +{createdCount} created
          </Badge>
        )}
        {updatedCount > 0 && (
          <Badge
            variant="outline"
            className="border-yellow-500 bg-yellow-50 text-yellow-700 dark:bg-yellow-950/50"
          >
            {updatedCount} updated
          </Badge>
        )}
        {deletedCount > 0 && (
          <Badge
            variant="outline"
            className="border-red-500 bg-red-50 text-red-700 dark:bg-red-950/50"
          >
            -{deletedCount} deleted
          </Badge>
        )}
        {createdCount === 0 && updatedCount === 0 && deletedCount === 0 && (
          <span className="text-muted-foreground text-sm">No changes</span>
        )}
      </div>

      {/* Single graph showing all changes */}
      <div className="flex min-h-0 flex-1 flex-col overflow-hidden rounded-lg border">
        <div className="bg-muted/50 border-b px-3 py-2">
          <h3 className="text-sm font-medium">Your Changes</h3>
        </div>
        <div className="flex-1 overflow-auto p-2">
          <StaticTaskGraphView
            data={sessionBranchGraph}
            filterIssueIds={allChangesFilter}
            depth={10}
            selectedIssueId={selectedIssueId}
            onSelectIssue={handleSelectIssue}
            data-testid="session-branch-graph"
          />
        </div>
      </div>

      {/* Change details panel (shown when an issue is selected) */}
      {selectedChange && (
        <IssueChangeDetailPanel change={selectedChange} onClose={handleCloseDetail} />
      )}
    </div>
  )
}

function mapChangeType(changeType: ChangeType): 'created' | 'updated' | 'deleted' {
  switch (changeType) {
    case ChangeType.CREATED:
      return 'created'
    case ChangeType.UPDATED:
      return 'updated'
    case ChangeType.DELETED:
      return 'deleted'
    default:
      return 'updated'
  }
}
