import type {
  IssueOpenSpecState,
  IssueResponse,
  SnapshotOrphan,
  TaskGraphNodeResponse,
  TaskGraphResponse,
} from '@/api/generated/types.gen'
import { generateBranchName } from './branch-name'

export interface OrphanOccurrence {
  /** Branch name the orphan lives on, or null for main. */
  branch: string | null
  changeName: string
}

export interface OrphanEntry {
  name: string
  occurrences: OrphanOccurrence[]
  /** IDs of issues whose branch currently carries the orphan. */
  containingIssueIds: string[]
}

/**
 * Aggregate orphan OpenSpec changes across main and every branch that
 * carries them into a deduplicated list keyed by change name. Each row
 * carries the full occurrence list so the link action can fan out one
 * POST per clone.
 */
export function aggregateOrphans(taskGraph: TaskGraphResponse): OrphanEntry[] {
  const byName = new Map<string, OrphanEntry>()

  const ensure = (name: string): OrphanEntry => {
    let entry = byName.get(name)
    if (!entry) {
      entry = { name, occurrences: [], containingIssueIds: [] }
      byName.set(name, entry)
    }
    return entry
  }

  for (const orphan of taskGraph.mainOrphanChanges ?? []) {
    const name = orphan.name?.trim()
    if (!name) continue
    ensure(name).occurrences.push({ branch: null, changeName: name })
  }

  const openSpecStates = taskGraph.openSpecStates ?? {}
  const nodesById = new Map<string, TaskGraphNodeResponse>()
  for (const node of taskGraph.nodes ?? []) {
    if (node.issue?.id) nodesById.set(node.issue.id, node)
  }

  const issueIds = Object.keys(openSpecStates).sort()
  for (const issueId of issueIds) {
    const state = openSpecStates[issueId]
    const orphans = state?.orphans
    if (!orphans || orphans.length === 0) continue

    const node = nodesById.get(issueId)
    const branch = generateBranchName(node?.issue ?? null)
    if (!branch) continue

    for (const orphan of orphans) {
      const name = orphan.name?.trim()
      if (!name) continue
      const entry = ensure(name)
      entry.occurrences.push({ branch, changeName: name })
      if (!entry.containingIssueIds.includes(issueId)) {
        entry.containingIssueIds.push(issueId)
      }
    }
  }

  return Array.from(byName.values()).sort((a, b) => a.name.localeCompare(b.name))
}

/**
 * Variant that consumes the un-bundled per-endpoint payloads (matches the
 * client-side layout pipeline). Equivalent semantics to `aggregateOrphans`
 * but takes the raw lists/maps directly.
 */
export function aggregateOrphansFromInputs(input: {
  orphanChanges?: readonly SnapshotOrphan[] | null
  openSpecStates?: Record<string, IssueOpenSpecState> | null
  issues?: readonly IssueResponse[] | null
}): OrphanEntry[] {
  const byName = new Map<string, OrphanEntry>()
  const ensure = (name: string): OrphanEntry => {
    let entry = byName.get(name)
    if (!entry) {
      entry = { name, occurrences: [], containingIssueIds: [] }
      byName.set(name, entry)
    }
    return entry
  }

  for (const orphan of input.orphanChanges ?? []) {
    const name = orphan.name?.trim()
    if (!name) continue
    ensure(name).occurrences.push({ branch: null, changeName: name })
  }

  const openSpecStates = input.openSpecStates ?? {}
  const issuesById = new Map<string, IssueResponse>()
  for (const issue of input.issues ?? []) if (issue.id) issuesById.set(issue.id, issue)

  const issueIds = Object.keys(openSpecStates).sort()
  for (const issueId of issueIds) {
    const state = openSpecStates[issueId]
    const orphans = state?.orphans
    if (!orphans || orphans.length === 0) continue

    const issue = issuesById.get(issueId)
    const branch = generateBranchName(issue ?? null)
    if (!branch) continue

    for (const orphan of orphans) {
      const name = orphan.name?.trim()
      if (!name) continue
      const entry = ensure(name)
      entry.occurrences.push({ branch, changeName: name })
      if (!entry.containingIssueIds.includes(issueId)) {
        entry.containingIssueIds.push(issueId)
      }
    }
  }

  return Array.from(byName.values()).sort((a, b) => a.name.localeCompare(b.name))
}
