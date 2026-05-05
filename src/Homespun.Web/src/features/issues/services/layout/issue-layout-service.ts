/**
 * TS port of `Fleece.Core.Services.GraphLayout.IssueLayoutService` (v3.0.0).
 *
 * Wraps the generic `GraphLayoutService` with issue-specific filtering:
 *
 * - `layoutForTree` filters out terminal issues (unless visibility is
 *   `always` or `ifHasActiveDescendants`), then walks active parent
 *   chains to pull in ancestors, then runs the engine in `issueGraph`
 *   mode.
 * - `layoutForNext` starts from a `matchedIds` seed set and walks parent
 *   chains to collect ancestors. Useful for "what should I work on next?"
 *   views.
 *
 * Cycles in the parent chain throw a synthetic `InvalidGraphError` —
 * mirrors the C# `InvalidGraphException` so the calling layer can present
 * a degraded fallback view (flat list + banner).
 */

import { GraphLayoutService } from './graph-layout-service'
import type {
  GraphLayout,
  GraphLayoutRequest,
  GraphLayoutResult,
  GraphSortConfig,
  InactiveVisibility,
  LayoutMode,
} from './types'
import { DefaultGraphSortConfig } from './types'
import type { IssueLayoutNode, LayoutNode, LayoutPhase, PhaseLayoutNode } from './nodes'
import { phaseNodeId } from './nodes'

export type IssueStatus =
  | 'draft'
  | 'open'
  | 'progress'
  | 'review'
  | 'complete'
  | 'archived'
  | 'closed'
  | 'deleted'

export type ExecutionMode = 'series' | 'parallel'

export interface ParentIssueRef {
  readonly parentIssue: string
  readonly sortOrder?: string | null
  readonly active?: boolean
}

export interface LayoutIssue {
  readonly id: string
  readonly title?: string | null
  readonly description?: string | null
  readonly status: IssueStatus
  readonly executionMode?: ExecutionMode
  readonly parentIssues?: readonly ParentIssueRef[] | null
  readonly priority?: number | null
  readonly assignedTo?: string | null
  readonly createdAt?: string | null
}

export class InvalidGraphError extends Error {
  readonly cycle: readonly string[]
  constructor(cycle: readonly string[]) {
    super(`Graph contains a cycle: ${cycle.join(' -> ')}`)
    this.cycle = cycle
    this.name = 'InvalidGraphError'
  }
}

const DONE_STATUSES: readonly IssueStatus[] = ['complete', 'archived', 'closed']
const TERMINAL_STATUSES: readonly IssueStatus[] = ['complete', 'archived', 'closed', 'deleted']

export const isDone = (s: IssueStatus): boolean => DONE_STATUSES.includes(s)
export const isTerminal = (s: IssueStatus): boolean => TERMINAL_STATUSES.includes(s)

const idEq = (a: string, b: string) => a.toLowerCase() === b.toLowerCase()

const activeParents = (issue: LayoutIssue): readonly ParentIssueRef[] =>
  (issue.parentIssues ?? []).filter((p) => p.active !== false && p.parentIssue)

function toIssueNode(issue: LayoutIssue): IssueLayoutNode {
  return {
    kind: 'issue',
    issue,
    id: issue.id,
    childSequencing: issue.executionMode === 'parallel' ? 'parallel' : 'series',
  }
}

function toPhaseNode(parentIssueId: string, phase: LayoutPhase): PhaseLayoutNode {
  return {
    kind: 'phase',
    id: phaseNodeId(parentIssueId, phase.name),
    childSequencing: 'series',
    parentIssueId,
    phase,
  }
}

/** Optional input keyed by issue id (case-insensitive lookup) → ordered phase chain. */
export type PhasesByIssueId = ReadonlyMap<string, readonly LayoutPhase[]>

export interface LayoutOptions {
  readonly visibility?: InactiveVisibility
  readonly assignedTo?: string | null
  readonly sort?: GraphSortConfig | null
  readonly mode?: LayoutMode
  readonly phases?: PhasesByIssueId | null
}

function buildIssueLookup(issues: readonly LayoutIssue[]): Map<string, LayoutIssue> {
  const out = new Map<string, LayoutIssue>()
  for (const i of issues) out.set(i.id.toLowerCase(), i)
  return out
}

