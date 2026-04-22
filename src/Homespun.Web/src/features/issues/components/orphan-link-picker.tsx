/**
 * Filterable picker dialog for selecting an issue to link an orphan
 * change to (or to choose as a parent for a new sub-issue).
 *
 * Pinned block at the top surfaces issues whose branch already carries
 * the orphan; the filter input narrows only the lower list. Pinned rows
 * also appear in their normal sorted position below the divider, so a
 * filter query for a highlighted issue does not remove it from the
 * pinned section.
 */

import { useState, useMemo } from 'react'
import { Dialog, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { IssueRowContent } from './issue-row-content'
import type { TaskGraphIssueRenderLine } from '../services'
import type { IssueOpenSpecState } from '@/api/generated/types.gen'
import { matchesTitleFilter } from '../services/fuzzy-title-filter'
import { cn } from '@/lib/utils'

export interface OrphanLinkPickerProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  title: string
  projectId: string
  issues: TaskGraphIssueRenderLine[]
  /** IDs of issues whose branch carries the orphan — pinned at the top. */
  containingIssueIds: string[]
  /** Map of openspec states keyed by issue id (optional). */
  openSpecStates?: Record<string, IssueOpenSpecState | undefined>
  onSelect: (issueId: string) => void
}

function PickerRow({
  line,
  projectId,
  openSpecState,
  onClick,
}: {
  line: TaskGraphIssueRenderLine
  projectId: string
  openSpecState?: IssueOpenSpecState | null
  onClick: () => void
}) {
  return (
    <button
      type="button"
      data-testid={`orphan-picker-row-${line.issueId}`}
      data-issue-id={line.issueId}
      onClick={onClick}
      className={cn(
        'hover:bg-muted/50 focus-visible:ring-ring w-full text-left focus-visible:ring-2',
        'flex items-center'
      )}
    >
      <IssueRowContent
        line={line}
        projectId={projectId}
        openSpecState={openSpecState}
        editable={false}
        showPrStatus={false}
      />
    </button>
  )
}

export function OrphanLinkPicker({
  open,
  onOpenChange,
  title,
  projectId,
  issues,
  containingIssueIds,
  openSpecStates,
  onSelect,
}: OrphanLinkPickerProps) {
  const [filter, setFilter] = useState('')

  const pinnedSet = useMemo(() => new Set(containingIssueIds), [containingIssueIds])
  const pinnedIssues = useMemo(
    () =>
      containingIssueIds
        .map((id) => issues.find((i) => i.issueId === id))
        .filter((i): i is TaskGraphIssueRenderLine => !!i),
    [issues, containingIssueIds]
  )

  const filteredIssues = useMemo(
    () => issues.filter((i) => matchesTitleFilter(i.title, filter)),
    [issues, filter]
  )

  const handleSelect = (issueId: string) => {
    onSelect(issueId)
    onOpenChange(false)
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-2xl">
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
        </DialogHeader>

        <Input
          data-testid="orphan-picker-filter"
          placeholder="Filter by title…"
          value={filter}
          onChange={(e) => setFilter(e.target.value)}
          autoFocus
        />

        {issues.length === 0 ? (
          <p className="text-muted-foreground py-4 text-sm">No issues in this project.</p>
        ) : (
          <div className="max-h-[60vh] overflow-y-auto">
            {pinnedIssues.length > 0 && (
              <>
                <ul data-testid="orphan-picker-pinned" className="flex flex-col">
                  {pinnedIssues.map((line) => (
                    <li key={`pinned-${line.issueId}`}>
                      <PickerRow
                        line={line}
                        projectId={projectId}
                        openSpecState={openSpecStates?.[line.issueId] ?? null}
                        onClick={() => handleSelect(line.issueId)}
                      />
                    </li>
                  ))}
                </ul>
                <div
                  data-testid="orphan-picker-divider"
                  className="border-border my-1 border-t"
                  role="separator"
                />
              </>
            )}

            {filteredIssues.length === 0 ? (
              <p className="text-muted-foreground py-4 text-sm">No matches.</p>
            ) : (
              <ul data-testid="orphan-picker-list" className="flex flex-col">
                {filteredIssues.map((line) => (
                  <li
                    key={`list-${line.issueId}`}
                    data-pinned={pinnedSet.has(line.issueId) ? 'true' : 'false'}
                  >
                    <PickerRow
                      line={line}
                      projectId={projectId}
                      openSpecState={openSpecStates?.[line.issueId] ?? null}
                      onClick={() => handleSelect(line.issueId)}
                    />
                  </li>
                ))}
              </ul>
            )}
          </div>
        )}
      </DialogContent>
    </Dialog>
  )
}
