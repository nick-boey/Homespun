/**
 * Renders orphan OpenSpec changes (those without a `.homespun.yaml` sidecar)
 * in two contexts:
 *
 * 1. **Branch-scoped** — shown under the branch's issue row with `[link-to-issue]`
 *    and `[create-sub-issue]` actions. Linking writes a sidecar pointing at the
 *    parent issue or a new sub-issue.
 * 2. **Main-scoped** — shown as a flat "Orphaned Changes" section at the
 *    bottom of the graph with a `[create-issue]` action that creates a new
 *    top-level issue and links the change to it.
 */

import { useState } from 'react'
import { Link2, FilePlus, Plus, Loader2 } from 'lucide-react'
import { Button } from '@/components/ui/button'
import type { SnapshotOrphan } from '@/api/generated/types.gen'
import { useLinkOrphan } from '../hooks/use-link-orphan'
import { useCreateIssue } from '../hooks/use-create-issue'

export interface BranchOrphanListProps {
  projectId: string
  branch: string | null
  /** Fleece ID of the branch's owning issue (required for link + create-sub). */
  fleeceId: string | null
  orphans: SnapshotOrphan[] | null | undefined
}

export function BranchOrphanList({ projectId, branch, fleeceId, orphans }: BranchOrphanListProps) {
  const link = useLinkOrphan()
  const createIssue = useCreateIssue({ projectId })
  const [busy, setBusy] = useState<string | null>(null)

  if (!orphans || orphans.length === 0) return null

  async function handleLinkToIssue(orphan: SnapshotOrphan) {
    if (!fleeceId) return
    setBusy(orphan.name ?? '')
    try {
      await link.mutateAsync({
        projectId,
        branch,
        changeName: orphan.name ?? '',
        fleeceId,
      })
    } finally {
      setBusy(null)
    }
  }

  async function handleCreateSubIssue(orphan: SnapshotOrphan) {
    if (!fleeceId) return
    const name = orphan.name ?? ''
    setBusy(name)
    try {
      const issue = await createIssue.createIssue({
        title: `OpenSpec: ${name}`,
        parentIssueId: fleeceId,
      })
      if (issue?.id) {
        await link.mutateAsync({
          projectId,
          branch,
          changeName: name,
          fleeceId: issue.id,
        })
      }
    } finally {
      setBusy(null)
    }
  }

  return (
    <ul
      className="flex flex-col gap-1 pl-6 text-sm"
      data-testid="branch-orphan-list"
      data-branch={branch ?? ''}
    >
      {orphans.map((orphan) => {
        const name = orphan.name ?? ''
        const isBusy = busy === name
        return (
          <li
            key={name}
            className="flex items-center gap-2"
            data-testid="branch-orphan"
            data-change-name={name}
          >
            <span className="text-muted-foreground font-mono text-xs">orphan</span>
            <span className="flex-1 truncate">{name}</span>
            <Button
              size="sm"
              variant="ghost"
              className="h-6 gap-1 px-2 text-xs"
              disabled={isBusy || !fleeceId}
              onClick={() => handleLinkToIssue(orphan)}
              data-testid="orphan-link-to-issue"
            >
              {isBusy ? (
                <Loader2 className="h-3 w-3 animate-spin" />
              ) : (
                <Link2 className="h-3 w-3" />
              )}
              Link to issue
            </Button>
            <Button
              size="sm"
              variant="ghost"
              className="h-6 gap-1 px-2 text-xs"
              disabled={isBusy || !fleeceId}
              onClick={() => handleCreateSubIssue(orphan)}
              data-testid="orphan-create-sub-issue"
            >
              <FilePlus className="h-3 w-3" /> Create sub-issue
            </Button>
          </li>
        )
      })}
    </ul>
  )
}

export interface MainOrphanListProps {
  projectId: string
  orphans: SnapshotOrphan[] | null | undefined
}

export function MainOrphanList({ projectId, orphans }: MainOrphanListProps) {
  const link = useLinkOrphan()
  const createIssue = useCreateIssue({ projectId })
  const [busy, setBusy] = useState<string | null>(null)

  if (!orphans || orphans.length === 0) return null

  async function handleCreateIssue(orphan: SnapshotOrphan) {
    const name = orphan.name ?? ''
    setBusy(name)
    try {
      const issue = await createIssue.createIssue({ title: `OpenSpec: ${name}` })
      if (issue?.id) {
        await link.mutateAsync({
          projectId,
          branch: null,
          changeName: name,
          fleeceId: issue.id,
        })
      }
    } finally {
      setBusy(null)
    }
  }

  return (
    <section className="border-border mt-4 border-t pt-3" data-testid="main-orphans-section">
      <h3 className="text-muted-foreground px-2 text-xs font-medium tracking-wide uppercase">
        Orphaned Changes
      </h3>
      <ul className="mt-1 flex flex-col gap-1 px-2 text-sm">
        {orphans.map((orphan) => {
          const name = orphan.name ?? ''
          const isBusy = busy === name
          return (
            <li
              key={name}
              className="flex items-center gap-2"
              data-testid="main-orphan"
              data-change-name={name}
            >
              <span className="text-muted-foreground font-mono text-xs">main</span>
              <span className="flex-1 truncate">{name}</span>
              <Button
                size="sm"
                variant="ghost"
                className="h-6 gap-1 px-2 text-xs"
                disabled={isBusy}
                onClick={() => handleCreateIssue(orphan)}
                data-testid="orphan-create-issue"
              >
                {isBusy ? (
                  <Loader2 className="h-3 w-3 animate-spin" />
                ) : (
                  <Plus className="h-3 w-3" />
                )}
                Create issue
              </Button>
            </li>
          )
        })}
      </ul>
    </section>
  )
}