function buildChildrenLookup(
  display: readonly LayoutIssue[],
  displayLookup: Map<string, LayoutIssue>
): Map<string, LayoutIssue[]> {
  const out = new Map<string, LayoutIssue[]>()
  for (const issue of display) {
    for (const p of activeParents(issue)) {
      if (!displayLookup.has(p.parentIssue.toLowerCase())) continue
      const k = p.parentIssue.toLowerCase()
      let bucket = out.get(k)
      if (!bucket) {
        bucket = []
        out.set(k, bucket)
      }
      bucket.push(issue)
    }
  }
  // Sort children of each parent by their SortOrder under that parent.
  for (const [parentId, children] of out) {
    children.sort((a, b) => {
      const aSort = activeParents(a).find((p) => idEq(p.parentIssue, parentId))?.sortOrder ?? ''
      const bSort = activeParents(b).find((p) => idEq(p.parentIssue, parentId))?.sortOrder ?? ''
      // Ordinal string compare matches `string.Compare(a, b, Ordinal)`.
      if (aSort < bSort) return -1
      if (aSort > bSort) return 1
      return 0
    })
  }
  return out
}

function applyGraphSort(issues: LayoutIssue[], config: GraphSortConfig): void {
  if (config.rules.length === 0) return
  issues.sort((a, b) => {
    for (const rule of config.rules) {
      let cmp = 0
      switch (rule.criteria) {
        case 'createdAt':
          cmp = (a.createdAt ?? '').localeCompare(b.createdAt ?? '')
          break
        case 'priority': {
          const ap = a.priority ?? 99
          const bp = b.priority ?? 99
          cmp = ap < bp ? -1 : ap > bp ? 1 : 0
          break
        }
        case 'hasDescription': {
          const ad = !a.description?.trim() ? 1 : 0
          const bd = !b.description?.trim() ? 1 : 0
          cmp = ad - bd
          break
        }
        case 'title': {
          const at = a.title ?? ''
          const bt = b.title ?? ''
          cmp = at < bt ? -1 : at > bt ? 1 : 0
          break
        }
      }
      if (rule.direction === 'descending') cmp = -cmp
      if (cmp !== 0) return cmp
    }
    return 0
  })
}

function collectIssuesToDisplay(
  active: readonly LayoutIssue[],
  fullLookup: Map<string, LayoutIssue>
): LayoutIssue[] {
  const inSet = new Set<string>()
  for (const a of active) inSet.add(a.id.toLowerCase())

  const queue: LayoutIssue[] = [...active]
  const visited = new Set<string>()
  while (queue.length > 0) {
    const issue = queue.shift()!
    const key = issue.id.toLowerCase()
    if (visited.has(key)) continue
    visited.add(key)
    for (const p of activeParents(issue)) {
      const pk = p.parentIssue.toLowerCase()
      const parent = fullLookup.get(pk)
      if (parent && !inSet.has(pk)) {
        inSet.add(pk)
        queue.push(parent)
      }
    }
  }

  const out: LayoutIssue[] = []
  const emitted = new Set<string>()
  for (const a of active) {
    out.push(a)
    emitted.add(a.id.toLowerCase())
  }
  for (const id of inSet) {
    if (emitted.has(id)) continue
    const parent = fullLookup.get(id)
    if (parent && isTerminal(parent.status)) {
      out.push(parent)
    }
  }
  return out
}

function collectTerminalIssuesWithActiveDescendants(
  activeIssues: readonly LayoutIssue[],
  fullLookup: Map<string, LayoutIssue>
): LayoutIssue[] {
  const activeIds = new Set<string>()
  for (const a of activeIssues) activeIds.add(a.id.toLowerCase())

  const childrenOf = new Map<string, string[]>()
  for (const issue of fullLookup.values()) {
    for (const p of activeParents(issue)) {
      const k = p.parentIssue.toLowerCase()
      let list = childrenOf.get(k)
      if (!list) {
        list = []
        childrenOf.set(k, list)
      }
      list.push(issue.id.toLowerCase())
    }
  }

  const checked = new Map<string, boolean>()
  const hasActive = (id: string): boolean => {
    const cached = checked.get(id)
    if (cached !== undefined) return cached
    checked.set(id, false)
    const kids = childrenOf.get(id)
    if (!kids) return false
    for (const k of kids) {
      if (activeIds.has(k) || hasActive(k)) {
        checked.set(id, true)
        return true
      }
    }
    return false
  }

  const out: LayoutIssue[] = []
  for (const issue of fullLookup.values()) {
    const k = issue.id.toLowerCase()
    if (isTerminal(issue.status) && !activeIds.has(k) && hasActive(k)) {
      out.push(issue)
    }
  }
  return out
}

