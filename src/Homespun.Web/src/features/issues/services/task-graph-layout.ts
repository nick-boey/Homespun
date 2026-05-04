/**
 * Render-line + edge synthesis for TaskGraphView.
 *
 * Two entry points:
 *
 * - `computeLayout(taskGraph, …)` — legacy path that consumes a server-laid-out
 *   `TaskGraphResponse` (used by the static diff view). Each node already
 *   carries its row/lane/appearance position; the wrapper threads them through.
 *
 * - `computeLayoutFromIssues({ issues, decorations, viewMode, … })` — the
 *   new client-side path. Runs the TS port of `Fleece.Core`'s
 *   `IIssueLayoutService` against `IssueResponse[]` plus parallel decoration
 *   maps fetched per-endpoint, then synthesizes Homespun-only rows
 *   (PR rows, separators, "load more"). Returns the same shape as the legacy
 *   path so consumers don't change. Memoise at the call site (issue set +
 *   viewMode + filter is the layout key; decorations are render-only).
 */

import type {
  TaskGraphResponse,
  LinkedPr,
  AgentStatusData,
  IssueResponse,
  IssueOpenSpecState,
  PhaseTaskSummary,
  PullRequestWithTime,
  IssueType as IssueTypeEnum,
  IssueStatus as IssueStatusEnum,
  ExecutionMode as ExecutionModeEnum,
} from '@/api'
import { ExecutionMode, IssueStatus, IssueType } from '@/api'
import { generateBranchName } from './branch-name'
import { ViewMode } from '../types'
import {
  InvalidGraphError,
  layoutForNext,
  layoutForTree,
  type GraphLayoutResult,
  type GraphSortConfig,
  type LayoutIssue,
  type PositionedNode,
} from './layout'

export type TaskGraphEdge = {
  from: string
  to: string
  kind: 'SeriesSibling' | 'SeriesCornerToParent' | 'ParallelChildToSpine' | string
  startRow: number
  startLane: number
  endRow: number
  endLane: number
  pivotLane?: number | null
  sourceAttach: 'Top' | 'Bottom' | 'Left' | 'Right' | string
  targetAttach: 'Top' | 'Bottom' | 'Left' | 'Right' | string
}

export type TaskGraphLayoutResult = {
  lines: TaskGraphRenderLine[]
  edges: TaskGraphEdge[]
}

export const TaskGraphMarkerType = {
  Actionable: 'actionable',
  Open: 'open',
  Complete: 'complete',
  Closed: 'closed',
} as const

export type TaskGraphMarkerType = (typeof TaskGraphMarkerType)[keyof typeof TaskGraphMarkerType]

export interface TaskGraphIssueRenderLine {
  type: 'issue'
  issueId: string
  title: string
  description: string | null
  branchName: string | null
  lane: number
  marker: TaskGraphMarkerType
  issueType: IssueTypeEnum
  status: IssueStatusEnum
  hasDescription: boolean
  linkedPr: LinkedPr | null
  agentStatus: AgentStatusData | null
  assignedTo: string | null
  executionMode: ExecutionModeEnum
  parentIssues: Array<{ parentIssue?: string | null; sortOrder?: string | null }> | null
  parentIssueId: string | null
  appearanceIndex: number
  totalAppearances: number
}

export interface TaskGraphSeparatorRenderLine {
  type: 'separator'
}

export interface TaskGraphPrRenderLine {
  type: 'pr'
  prNumber: number
  title: string
  url: string | null
  isMerged: boolean
  hasDescription: boolean
  agentStatus: AgentStatusData | null
  drawTopLine: boolean
  drawBottomLine: boolean
}

export interface TaskGraphLoadMoreRenderLine {
  type: 'loadMore'
}

export interface TaskGraphPhaseRenderLine {
  type: 'phase'
  /** Stable id: `${issueId}::phase::${phaseName}`. Never collides with fleece issue ids. */
  phaseId: string
  parentIssueId: string
  lane: number
  phaseName: string
  done: number
  total: number
  tasks: PhaseTaskSummary[]
}

