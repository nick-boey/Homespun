/**
 * Renders orphan OpenSpec changes (those without a `.homespun.yaml` sidecar)
 * as a single deduplicated "Orphaned Changes" section at the bottom of the
 * task graph. Each row offers two actions:
 *
 * - `[🔗 Link to issue]` opens a picker pre-populated with the issues whose
 *   branches already carry the orphan; selecting an issue fans out one POST
 *   per occurrence so every clone gets a sidecar.
 * - `[+ Create issue ▾]` is a split button — primary creates a top-level
 *   issue and links all occurrences to it; the secondary dropdown item
 *   opens the same picker in "choose parent" mode so the newly created
 *   issue becomes a sub-issue under the picked parent.
 */

import { useState } from 'react'
import { Link2, Plus, ChevronDown, Loader2 } from 'lucide-react'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from '@/components/ui/tooltip'
import type { IssueOpenSpecState } from '@/api/generated/types.gen'
import { useLinkOrphan } from '../hooks/use-link-orphan'
import { useCreateIssue } from '../hooks/use-create-issue'
import type { OrphanEntry, TaskGraphIssueRenderLine } from '../services'
import { OrphanLinkPicker } from './orphan-link-picker'

export interface OrphanedChangesListProps {
  projectId: string
  entries: OrphanEntry[]
  issues: TaskGraphIssueRenderLine[]
  openSpecStates?: Record<string, IssueOpenSpecState | undefined>
}

type PickerMode = { kind: 'link'; entry: OrphanEntry } | { kind: 'parent'; entry: OrphanEntry }

function formatOccurrenceLabel(entry: OrphanEntry): string {
  if (entry.occurrences.length === 1) {
    return entry.occurrences[0]!.branch ?? 'main'
  }
  return `on ${entry.occurrences.length} branches`
}

function OccurrenceLabel({ entry }: { entry: OrphanEntry }) {
  const label = formatOccurrenceLabel(entry)

  if (entry.occurrences.length === 1) {
    return (
      <span
        data-testid="orphaned-change-occurrence-label"
        className="text-muted-foreground font-mono text-xs"
      >
        {label}
      </span>
    )
  }

  const branches = entry.occurrences.map((o) => o.branch ?? 'main')
  return (
    <TooltipProvider>
      <Tooltip>
        <TooltipTrigger asChild>
          <span
            data-testid="orphaned-change-occurrence-label"
            className="text-muted-foreground cursor-help font-mono text-xs underline decoration-dotted"
          >
            {label}
          </span>
        </TooltipTrigger>
        <TooltipContent>
          <ul className="flex flex-col gap-0.5">
            {branches.map((b) => (
              <li key={b}>{b}</li>
            ))}
          </ul>
        </TooltipContent>
      </Tooltip>
    </TooltipProvider>
  )
}

export function OrphanedChangesList({
  projectId,
  entries,
  issues,
  openSpecStates,
}: OrphanedChangesListProps) {
  const link = useLinkOrphan()
  const createIssue = useCreateIssue({ projectId })
  const [busy, setBusy] = useState<string | null>(null)
  const [picker, setPicker] = useState<PickerMode | null>(null)

  if (entries.length === 0) return null

  async function handleLinkSelect(entry: OrphanEntry, issueId: string) {
    setBusy(entry.name)
    try {
      await link.mutateAsync({
        projectId,
        changeName: entry.name,
        fleeceId: issueId,
      })
    } finally {
      setBusy(null)
      setPicker(null)
    }
  }

  async function handleCreateIssue(entry: OrphanEntry, parentIssueId?: string) {
    setBusy(entry.name)
    try {
      const issue = await createIssue.createIssue({
        title: `OpenSpec: ${entry.name}`,
        ...(parentIssueId ? { parentIssueId } : {}),
      })
      if (issue?.id) {
        await link.mutateAsync({
          projectId,
          changeName: entry.name,
          fleeceId: issue.id,
        })
      }
    } finally {
      setBusy(null)
      setPicker(null)
    }
  }

  return (
    <>
      <section className="border-border mt-4 border-t pt-3" data-testid="orphaned-changes-section">
        <h3 className="text-muted-foreground px-2 text-xs font-medium tracking-wide uppercase">
          Orphaned Changes
        </h3>
        <ul className="mt-1 flex flex-col gap-1 px-2 text-sm">
          {entries.map((entry) => {
            const isBusy = busy === entry.name
            return (
              <li
                key={entry.name}
                className="flex items-center gap-2"
                data-testid="orphaned-change-row"
                data-change-name={entry.name}
              >
                <OccurrenceLabel entry={entry} />
                <span className="flex-1 truncate">{entry.name}</span>
                <Button
                  size="sm"
                  variant="ghost"
                  className="h-6 gap-1 px-2 text-xs"
                  disabled={isBusy}
                  onClick={() => setPicker({ kind: 'link', entry })}
                  data-testid="orphan-link-to-issue"
                >
                  {isBusy ? (
                    <Loader2 className="h-3 w-3 animate-spin" />
                  ) : (
                    <Link2 className="h-3 w-3" />
                  )}
                  Link to issue
                </Button>
                <div className="flex items-center">
                  <Button
                    size="sm"
                    variant="ghost"
                    className="h-6 gap-1 rounded-r-none px-2 text-xs"
                    disabled={isBusy}
                    onClick={() => handleCreateIssue(entry)}
                    data-testid="orphan-create-issue"
                  >
                    <Plus className="h-3 w-3" /> Create issue
                  </Button>
                  <DropdownMenu>
                    <DropdownMenuTrigger asChild>
                      <Button
                        size="sm"
                        variant="ghost"
                        className="h-6 rounded-l-none border-l px-1.5 text-xs"
                        disabled={isBusy}
                        aria-label="More create options"
                        data-testid="orphan-create-issue-menu"
                      >
                        <ChevronDown className="h-3 w-3" />
                      </Button>
                    </DropdownMenuTrigger>
                    <DropdownMenuContent align="end">
                      <DropdownMenuItem
                        onSelect={() => setPicker({ kind: 'parent', entry })}
                        data-testid="orphan-create-sub-issue-menuitem"
                      >
                        Create as sub-issue under…
                      </DropdownMenuItem>
                    </DropdownMenuContent>
                  </DropdownMenu>
                </div>
              </li>
            )
          })}
        </ul>
      </section>

      {picker && (
        <OrphanLinkPicker
          open
          onOpenChange={(open) => {
            if (!open) setPicker(null)
          }}
          title={
            picker.kind === 'link'
              ? `Link "${picker.entry.name}" to an issue`
              : `Choose a parent for "${picker.entry.name}"`
          }
          projectId={projectId}
          issues={issues}
          containingIssueIds={picker.entry.containingIssueIds}
          openSpecStates={openSpecStates}
          onSelect={(issueId) => {
            if (picker.kind === 'link') {
              handleLinkSelect(picker.entry, issueId)
            } else {
              handleCreateIssue(picker.entry, issueId)
            }
          }}
        />
      )}
    </>
  )
}
