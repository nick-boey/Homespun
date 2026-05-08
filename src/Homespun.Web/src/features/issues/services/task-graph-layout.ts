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
 *   maps fetched per-endpoint. Returns the same shape as the legacy path so
 *   consumers don't change. Memoise at the call site (issue set + viewMode +
 *   filter is the layout key; decorations are render-only).
 */

import type {
  TaskGraphResponse,
  LinkedPr,
  AgentStatusData,
  IssueResponse,
  IssueType as IssueTypeEnum,
  IssueStatus as IssueStatusEnum,
  ExecutionMode as ExecutionModeEnum,
} from '@/api'
import { ExecutionMode, IssueStatus, IssueType } from '@/api'
import { generateBranchName } from './branch-name'
import { ViewMode } from '../types'
import {
  InvalidGraphError,
  isIssueNode,
  isPendingIssueNode,
  PENDING_ISSUE_ID,
  layoutForNext,
  layoutForTree,
  type GraphLayoutResult,
  type GraphSortConfig,
  type IssueLayoutNode,
  type LayoutIssue,
  type LayoutNode,
  type ParentIssueRef,
  type PositionedNode,
} from './layout'
import { midpoint } from './sort-order-midpoint'

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

export interface TaskGraphPendingIssueRenderLine {
  type: 'pending-issue'
  pendingTitle: string
  lane: number
  parentIssues?: readonly { parentIssue?: string | null; sortOrder?: string | null }[] | null
}

export type TaskGraphRenderLine = TaskGraphIssueRenderLine | TaskGraphPendingIssueRenderLine

export function isIssueRenderLine(line: TaskGraphRenderLine): line is TaskGraphIssueRenderLine {
  return line.type === 'issue'
}