export type TaskGraphRenderLine =
  | TaskGraphIssueRenderLine
  | TaskGraphSeparatorRenderLine
  | TaskGraphPrRenderLine
  | TaskGraphLoadMoreRenderLine
  | TaskGraphPhaseRenderLine

export function isIssueRenderLine(line: TaskGraphRenderLine): line is TaskGraphIssueRenderLine {
  return line.type === 'issue'
}

export function isPhaseRenderLine(line: TaskGraphRenderLine): line is TaskGraphPhaseRenderLine {
  return line.type === 'phase'
}

export function isSeparatorRenderLine(
  line: TaskGraphRenderLine
): line is TaskGraphSeparatorRenderLine {
  return line.type === 'separator'
}

export function isPrRenderLine(line: TaskGraphRenderLine): line is TaskGraphPrRenderLine {
  return line.type === 'pr'
}

export function isLoadMoreRenderLine(
  line: TaskGraphRenderLine
): line is TaskGraphLoadMoreRenderLine {
  return line.type === 'loadMore'
}

/** Unique key that distinguishes multi-parent appearances of the same issue. */
export function getRenderKey(line: TaskGraphIssueRenderLine): string {
  return line.totalAppearances > 1 ? `${line.issueId}:${line.appearanceIndex}` : line.issueId
}

function getMarker(
  status: IssueStatusEnum | undefined,
  isActionable: boolean | undefined
): TaskGraphMarkerType {
  switch (status) {
    case IssueStatus.COMPLETE:
      return TaskGraphMarkerType.Complete
    case IssueStatus.CLOSED:
    case IssueStatus.ARCHIVED:
      return TaskGraphMarkerType.Closed
    default:
      return isActionable ? TaskGraphMarkerType.Actionable : TaskGraphMarkerType.Open
  }
}

/**
 * Post-pass: splice one `TaskGraphPhaseRenderLine` per phase immediately after
 * its parent issue line, and append synthetic edges connecting them.
 * Mutates both arrays in place.
 *
 * Called from both `computeLayout` and `computeLayoutFromIssues` so behaviour
 * is identical for the diff view and the live graph.
 */
export function synthesisePhaseRows(
  lines: TaskGraphRenderLine[],
  edges: TaskGraphEdge[],
  openSpecStates: Record<string, IssueOpenSpecState> | null | undefined
): void {
  if (!openSpecStates) return

  const originalLength = lines.length
  let insertionOffset = 0

  for (let i = 0; i < originalLength; i++) {
    const line = lines[i + insertionOffset]
    if (line.type !== 'issue') continue

    const phases = openSpecStates[line.issueId]?.phases
    if (!phases || phases.length === 0) continue

    const issueLane = line.lane
    const phaseLane = issueLane + 1

    const phaseLines: TaskGraphPhaseRenderLine[] = phases.map((phase, phaseIdx) => ({
      type: 'phase',
      phaseId: `${line.issueId}::phase::${phase.name ?? phaseIdx}`,
      parentIssueId: line.issueId,
      lane: phaseLane,
      phaseName: phase.name ?? `Phase ${phaseIdx + 1}`,
      done: phase.done ?? 0,
      total: phase.total ?? 0,
      tasks: phase.tasks ?? [],
    }))

    lines.splice(i + insertionOffset + 1, 0, ...phaseLines)

    // Issue → first phase: L-shaped corner
    edges.push({
      from: line.issueId,
      to: phaseLines[0].phaseId,
      kind: 'SeriesCornerToParent',
      startRow: 0,
      startLane: issueLane,
      endRow: 0,
      endLane: phaseLane,
      pivotLane: issueLane,
      sourceAttach: 'Bottom',
      targetAttach: 'Top',
    })

    // Consecutive phases: straight vertical drop
    for (let p = 1; p < phaseLines.length; p++) {
      edges.push({
        from: phaseLines[p - 1].phaseId,
        to: phaseLines[p].phaseId,
        kind: 'SeriesSibling',
        startRow: 0,
        startLane: phaseLane,
        endRow: 0,
        endLane: phaseLane,
        pivotLane: null,
        sourceAttach: 'Bottom',
        targetAttach: 'Top',
      })
    }

    insertionOffset += phaseLines.length
  }
}

