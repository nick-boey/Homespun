/**
 * Converts a TaskGraphResponse into render lines + edges for TaskGraphView.
 *
 * Server (Fleece v3) supplies the authoritative graph layout: each node carries
 * its row/lane/appearance position, and a separate `edges` collection carries
 * every connector with its kind + attach sides. This module is a thin pass-through:
 * one render line per `PositionedNode`, edges threaded through unchanged, plus
 * Homespun-only synthesis for PR rows / separators / "load more" entries.
 */

import type {
  TaskGraphResponse,
  TaskGraphLinkedPr,
  AgentStatusData,
  IssueType as IssueTypeEnum,
  IssueStatus as IssueStatusEnum,
  ExecutionMode as ExecutionModeEnum,
} from '@/api'
import { ExecutionMode, IssueStatus, IssueType } from '@/api'
import { generateBranchName } from './branch-name'
import { ViewMode } from '../types'

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
  linkedPr: TaskGraphLinkedPr | null
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

export type TaskGraphRenderLine =
  | TaskGraphIssueRenderLine
  | TaskGraphSeparatorRenderLine
  | TaskGraphPrRenderLine
  | TaskGraphLoadMoreRenderLine

export function isIssueRenderLine(line: TaskGraphRenderLine): line is TaskGraphIssueRenderLine {
  return line.type === 'issue'
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
 * Computes render lines + edges for a task graph. The server supplies positions
 * and edges; this function maps each node to one render line and threads edges
 * through unchanged. PR rows / separator / load-more synthesis is Homespun-only.
 *
 * @param taskGraph - response from the server (Fleece v3 layout).
 * @param viewMode - 'tree' hides PR / separator / load-more entries.
 */
export function computeLayout(
  taskGraph: TaskGraphResponse | null | undefined,
  _maxDepth: number = Infinity,
  viewMode: ViewMode = ViewMode.Tree
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
    from: e.from,
    to: e.to,
    kind: e.kind,
    startRow: e.startRow,
    startLane: e.startLane,
    endRow: e.endRow,
    endLane: e.endLane,
    pivotLane: e.pivotLane,
    sourceAttach: e.sourceAttach,
    targetAttach: e.targetAttach,
  }))

  return { lines, edges }
}