function collectMatchedAndAncestors(
  matched: readonly LayoutIssue[],
  matchedIds: ReadonlySet<string>,
  fullLookup: Map<string, LayoutIssue>
): LayoutIssue[] {
  const matchedKeys = new Set<string>()
  for (const m of matchedIds) matchedKeys.add(m.toLowerCase())

  const ancestors = new Set<string>()
  const queue: LayoutIssue[] = [...matched]
  const visited = new Set<string>()
  while (queue.length > 0) {
    const issue = queue.shift()!
    const key = issue.id.toLowerCase()
    if (visited.has(key)) continue
    visited.add(key)
    for (const p of activeParents(issue)) {
      const pk = p.parentIssue.toLowerCase()
      const parent = fullLookup.get(pk)
      if (parent && !matchedKeys.has(pk) && !ancestors.has(pk)) {
        ancestors.add(pk)
        queue.push(parent)
      }
    }
  }

  const out: LayoutIssue[] = [...matched]
  for (const id of ancestors) {
    const parent = fullLookup.get(id)
    if (parent) out.push(parent)
  }
  return out
}

function findParentCycle(
  issues: readonly LayoutIssue[],
  childrenOf: Map<string, LayoutIssue[]>
): readonly string[] | null {
  const visiting = new Set<string>()
  const visited = new Set<string>()
  const pathStack: string[] = []

  const dfs = (id: string): readonly string[] | null => {
    if (visiting.has(id)) {
      const ix = pathStack.findIndex((s) => idEq(s, id))
      const cycle = ix >= 0 ? pathStack.slice(ix) : [id]
      return [...cycle, id]
    }
    if (visited.has(id)) return null
    visiting.add(id)
    pathStack.push(id)
    const kids = childrenOf.get(id.toLowerCase())
    if (kids) {
      for (const child of kids) {
        const cycle = dfs(child.id)
        if (cycle !== null) return cycle
      }
    }
    pathStack.pop()
    visiting.delete(id)
    visited.add(id)
    return null
  }

  for (const issue of issues) {
    const cycle = dfs(issue.id)
    if (cycle !== null) return cycle
  }
  return null
}

function getIncompleteChildrenForLayout(
  issue: LayoutIssue,
  childrenOf: Map<string, LayoutIssue[]>
): LayoutIssue[] {
  const kids = childrenOf.get(issue.id.toLowerCase())
  if (!kids) return []
  const hasActive = (i: LayoutIssue): boolean => {
    if (!isDone(i.status)) return true
    const grandKids = childrenOf.get(i.id.toLowerCase())
    if (!grandKids) return false
    return grandKids.some(hasActive)
  }
  return kids.filter(hasActive)
}

function emptyLayout(): GraphLayout<LayoutNode> {
  return { nodes: [], edges: [], totalRows: 0, totalLanes: 0 }
}

export class IssueLayoutService {
  private readonly engine: GraphLayoutService

  constructor(engine?: GraphLayoutService) {
    this.engine = engine ?? new GraphLayoutService()
  }

  layoutForTree(
    issues: readonly LayoutIssue[],
    visibility: InactiveVisibility = 'hide',
    assignedTo: string | null = null,
    sort: GraphSortConfig | null = null,
    mode: LayoutMode = 'issueGraph',
    phases: PhasesByIssueId | null = null
  ): GraphLayoutResult<LayoutNode> {
    if (issues.length === 0) {
      return { ok: true, layout: emptyLayout() }
    }
    const fullLookup = buildIssueLookup(issues)
    const filtered: LayoutIssue[] = issues.filter(
      (i) =>
        (visibility === 'always' || !isTerminal(i.status)) &&
        (assignedTo === null ||
          (typeof i.assignedTo === 'string' &&
            i.assignedTo.toLowerCase() === assignedTo.toLowerCase()))
    )
    if (filtered.length === 0) {
      return { ok: true, layout: emptyLayout() }
    }
    if (visibility === 'ifHasActiveDescendants') {
      filtered.push(...collectTerminalIssuesWithActiveDescendants(filtered, fullLookup))
    }
    const display = collectIssuesToDisplay(filtered, fullLookup)
    return this.runEngine(display, sort, mode, phases)
  }

  layoutForNext(
    issues: readonly LayoutIssue[],
    matchedIds: ReadonlySet<string> | null = null,
    visibility: InactiveVisibility = 'hide',
    assignedTo: string | null = null,
    sort: GraphSortConfig | null = null,
    mode: LayoutMode = 'issueGraph',
    phases: PhasesByIssueId | null = null
  ): GraphLayoutResult<LayoutNode> {
    if (matchedIds === null) {
      return this.layoutForTree(issues, visibility, assignedTo, sort, mode, phases)
    }
    if (issues.length === 0 || matchedIds.size === 0) {
      return { ok: true, layout: emptyLayout() }
    }
    const fullLookup = buildIssueLookup(issues)
    const matched: LayoutIssue[] = []
    for (const id of matchedIds) {
      const found = fullLookup.get(id.toLowerCase())
      if (found) matched.push(found)
    }
    if (matched.length === 0) {
      return { ok: true, layout: emptyLayout() }
    }
    const display = collectMatchedAndAncestors(matched, matchedIds, fullLookup)
    return this.runEngine(display, sort, mode, phases)
  }