/**
 * Computes render lines + edges for a task graph. The server supplies positions
 * and edges; this function maps each node to one render line and threads edges
 * through unchanged. PR rows / separator / load-more synthesis is Homespun-only.
 *
 * @param taskGraph - response from the server (Fleece v3 layout).
 * @param viewMode - 'tree' hides PR / separator / load-more entries.
 * @param openSpecStates - phase data per issue id; defaults to `taskGraph?.openSpecStates`.
 */
export function computeLayout(
  taskGraph: TaskGraphResponse | null | undefined,
  _maxDepth: number = Infinity,
  viewMode: ViewMode = ViewMode.Tree,
  openSpecStates?: Record<string, IssueOpenSpecState> | null
): TaskGraphLayoutResult {
  if (!taskGraph) {
    return { lines: [], edges: [] }
  }

  const nodes = taskGraph.nodes ?? []
  const mergedPrs = taskGraph.mergedPrs ?? []
  const agentStatuses = taskGraph.agentStatuses ?? {}
  const linkedPrs = taskGraph.linkedPrs ?? {}

  if (nodes.length === 0 && mergedPrs.length === 0) {
    return { lines: [], edges: [] }
  }

  const lines: TaskGraphRenderLine[] = []
  const isTreeView = viewMode === ViewMode.Tree

  if (!isTreeView) {
    if (taskGraph.hasMorePastPrs) {
      lines.push({ type: 'loadMore' })
    }
    const hasIssues = nodes.length > 0
    for (let prIdx = 0; prIdx < mergedPrs.length; prIdx++) {
      const pr = mergedPrs[prIdx]
      const isFirstPr = prIdx === 0
      const isLastPr = prIdx === mergedPrs.length - 1
      lines.push({
        type: 'pr',
        prNumber: pr.number ?? 0,
        title: pr.title ?? '',
        url: pr.url ?? null,
        isMerged: pr.isMerged ?? false,
        hasDescription: pr.hasDescription ?? false,
        agentStatus: pr.agentStatus ?? null,
        drawTopLine: !isFirstPr,
        drawBottomLine: !isLastPr || hasIssues,
      })
    }
    if (mergedPrs.length > 0 && nodes.length > 0) {
      lines.push({ type: 'separator' })
    }
  }

  const sortedNodes = [...nodes].sort((a, b) => (a.row ?? 0) - (b.row ?? 0))

  for (const node of sortedNodes) {
    if (!node.issue?.id) continue

    const issueId = node.issue.id
    const lane = node.lane ?? 0
    const marker = getMarker(node.issue.status, node.isActionable)

    const linkedPr =
      linkedPrs[issueId] ??
      linkedPrs[issueId.toLowerCase()] ??
      linkedPrs[issueId.toUpperCase()] ??
      null
    const agentStatus =
      agentStatuses[issueId] ??
      agentStatuses[issueId.toLowerCase()] ??
      agentStatuses[issueId.toUpperCase()] ??
      null

    const parentRefs = node.issue.parentIssues ?? []
    const primaryParentId = parentRefs[0]?.parentIssue ?? null

    lines.push({
      type: 'issue',
      issueId,
      title: node.issue.title ?? '',
      description: node.issue.description ?? null,
      branchName: generateBranchName(node.issue),
      lane,
      marker,
      issueType: node.issue.type ?? IssueType.TASK,
      status: node.issue.status ?? IssueStatus.DRAFT,
      hasDescription: !!(node.issue.description && node.issue.description.trim()),
      linkedPr,
      agentStatus,
      assignedTo: node.issue.assignedTo ?? null,
      executionMode: node.issue.executionMode ?? ExecutionMode.SERIES,
      parentIssues: parentRefs.length > 0 ? parentRefs : null,
      parentIssueId: primaryParentId,
      appearanceIndex: node.appearanceIndex ?? 1,
      totalAppearances: node.totalAppearances ?? 1,
    })
  }

  const edges: TaskGraphEdge[] = (taskGraph.edges ?? []).map((e) => ({
    from: e.from ?? '',
    to: e.to ?? '',
    kind: e.kind ?? 'SeriesSibling',
    startRow: e.startRow,
    startLane: e.startLane,
    endRow: e.endRow,
    endLane: e.endLane,
    pivotLane: e.pivotLane,
    sourceAttach: e.sourceAttach ?? 'Top',
    targetAttach: e.targetAttach ?? 'Top',
  }))

  const resolvedOpenSpecStates = openSpecStates ?? taskGraph.openSpecStates ?? null
  synthesisePhaseRows(lines, edges, resolvedOpenSpecStates)

  return { lines, edges }
}