export function isPendingIssueRenderLine(
  line: TaskGraphRenderLine
): line is TaskGraphPendingIssueRenderLine {
  return line.type === 'pending-issue'
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
 * Computes render lines + edges for a task graph from a server-laid-out
 * `TaskGraphResponse` (legacy / diff path). The server supplies positions and
 * edges; this function maps each node to one issue render line and threads
 * edges through unchanged.
 */
export function computeLayout(
  taskGraph: TaskGraphResponse | null | undefined,
  _maxDepth: number = Infinity,
  _viewMode: ViewMode = ViewMode.Tree
): TaskGraphLayoutResult {
  if (!taskGraph) {
    return { lines: [], edges: [] }
  }

  const nodes = taskGraph.nodes ?? []
  const agentStatuses = taskGraph.agentStatuses ?? {}
  const linkedPrs = taskGraph.linkedPrs ?? {}

  if (nodes.length === 0) {
    return { lines: [], edges: [] }
  }

  const lines: TaskGraphRenderLine[] = []

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

  return { lines, edges }
}

// =====================================================================
// Client-side layout: new path consuming Issue[] + decoration maps.
// =====================================================================

export interface PendingIssueInput {
  mode: 'sibling-below' | 'sibling-above' | 'child-of' | 'parent-of'
  referenceIssueId: string
  title: string
  viewMode: ViewMode
}

export interface ComputeLayoutInput {
  /** Visible-set issues from `useIssues`. */
  issues: readonly IssueResponse[]
  /** Decoration: per-issue linked-PR map (`useLinkedPrs`). */
  linkedPrs?: Record<string, LinkedPr> | null
  /** Decoration: per-issue agent-status map (`useAgentStatuses`). */
  agentStatuses?: Record<string, AgentStatusData> | null
  /** View mode: tree (issues only) vs next (actionable leaves-up). */
  viewMode?: ViewMode
  /** Active assignee filter (passed to the layout engine). */
  assigneeFilter?: string | null
  /** Sort config (passed to the layout engine). */
  sortConfig?: GraphSortConfig | null
  /** Subset of issue ids that should appear (next mode only). */
  matchedIds?: ReadonlySet<string> | null
  /** Optional pending (synthetic) issue to inject into the layout. */
  pendingIssue?: PendingIssueInput | null
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
  positioned: PositionedNode<IssueLayoutNode | LayoutIssue>,
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
    viewMode = ViewMode.Tree,
    assigneeFilter = null,
    sortConfig = null,
    matchedIds = null,
    pendingIssue = null,
  } = input

  const isTreeView = viewMode === ViewMode.Tree

  let layoutIssues = issues.map(toLayoutIssue)
  const actionable = actionableIds(issues)

  // Inject the synthetic pending-issue node into the layout if present.
  if (pendingIssue) {
    const { mode, referenceIssueId, title } = pendingIssue
    const refIssue = layoutIssues.find((i) => i.id.toLowerCase() === referenceIssueId.toLowerCase())
    if (refIssue) {
      // Build shallow copies so we don't mutate cached data.
      const issuesCopy: LayoutIssue[] = layoutIssues.map((i) => ({
        ...i,
        parentIssues: i.parentIssues ? [...i.parentIssues] : null,
      }))

      // Find siblings for sortOrder computation.
      const refParentId = refIssue.parentIssues?.[0]?.parentIssue?.toLowerCase() ?? null
      const siblings = (
        refParentId
          ? layoutIssues.filter((i) =>
              i.parentIssues?.some((p) => p.parentIssue?.toLowerCase() === refParentId)
            )
          : layoutIssues.filter((i) => !i.parentIssues?.length)
      ).sort((a, b) => {
        const aS =
          a.parentIssues?.find((p) => p.parentIssue?.toLowerCase() === (refParentId ?? ''))
            ?.sortOrder ?? ''
        const bS =
          b.parentIssues?.find((p) => p.parentIssue?.toLowerCase() === (refParentId ?? ''))
            ?.sortOrder ?? ''
        return aS < bS ? -1 : aS > bS ? 1 : 0
      })
      const refSiblingIdx = siblings.findIndex(
        (i) => i.id.toLowerCase() === referenceIssueId.toLowerCase()
      )

      let syntheticParentIssues: ParentIssueRef[] | undefined

      switch (mode) {
        case 'sibling-below': {
          const nextSibling = siblings[refSiblingIdx + 1]
          const prevSortOrder = refIssue.parentIssues?.[0]?.sortOrder ?? ''
          const nextSortOrder =
            nextSibling?.parentIssues?.find(
              (p) => p.parentIssue?.toLowerCase() === (refParentId ?? '')
            )?.sortOrder ?? ''
          const sortOrder = midpoint(prevSortOrder, nextSortOrder)
          syntheticParentIssues = refParentId
            ? [{ parentIssue: refParentId, sortOrder, active: true }]
            : undefined
          break
        }
        case 'sibling-above': {
          const prevSibling = siblings[refSiblingIdx - 1]
          const prevSortOrder =
            prevSibling?.parentIssues?.find(
              (p) => p.parentIssue?.toLowerCase() === (refParentId ?? '')
            )?.sortOrder ?? ''
          const nextSortOrder = refIssue.parentIssues?.[0]?.sortOrder ?? ''
          const sortOrder = midpoint(prevSortOrder, nextSortOrder)
          syntheticParentIssues = refParentId
            ? [{ parentIssue: refParentId, sortOrder, active: true }]
            : undefined
          break
        }
        case 'child-of': {
          // Synthetic is a child of the reference issue.
          const refChildren = layoutIssues
            .filter((i) =>
              i.parentIssues?.some(
                (p) => p.parentIssue?.toLowerCase() === referenceIssueId.toLowerCase()
              )
            )
            .sort((a, b) => {
              const aS =
                a.parentIssues?.find(
                  (p) => p.parentIssue?.toLowerCase() === referenceIssueId.toLowerCase()
                )?.sortOrder ?? ''
              const bS =
                b.parentIssues?.find(
                  (p) => p.parentIssue?.toLowerCase() === referenceIssueId.toLowerCase()
                )?.sortOrder ?? ''
              return aS < bS ? -1 : aS > bS ? 1 : 0
            })
          const lastChild = refChildren[refChildren.length - 1]
          const lastSortOrder =
            lastChild?.parentIssues?.find(
              (p) => p.parentIssue?.toLowerCase() === referenceIssueId.toLowerCase()
            )?.sortOrder ?? ''
          const sortOrder = midpoint(lastSortOrder, '')
          syntheticParentIssues = [{ parentIssue: referenceIssueId, sortOrder, active: true }]
          break
        }
        case 'parent-of': {
          // Synthetic takes ref's old parent slot; ref becomes a child of synthetic.
          const oldParentRef = refIssue.parentIssues?.[0]
          syntheticParentIssues = oldParentRef
            ? [
                {
                  parentIssue: oldParentRef.parentIssue,
                  sortOrder: oldParentRef.sortOrder ?? null,
                  active: true,
                },
              ]
            : undefined
          // Patch ref's parentIssues to point at the synthetic node.
          const fabricatedSortOrder = midpoint('', refIssue.parentIssues?.[0]?.sortOrder ?? 'a')
          const refIdx = issuesCopy.findIndex(
            (i) => i.id.toLowerCase() === referenceIssueId.toLowerCase()
          )
          if (refIdx >= 0) {
            issuesCopy[refIdx] = {
              ...issuesCopy[refIdx],
              parentIssues: [
                { parentIssue: PENDING_ISSUE_ID, sortOrder: fabricatedSortOrder, active: true },
              ],
            }
          }
          break
        }
      }

      const synthetic: LayoutIssue = {
        id: PENDING_ISSUE_ID,
        title: title,
        status: 'open',
        executionMode: 'series',
        parentIssues: syntheticParentIssues ?? null,
      }

      issuesCopy.push(synthetic)
      layoutIssues = issuesCopy
    }
  }
  let layout: GraphLayoutResult<LayoutNode>
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
    return { ok: false, cycle: layout.cycle, lines: flatLines, edges: [] }
  }

  const lines: TaskGraphRenderLine[] = []

  for (const positioned of layout.layout.nodes) {
    const node = positioned.node
    if (isPendingIssueNode(node)) {
      lines.push({
        type: 'pending-issue',
        pendingTitle: node.pendingTitle,
        lane: positioned.lane,
        parentIssues: node.parentIssues ?? null,
      })
    } else if (isIssueNode(node)) {
      const issue = issueById.get(node.id.toLowerCase())
      if (!issue) continue
      lines.push(
        buildIssueRenderLine(
          positioned as PositionedNode<IssueLayoutNode>,
          issue,
          { linkedPrs, agentStatuses },
          actionable.has(issue.id ?? '')
        )
      )
    }
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

  return { ok: true, lines, edges }
}