  private runEngine(
    display: LayoutIssue[],
    sort: GraphSortConfig | null,
    mode: LayoutMode,
    phases: PhasesByIssueId | null
  ): GraphLayoutResult<LayoutNode> {
    const displayLookup = buildIssueLookup(display)
    const childrenOf = buildChildrenLookup(display, displayLookup)
    const roots = display.filter((i) => {
      const parents = activeParents(i)
      if (parents.length === 0) return true
      return parents.every((p) => !displayLookup.has(p.parentIssue.toLowerCase()))
    })
    applyGraphSort(roots, sort ?? DefaultGraphSortConfig)

    if (roots.length === 0) {
      if (display.length > 0) {
        const cycle = findParentCycle(display, childrenOf)
        if (cycle !== null) {
          throw new InvalidGraphError(cycle)
        }
      }
      return { ok: true, layout: emptyLayout() }
    }

    // Synthesise one PhaseLayoutNode per phase per issue with phases. Phase
    // nodes join the engine's input set as the LAST children (siblings) of
    // their owning issue, in chain order (phase[0], phase[1], …). All phase
    // nodes are leaves (childIterator returns nothing). The engine then
    // assigns rows/lanes/edges using the same rules it uses for real issues:
    // series-mode parents stack leaf siblings into a single column;
    // normalTree puts all children at parent.lane + 1.
    const phaseNodesByIssueId = new Map<string, PhaseLayoutNode[]>()
    if (phases) {
      for (const issue of display) {
        const list = phases.get(issue.id.toLowerCase())
        if (!list || list.length === 0) continue
        phaseNodesByIssueId.set(
          issue.id.toLowerCase(),
          list.map((p) => toPhaseNode(issue.id, p))
        )
      }
    }

    const nodeMap = new Map<string, LayoutNode>()
    for (const issue of display) {
      nodeMap.set(issue.id.toLowerCase(), toIssueNode(issue))
    }
    for (const chain of phaseNodesByIssueId.values()) {
      for (const phaseNode of chain) {
        nodeMap.set(phaseNode.id.toLowerCase(), phaseNode)
      }
    }
    const allNodes: LayoutNode[] = []
    for (const issue of display) {
      allNodes.push(nodeMap.get(issue.id.toLowerCase())!)
      const chain = phaseNodesByIssueId.get(issue.id.toLowerCase())
      if (chain) allNodes.push(...chain)
    }
    const rootNodes = roots.map((i) => nodeMap.get(i.id.toLowerCase())!)

    const request: GraphLayoutRequest<LayoutNode> = {
      allNodes,
      rootFinder: () => rootNodes,
      childIterator: (parent) => {
        if (parent.kind === 'phase') return []
        const issue = parent.issue
        const kids = getIncompleteChildrenForLayout(issue, childrenOf)
        const childNodes: LayoutNode[] = kids.map((k) => nodeMap.get(k.id.toLowerCase())!)
        // Phases appended LAST so series-mode parents emit them after real
        // children. They are leaves, so series-mode leaf-stacking puts them
        // in a single column at the lane that was last emitted.
        const chain = phaseNodesByIssueId.get(issue.id.toLowerCase())
        if (chain && chain.length > 0) childNodes.push(...chain)
        return childNodes
      },
      mode,
    }

    const result = this.engine.layout(request)
    if (!result.ok) {
      throw new InvalidGraphError(result.cycle)
    }

    return {
      ok: true,
      layout: {
        totalRows: result.layout.totalRows,
        totalLanes: result.layout.totalLanes,
        nodes: result.layout.nodes,
        edges: result.layout.edges,
      },
    }
  }
}

const defaultService = new IssueLayoutService()

export function layoutForTree(
  issues: readonly LayoutIssue[],
  options?: LayoutOptions
): GraphLayoutResult<LayoutNode> {
  return defaultService.layoutForTree(
    issues,
    options?.visibility ?? 'hide',
    options?.assignedTo ?? null,
    options?.sort ?? null,
    options?.mode ?? 'issueGraph',
    options?.phases ?? null
  )
}

export function layoutForNext(
  issues: readonly LayoutIssue[],
  matchedIds: ReadonlySet<string> | null,
  options?: LayoutOptions
): GraphLayoutResult<LayoutNode> {
  return defaultService.layoutForNext(
    issues,
    matchedIds,
    options?.visibility ?? 'hide',
    options?.assignedTo ?? null,
    options?.sort ?? null,
    options?.mode ?? 'issueGraph',
    options?.phases ?? null
  )
}