// =====================================================================
// Client-side layout: new path consuming Issue[] + decoration maps.
// =====================================================================

export interface ComputeLayoutInput {
  /** Visible-set issues from `useIssues`. */
  issues: readonly IssueResponse[]
  /** Decoration: per-issue linked-PR map (`useLinkedPrs`). */
  linkedPrs?: Record<string, LinkedPr> | null
  /** Decoration: per-issue agent-status map (`useAgentStatuses`). */
  agentStatuses?: Record<string, AgentStatusData> | null
  /** Decoration: merged-PR list for next-mode header (`useMergedPrs`). */
  mergedPrs?: readonly PullRequestWithTime[] | null
  /** Whether more past PRs are available for "load more" rendering. */
  hasMorePastPrs?: boolean
  /** View mode: tree (issues only) vs next (PR rows + separator + issues). */
  viewMode?: ViewMode
  /** Active assignee filter (passed to the layout engine). */
  assigneeFilter?: string | null
  /** Sort config (passed to the layout engine). */
  sortConfig?: GraphSortConfig | null
  /** Subset of issue ids that should appear (next mode only). */
  matchedIds?: ReadonlySet<string> | null
  /** OpenSpec phase data per issue id, from `useOpenSpecStates`. Used to synthesise phase rows. */
  openSpecStates?: Record<string, IssueOpenSpecState> | null
}

export type ClientLayoutResult =
  | (TaskGraphLayoutResult & { ok: true })
  | { ok: false; cycle: readonly string[]; lines: TaskGraphRenderLine[]; edges: TaskGraphEdge[] }

const EDGE_ATTACH_MAP: Record<string, TaskGraphEdge['sourceAttach']> = {
  top: 'Top',
  bottom: 'Bottom',
  left: 'Left',
  right: 'Right',
}

const EDGE_KIND_MAP: Record<string, TaskGraphEdge['kind']> = {
  seriesSibling: 'SeriesSibling',
  seriesCornerToParent: 'SeriesCornerToParent',
  parallelChildToSpine: 'ParallelChildToSpine',
}

function lookupDecoration<T>(map: Record<string, T> | null | undefined, issueId: string): T | null {
  if (!map) return null
  return map[issueId] ?? map[issueId.toLowerCase()] ?? map[issueId.toUpperCase()] ?? null
}

function toLayoutIssue(issue: IssueResponse): LayoutIssue {
  return {
    id: issue.id ?? '',
    title: issue.title ?? null,
    description: issue.description ?? null,
    status: (issue.status ?? IssueStatus.OPEN) as LayoutIssue['status'],
    executionMode: issue.executionMode === ExecutionMode.PARALLEL ? 'parallel' : 'series',
    parentIssues:
      issue.parentIssues?.map((p) => ({
        parentIssue: p.parentIssue ?? '',
        sortOrder: p.sortOrder ?? null,
        // Server-side `IssueDtoMapper` already filters inactive refs out of
        // the response; surface every ref as active here.
        active: true,
      })) ?? null,
    priority: issue.priority ?? null,
    assignedTo: issue.assignedTo ?? null,
    createdAt: issue.createdAt ?? null,
  }
}

function actionableIds(issues: readonly IssueResponse[]): Set<string> {
  // Seeds for Next view = every non-terminal leaf (no incomplete children).
  // Walking ancestors from this seed reaches every non-terminal ancestor, so
  // Next renders the same set of issues as Tree but framed as
  // "leaves-up to roots" rather than "roots-down to leaves".
  const isDoneStatus = (s: IssueStatusEnum | undefined) =>
    s === IssueStatus.COMPLETE || s === IssueStatus.ARCHIVED || s === IssueStatus.CLOSED
  const isTerminalStatus = (s: IssueStatusEnum | undefined) => isDoneStatus(s) || s === undefined

  const childrenOf = new Map<string, IssueResponse[]>()
  for (const issue of issues) {
    for (const p of issue.parentIssues ?? []) {
      if (!p.parentIssue) continue
      const k = p.parentIssue.toLowerCase()
      let bucket = childrenOf.get(k)
      if (!bucket) {
        bucket = []
        childrenOf.set(k, bucket)
      }
      bucket.push(issue)
    }
  }

  const hasIncompleteChildren = (issue: IssueResponse): boolean => {
    if (!issue.id) return false
    const kids = childrenOf.get(issue.id.toLowerCase())
    if (!kids || kids.length === 0) return false
    return kids.some((k) => !isDoneStatus(k.status))
  }

  const result = new Set<string>()
  for (const issue of issues) {
    if (!issue.id) continue
    if (issue.type === IssueType.IDEA) continue
    if (isTerminalStatus(issue.status)) continue
    if (hasIncompleteChildren(issue)) continue
    result.add(issue.id)
  }
  return result
}

function getMarkerForIssue(issue: IssueResponse, isActionable: boolean): TaskGraphMarkerType {
  return getMarker(issue.status, isActionable)
}

function buildIssueRenderLine(
  positioned: PositionedNode<LayoutIssue>,
  issue: IssueResponse,
  decorations: {
    linkedPrs?: Record<string, LinkedPr> | null
    agentStatuses?: Record<string, AgentStatusData> | null
  },
  isActionable: boolean
): TaskGraphIssueRenderLine {
  const parentRefs = issue.parentIssues ?? []
  return {
    type: 'issue',
    issueId: issue.id ?? positioned.node.id,
    title: issue.title ?? '',
    description: issue.description ?? null,
    branchName: generateBranchName(issue),
    lane: positioned.lane,
    marker: getMarkerForIssue(issue, isActionable),
    issueType: issue.type ?? IssueType.TASK,
    status: issue.status ?? IssueStatus.DRAFT,
    hasDescription: !!(issue.description && issue.description.trim()),
    linkedPr: lookupDecoration(decorations.linkedPrs, issue.id ?? ''),
    agentStatus: lookupDecoration(decorations.agentStatuses, issue.id ?? ''),
    assignedTo: issue.assignedTo ?? null,
    executionMode: issue.executionMode ?? ExecutionMode.SERIES,
    parentIssues: parentRefs.length > 0 ? parentRefs : null,
    parentIssueId: parentRefs[0]?.parentIssue ?? null,
    appearanceIndex: positioned.appearanceIndex,
    totalAppearances: positioned.totalAppearances,
  }
}

function emitMergedPrRows(
  mergedPrs: readonly PullRequestWithTime[],
  hasMorePastPrs: boolean,
  hasIssues: boolean,
  agentStatuses: Record<string, AgentStatusData> | null | undefined
): TaskGraphRenderLine[] {
  const lines: TaskGraphRenderLine[] = []
  if (hasMorePastPrs) lines.push({ type: 'loadMore' })
  for (let prIdx = 0; prIdx < mergedPrs.length; prIdx++) {
    const pr = mergedPrs[prIdx]
    const info = pr.pullRequest
    const isFirstPr = prIdx === 0
    const isLastPr = prIdx === mergedPrs.length - 1
    lines.push({
      type: 'pr',
      prNumber: info?.number ?? 0,
      title: info?.title ?? '',
      url: info?.htmlUrl ?? null,
      isMerged: !!info?.mergedAt,
      hasDescription: !!(info?.body && info.body.trim()),
      // Merged PRs aren't tied to a per-issue session, so no agent status —
      // but if the PR's author session is still resolving, surface that.
      agentStatus: lookupDecoration(agentStatuses, info?.number?.toString() ?? ''),
      drawTopLine: !isFirstPr,
      drawBottomLine: !isLastPr || hasIssues,
    })
  }
  if (mergedPrs.length > 0 && hasIssues) {
    lines.push({ type: 'separator' })
  }
  return lines
}

/**
 * Client-side layout driver. Runs the TS port against the supplied issues,
 * then assembles the render-line + edge stream. Returns `{ ok: false }` when
 * the issue graph contains a cycle so the caller can render a degraded
 * flat-list view + error banner.
 */
export function computeLayoutFromIssues(input: ComputeLayoutInput): ClientLayoutResult {
  const {
    issues,
    linkedPrs = null,
    agentStatuses = null,
    mergedPrs = [],
    hasMorePastPrs = false,
    viewMode = ViewMode.Tree,
    assigneeFilter = null,
    sortConfig = null,
    matchedIds = null,
    openSpecStates = null,
  } = input

  const isTreeView = viewMode === ViewMode.Tree

  const layoutIssues = issues.map(toLayoutIssue)
  const actionable = actionableIds(issues)
  let layout: GraphLayoutResult<LayoutIssue>
  try {
    if (isTreeView) {
      // Top-down: root at lane 0, children descending.
      layout = layoutForTree(layoutIssues, {
        assignedTo: assigneeFilter,
        sort: sortConfig,
        mode: 'normalTree',
      })
    } else {
      // Next mode: explicit matchedIds wins; otherwise seed from the
      // non-terminal-leaf actionable set. Empty seed degrades to the full
      // tree so the view isn't blank when nothing is actionable.
      const seed = matchedIds && matchedIds.size > 0 ? matchedIds : actionable
      if (seed.size === 0) {
        layout = layoutForTree(layoutIssues, {
          assignedTo: assigneeFilter,
          sort: sortConfig,
          mode: 'issueGraph',
        })
      } else {
        layout = layoutForNext(layoutIssues, seed, {
          assignedTo: assigneeFilter,
          sort: sortConfig,
          mode: 'issueGraph',
        })
      }
    }
  } catch (err) {
    if (err instanceof InvalidGraphError) {
      layout = { ok: false, cycle: err.cycle }
    } else {
      throw err
    }
  }

  // Index issues by id for O(1) decoration / metadata lookup.
  const issueById = new Map<string, IssueResponse>()
  for (const i of issues) if (i.id) issueById.set(i.id.toLowerCase(), i)

  // Cycle: degraded fallback — emit a flat list of every issue, no edges.
  if (!layout.ok) {
    const flatLines: TaskGraphRenderLine[] = []
    for (let idx = 0; idx < issues.length; idx++) {
      const issue = issues[idx]
      flatLines.push(
        buildIssueRenderLine(
          {
            node: toLayoutIssue(issue),
            row: idx,
            lane: 0,
            appearanceIndex: 1,
            totalAppearances: 1,
          },
          issue,
          { linkedPrs, agentStatuses },
          actionable.has(issue.id ?? '')
        )
      )
    }
    const flatEdges: TaskGraphEdge[] = []
    synthesisePhaseRows(flatLines, flatEdges, openSpecStates)
    return { ok: false, cycle: layout.cycle, lines: flatLines, edges: flatEdges }
  }

  const lines: TaskGraphRenderLine[] = []
  const hasIssues = layout.layout.nodes.length > 0

  if (!isTreeView) {
    lines.push(...emitMergedPrRows(mergedPrs ?? [], hasMorePastPrs, hasIssues, agentStatuses))
  }

  for (const positioned of layout.layout.nodes) {
    const issue = issueById.get(positioned.node.id.toLowerCase())
    if (!issue) continue
    lines.push(
      buildIssueRenderLine(
        positioned,
        issue,
        { linkedPrs, agentStatuses },
        actionable.has(issue.id ?? '')
      )
    )
  }

  const edges: TaskGraphEdge[] = layout.layout.edges.map((e) => ({
    from: e.from.id,
    to: e.to.id,
    kind: EDGE_KIND_MAP[e.kind] ?? e.kind,
    startRow: e.startRow,
    startLane: e.startLane,
    endRow: e.endRow,
    endLane: e.endLane,
    pivotLane: e.pivotLane,
    sourceAttach: EDGE_ATTACH_MAP[e.sourceAttach] ?? 'Top',
    targetAttach: EDGE_ATTACH_MAP[e.targetAttach] ?? 'Top',
  }))

  synthesisePhaseRows(lines, edges, openSpecStates)

  return { ok: true, lines, edges }
}
