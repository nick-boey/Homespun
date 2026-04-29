/**
 * TypeScript port of TaskGraphLayoutService.cs
 *
 * Converts a TaskGraphResponse into a list of render lines for the TaskGraphView component.
 * Produces structured render instructions for displaying issues in a lane-based hierarchical graph.
 */

import type {
  TaskGraphResponse,
  TaskGraphNodeResponse,
  TaskGraphLinkedPr,
  AgentStatusData,
  IssueType as IssueTypeEnum,
  IssueStatus as IssueStatusEnum,
  ExecutionMode as ExecutionModeEnum,
} from '@/api'
import { ExecutionMode, IssueStatus, IssueType } from '@/api'

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
import { getPriorityColor } from './priority-colors'
import { generateBranchName } from './branch-name'
import { ViewMode } from '../types'

// Marker types (matching C# enum)
export const TaskGraphMarkerType = {
  Actionable: 'actionable',
  Open: 'open',
  Complete: 'complete',
  Closed: 'closed',
} as const

export type TaskGraphMarkerType = (typeof TaskGraphMarkerType)[keyof typeof TaskGraphMarkerType]

// Render line types
export interface TaskGraphIssueRenderLine {
  type: 'issue'
  issueId: string
  title: string
  description: string | null
  branchName: string | null
  lane: number
  marker: TaskGraphMarkerType
  parentLane: number | null
  isFirstChild: boolean
  isSeriesChild: boolean
  drawTopLine: boolean
  drawBottomLine: boolean
  seriesConnectorFromLane: number | null
  issueType: IssueTypeEnum
  status: IssueStatusEnum
  hasDescription: boolean
  linkedPr: TaskGraphLinkedPr | null
  agentStatus: AgentStatusData | null
  assignedTo: string | null
  drawLane0Connector: boolean
  isLastLane0Connector: boolean
  drawLane0PassThrough: boolean
  lane0Color: string | null
  hasHiddenParent: boolean
  hiddenParentIsSeriesMode: boolean
  executionMode: ExecutionModeEnum
  parentIssues: Array<{ parentIssue?: string | null; sortOrder?: string | null }> | null
  multiParentIndex: number | null
  multiParentTotal: number | null
  isLastChild: boolean
  hasParallelChildren: boolean
  parentIssueId: string | null
  parentLaneReservations: Array<{ lane: number; issueType: IssueTypeEnum }>
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

// Type guards
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

/**
 * Returns a unique key for a render line, accounting for multi-parent duplicates.
 */
export function getRenderKey(line: TaskGraphIssueRenderLine): string {
  return line.multiParentIndex != null ? `${line.issueId}:${line.multiParentIndex}` : line.issueId
}

/**
 * Computes the layout for a task graph, optionally filtering nodes by depth.
 *
 * @param taskGraph - The task graph response to layout
 * @param maxDepth - Maximum depth (lane) to display. Default is 3 (shows lanes 0-3).
 *                   Use Infinity to show all levels.
 * @param viewMode - View mode: 'next' (actionable at root) or 'tree' (traditional hierarchy).
 *                   Default is 'tree'.
 * @returns List of render lines for the task graph view
 */
export function computeLayout(
  taskGraph: TaskGraphResponse | null | undefined,
  maxDepth = 3,
  viewMode: ViewMode = ViewMode.Tree
): TaskGraphLayoutResult {
  if (!taskGraph) {
    return { lines: [], edges: [] }
  }

  const nodes = taskGraph.nodes ?? []
  const mergedPrs = taskGraph.mergedPrs ?? []
  const agentStatuses = taskGraph.agentStatuses ?? {}
  const linkedPrs = taskGraph.linkedPrs ?? {}

  // Return early if no nodes and no PRs
  if (nodes.length === 0 && mergedPrs.length === 0) {
    return { lines: [], edges: [] }
  }

  const result: TaskGraphRenderLine[] = []
  const isTreeView = viewMode === ViewMode.Tree

  // In tree view, skip PRs, separators, and load more entirely
  if (!isTreeView) {
    // Add load more button at the very top if there are more PRs
    if (taskGraph.hasMorePastPrs) {
      result.push({ type: 'loadMore' })
    }

    // Add merged/closed PRs at the top with appropriate top/bottom line flags
    const hasIssues = nodes.length > 0
    for (let prIdx = 0; prIdx < mergedPrs.length; prIdx++) {
      const pr = mergedPrs[prIdx]
      const isFirstPr = prIdx === 0
      const isLastPr = prIdx === mergedPrs.length - 1

      const drawTopLine = !isFirstPr
      const drawBottomLine = !isLastPr || (isLastPr && hasIssues)

      result.push({
        type: 'pr',
        prNumber: pr.number ?? 0,
        title: pr.title ?? '',
        url: pr.url ?? null,
        isMerged: pr.isMerged ?? false,
        hasDescription: pr.hasDescription ?? false,
        agentStatus: pr.agentStatus ?? null,
        drawTopLine,
        drawBottomLine,
      })
    }

    // Add separator if we have PRs and issues
    if (mergedPrs.length > 0 && nodes.length > 0) {
      result.push({ type: 'separator' })
    }
  }

  // When merged PRs exist, offset all lanes by +1 to make room for the
  // vertical connector line in lane 0 that connects PRs to issues
  // In tree view, no lane offset is needed
  const laneOffset = !isTreeView && mergedPrs.length > 0 ? 1 : 0

  // Build lookup for hidden parent detection
  const allNodesByIssueId = new Map<string, TaskGraphNodeResponse>()
  for (const node of nodes) {
    if (node.issue?.id) {
      allNodesByIssueId.set(node.issue.id.toLowerCase(), node)
    }
  }

  const groups = groupNodes(nodes)
  for (const group of groups) {
    if (isTreeView) {
      // Tree view: compute lanes from roots and filter by depth from root
      const treeViewNodes = computeTreeViewLayout(group, maxDepth, allNodesByIssueId)

      // Compute hidden parent info for tree view
      const hiddenParentInfo = computeHiddenParentInfoTreeView(
        treeViewNodes,
        group,
        allNodesByIssueId,
        maxDepth
      )

      renderGroupTreeView(result, treeViewNodes, agentStatuses, linkedPrs, hiddenParentInfo)
    } else {
      // Filter nodes by maxDepth within each group
      const minLane = Math.min(...group.map((n) => n.lane ?? 0))
      const visibleNodes = group.filter((n) => (n.lane ?? 0) - minLane <= maxDepth)

      // Compute hidden parent info: which nodes have parents filtered out
      const hiddenParentInfo = computeHiddenParentInfo(
        visibleNodes,
        group,
        allNodesByIssueId,
        minLane,
        maxDepth
      )

      renderGroup(
        result,
        visibleNodes,
        group,
        agentStatuses,
        linkedPrs,
        laneOffset,
        hiddenParentInfo
      )
    }
  }

  // Post-process: connect merged PR vertical line at lane 0 to leftmost issue nodes
  if (laneOffset > 0) {
    let firstIdx = -1
    let lastIdx = -1

    for (let i = 0; i < result.length; i++) {
      const line = result[i]
      if (isIssueRenderLine(line) && line.lane === laneOffset) {
        if (firstIdx === -1) firstIdx = i
        lastIdx = i
      }
    }

    if (firstIdx >= 0) {
      let lastConnectorIdx = -1
      for (let i = firstIdx; i <= lastIdx; i++) {
        const line = result[i]
        if (isIssueRenderLine(line) && line.lane === laneOffset) {
          const isBlockedSeriesSibling = line.isSeriesChild && line.drawTopLine
          if (!isBlockedSeriesSibling) {
            lastConnectorIdx = i
          }
        }
      }

      for (let i = firstIdx; i <= lastIdx; i++) {
        const line = result[i]
        if (isIssueRenderLine(line)) {
          if (line.lane === laneOffset) {
            const isBlockedSeriesSibling = line.isSeriesChild && line.drawTopLine
            if (isBlockedSeriesSibling) {
              result[i] = { ...line, drawLane0PassThrough: true }
            } else {
              result[i] = {
                ...line,
                drawLane0Connector: true,
                isLastLane0Connector: i === lastConnectorIdx,
              }
            }
          } else {
            result[i] = { ...line, drawLane0PassThrough: true }
          }
        }
      }
    }
  }

  // Post-process: assign multiParentIndex/multiParentTotal for issues appearing more than once
  const mpCounts = new Map<string, number>()
  for (const line of result) {
    if (isIssueRenderLine(line)) {
      const id = line.issueId.toLowerCase()
      mpCounts.set(id, (mpCounts.get(id) ?? 0) + 1)
    }
  }
  const mpIdx = new Map<string, number>()
  for (let i = 0; i < result.length; i++) {
    const line = result[i]
    if (isIssueRenderLine(line)) {
      const id = line.issueId.toLowerCase()
      const total = mpCounts.get(id) ?? 1
      if (total > 1) {
        const idx = mpIdx.get(id) ?? 0
        mpIdx.set(id, idx + 1)
        result[i] = { ...line, multiParentIndex: idx, multiParentTotal: total }
      }
    }
  }

  // Post-process: compute parentLaneReservations (tree view only)
  if (isTreeView) {
    // For each parallel parent, find its children's index range and add reservation lines
    const parallelParents = new Map<string, { lane: number; issueType: IssueTypeEnum }>()
    for (const line of result) {
      if (isIssueRenderLine(line) && line.hasParallelChildren) {
        parallelParents.set(line.issueId.toLowerCase(), {
          lane: line.lane,
          issueType: line.issueType,
        })
      }
    }
    if (parallelParents.size > 0) {
      // Find first and last child indices for each parallel parent
      const childRanges = new Map<string, { firstIdx: number; lastIdx: number }>()
      for (let i = 0; i < result.length; i++) {
        const line = result[i]
        if (!isIssueRenderLine(line) || !line.parentIssueId) continue
        const pid = line.parentIssueId.toLowerCase()
        if (!parallelParents.has(pid)) continue
        const range = childRanges.get(pid)
        if (!range) {
          childRanges.set(pid, { firstIdx: i, lastIdx: i })
        } else {
          range.lastIdx = i
        }
      }
      // Add reservations to all rows between first and last child (exclusive of last —
      // the last child's connector already draws the correct shortened vertical line)
      for (const [pid, range] of childRanges) {
        const parent = parallelParents.get(pid)!
        for (let i = range.firstIdx; i < range.lastIdx; i++) {
          const line = result[i]
          if (isIssueRenderLine(line)) {
            result[i] = {
              ...line,
              parentLaneReservations: [
                ...line.parentLaneReservations,
                { lane: parent.lane, issueType: parent.issueType },
              ],
            }
          }
        }
      }
    }

    // Post-process: compute parentLaneReservations for series parent children
    // For each series parent, find its children's index range and add reservation lines
    const seriesParents = new Map<string, { lane: number; issueType: IssueTypeEnum }>()
    for (const line of result) {
      if (isIssueRenderLine(line) && line.seriesConnectorFromLane != null) {
        seriesParents.set(line.issueId.toLowerCase(), {
          lane: line.seriesConnectorFromLane,
          issueType: line.issueType,
        })
      }
    }
    if (seriesParents.size > 0) {
      const seriesChildRanges = new Map<string, { firstIdx: number; lastIdx: number }>()
      for (let i = 0; i < result.length; i++) {
        const line = result[i]
        if (!isIssueRenderLine(line) || !line.parentIssueId) continue
        const pid = line.parentIssueId.toLowerCase()
        if (!seriesParents.has(pid)) continue
        const range = seriesChildRanges.get(pid)
        if (!range) {
          seriesChildRanges.set(pid, { firstIdx: i, lastIdx: i })
        } else {
          range.lastIdx = i
        }
      }
      for (const [pid, range] of seriesChildRanges) {
        const parent = seriesParents.get(pid)!
        for (let i = range.firstIdx; i < range.lastIdx; i++) {
          const line = result[i]
          if (isIssueRenderLine(line)) {
            result[i] = {
              ...line,
              parentLaneReservations: [
                ...line.parentLaneReservations,
                { lane: parent.lane, issueType: parent.issueType },
              ],
            }
          }
        }
      }
    }
  } // end isTreeView reservation post-processing

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

  return { lines: result, edges }
}

/**
 * Computes which visible nodes have parents that were filtered out due to maxDepth.
 */
function computeHiddenParentInfo(
  visibleNodes: TaskGraphNodeResponse[],
  allGroupNodes: TaskGraphNodeResponse[],
  allNodesByIssueId: Map<string, TaskGraphNodeResponse>,
  _minLane: number,
  _maxDepth: number
): Map<string, { hasHiddenParent: boolean; hiddenParentIsSeriesMode: boolean }> {
  const result = new Map<string, { hasHiddenParent: boolean; hiddenParentIsSeriesMode: boolean }>()
  const visibleIds = new Set(visibleNodes.map((n) => n.issue?.id?.toLowerCase()).filter(Boolean))

  for (const node of visibleNodes) {
    if (!node.issue?.id) continue

    let hasHiddenParent = false
    let hiddenParentIsSeriesMode = false

    for (const parentRef of node.issue.parentIssues ?? []) {
      if (!parentRef.parentIssue) continue

      const parentNode = allNodesByIssueId.get(parentRef.parentIssue.toLowerCase())

      if (parentNode) {
        const parentInGroup = allGroupNodes.some(
          (n) => n.issue?.id?.toLowerCase() === parentRef.parentIssue?.toLowerCase()
        )

        if (parentInGroup && !visibleIds.has(parentRef.parentIssue.toLowerCase())) {
          hasHiddenParent = true
          hiddenParentIsSeriesMode = parentNode.issue?.executionMode === ExecutionMode.SERIES
        }
      }
    }

    result.set(node.issue.id.toLowerCase(), { hasHiddenParent, hiddenParentIsSeriesMode })
  }

  return result
}

/**
 * Groups nodes into connected components using BFS.
 */
function groupNodes(nodes: TaskGraphNodeResponse[]): TaskGraphNodeResponse[][] {
  const nodeById = new Map<string, TaskGraphNodeResponse>()
  for (const node of nodes) {
    if (node.issue?.id) {
      nodeById.set(node.issue.id.toLowerCase(), node)
    }
  }

  const adjacency = new Map<string, Set<string>>()
  for (const node of nodes) {
    if (!node.issue?.id) continue

    const nodeId = node.issue.id.toLowerCase()
    if (!adjacency.has(nodeId)) {
      adjacency.set(nodeId, new Set())
    }

    for (const parentRef of node.issue.parentIssues ?? []) {
      if (!parentRef.parentIssue) continue
      const parentId = parentRef.parentIssue.toLowerCase()

      if (!nodeById.has(parentId)) continue

      adjacency.get(nodeId)!.add(parentId)

      if (!adjacency.has(parentId)) {
        adjacency.set(parentId, new Set())
      }
      adjacency.get(parentId)!.add(nodeId)
    }
  }

  const visited = new Set<string>()
  const groups: TaskGraphNodeResponse[][] = []

  for (const node of nodes) {
    if (!node.issue?.id) continue
    const nodeId = node.issue.id.toLowerCase()

    if (visited.has(nodeId)) continue

    const component = new Set<string>()
    const queue: string[] = [nodeId]
    visited.add(nodeId)

    while (queue.length > 0) {
      const current = queue.shift()!
      component.add(current)

      const neighbors = adjacency.get(current)
      if (neighbors) {
        for (const neighbor of neighbors) {
          if (!visited.has(neighbor)) {
            visited.add(neighbor)
            queue.push(neighbor)
          }
        }
      }
    }

    const group = nodes
      .filter((n) => n.issue?.id && component.has(n.issue.id.toLowerCase()))
      .sort((a, b) => (a.row ?? 0) - (b.row ?? 0))

    groups.push(group)
  }

  // Sort groups by first row
  groups.sort((a, b) => (a[0]?.row ?? 0) - (b[0]?.row ?? 0))

  return groups
}

function renderGroup(
  result: TaskGraphRenderLine[],
  group: TaskGraphNodeResponse[],
  allGroupNodes: TaskGraphNodeResponse[],
  agentStatuses: Record<string, AgentStatusData>,
  linkedPrs: Record<string, TaskGraphLinkedPr>,
  laneOffset = 0,
  hiddenParentInfo?: Map<string, { hasHiddenParent: boolean; hiddenParentIsSeriesMode: boolean }>
): void {
  if (group.length === 0) return

  const minLane = Math.min(...group.map((n) => n.lane ?? 0))

  // Find the "next" (actionable) issue - the one at lane 0 (before offset)
  // Its priority determines the lane0Color for the entire group
  const nextIssue = group.find((n) => n.lane === minLane)
  const groupPriority = nextIssue?.issue?.priority
  const groupLane0Color = laneOffset > 0 ? getPriorityColor(groupPriority) : null

  // Pre-compute parent assignments and children-per-parent counts
  const parentByNode = new Map<string, TaskGraphNodeResponse>()
  const childrenCountByParent = new Map<string, number>()

  for (const node of group) {
    if (!node.issue?.id) continue

    let parentNode: TaskGraphNodeResponse | undefined

    for (const parentRef of node.issue.parentIssues ?? []) {
      if (!parentRef.parentIssue) continue

      const candidate = group.find(
        (n) => n.issue?.id?.toLowerCase() === parentRef.parentIssue?.toLowerCase()
      )

      if (candidate && (parentNode == null || (candidate.lane ?? 0) > (parentNode.lane ?? 0))) {
        parentNode = candidate
      }
    }

    if (
      parentNode &&
      parentNode.issue?.id &&
      (parentNode.lane ?? 0) - minLane > (node.lane ?? 0) - minLane
    ) {
      parentByNode.set(node.issue.id.toLowerCase(), parentNode)
      const parentId = parentNode.issue.id.toLowerCase()
      childrenCountByParent.set(parentId, (childrenCountByParent.get(parentId) ?? 0) + 1)
    }
  }

  // Build lookup for hidden parents (parents in allGroupNodes but not in visible group)
  const hiddenParentByNode = new Map<string, TaskGraphNodeResponse>()
  const visibleNodeIds = new Set(group.map((n) => n.issue?.id?.toLowerCase()).filter(Boolean))

  for (const node of group) {
    if (!node.issue?.id) continue

    for (const parentRef of node.issue.parentIssues ?? []) {
      if (!parentRef.parentIssue) continue

      const hiddenParent = allGroupNodes.find(
        (n) =>
          n.issue?.id?.toLowerCase() === parentRef.parentIssue?.toLowerCase() &&
          !visibleNodeIds.has(n.issue?.id?.toLowerCase())
      )

      if (hiddenParent) {
        hiddenParentByNode.set(node.issue.id.toLowerCase(), hiddenParent)
        break
      }
    }
  }

  // Pre-compute series child lane by parent
  const seriesChildLaneByParent = new Map<string, number>()

  for (const node of group) {
    if (!node.issue?.id) continue

    const parentNode = parentByNode.get(node.issue.id.toLowerCase())
    if (parentNode?.issue?.id && parentNode.issue?.executionMode === ExecutionMode.SERIES) {
      seriesChildLaneByParent.set(parentNode.issue.id.toLowerCase(), (node.lane ?? 0) - minLane)
    }
  }

  const childrenRendered = new Map<string, number>()

  for (let i = 0; i < group.length; i++) {
    const node = group[i]
    if (!node.issue?.id) continue

    const nodeId = node.issue.id.toLowerCase()
    const baseLane = (node.lane ?? 0) - minLane
    const lane = baseLane + laneOffset
    const marker = getMarker(node)

    const parentNode = parentByNode.get(nodeId)
    const baseParentLane = parentNode ? (parentNode.lane ?? 0) - minLane : null
    const parentLane = baseParentLane != null ? baseParentLane + laneOffset : null
    let isFirstChild = false

    if (parentNode?.issue?.id && baseParentLane != null && baseParentLane > baseLane) {
      const parentId = parentNode.issue.id.toLowerCase()
      const count = (childrenRendered.get(parentId) ?? 0) + 1
      childrenRendered.set(parentId, count)
      isFirstChild = count === 1
    }

    const seriesHiddenParent = hiddenParentByNode.get(nodeId)
    const isSeriesChild =
      (parentNode != null && parentNode.issue?.executionMode === ExecutionMode.SERIES) ||
      (seriesHiddenParent != null &&
        seriesHiddenParent.issue?.executionMode === ExecutionMode.SERIES)

    // Compute DrawTopLine
    let drawTopLine = false
    if (i > 0) {
      const prevNode = group[i - 1]
      if (prevNode.issue?.id) {
        const prevNodeId = prevNode.issue.id.toLowerCase()
        const prevBaseLane = (prevNode.lane ?? 0) - minLane
        const prevParentNode = parentByNode.get(prevNodeId)
        const prevBaseParentLane = prevParentNode ? (prevParentNode.lane ?? 0) - minLane : null

        const prevHiddenParentCheck = hiddenParentByNode.get(prevNodeId)
        const prevIsSeriesChild =
          (prevParentNode != null &&
            prevParentNode.issue?.executionMode === ExecutionMode.SERIES) ||
          (prevHiddenParentCheck != null &&
            prevHiddenParentCheck.issue?.executionMode === ExecutionMode.SERIES)

        if (
          !prevIsSeriesChild &&
          prevBaseParentLane != null &&
          prevBaseParentLane === baseLane &&
          prevBaseParentLane > prevBaseLane
        ) {
          drawTopLine = true
        }

        if (
          isSeriesChild &&
          prevIsSeriesChild &&
          prevParentNode?.issue?.id &&
          parentNode?.issue?.id &&
          prevParentNode.issue.id.toLowerCase() === parentNode.issue.id.toLowerCase()
        ) {
          drawTopLine = true
        }

        if (!drawTopLine) {
          const currentHiddenParent = hiddenParentByNode.get(nodeId)
          if (currentHiddenParent?.issue?.executionMode === ExecutionMode.SERIES) {
            for (let k = i - 1; k >= 0; k--) {
              const prevSiblingId = group[k].issue?.id?.toLowerCase() ?? ''
              const prevHiddenParent = hiddenParentByNode.get(prevSiblingId)
              if (
                prevHiddenParent?.issue?.id &&
                currentHiddenParent?.issue?.id &&
                prevHiddenParent.issue.id.toLowerCase() ===
                  currentHiddenParent.issue.id.toLowerCase()
              ) {
                drawTopLine = true
                break
              }
            }
          }
        }
      }
    }

    const hasVisibleSeriesParent =
      parentNode != null && parentNode.issue?.executionMode === ExecutionMode.SERIES
    let drawBottomLine = hasVisibleSeriesParent

    if (!drawBottomLine) {
      const bottomLineHiddenParent = hiddenParentByNode.get(nodeId)
      if (bottomLineHiddenParent?.issue?.executionMode === ExecutionMode.SERIES) {
        for (let j = i + 1; j < group.length; j++) {
          const nextHiddenParent = hiddenParentByNode.get(group[j].issue?.id?.toLowerCase() ?? '')
          if (
            nextHiddenParent?.issue?.id &&
            bottomLineHiddenParent?.issue?.id &&
            nextHiddenParent.issue.id.toLowerCase() ===
              bottomLineHiddenParent.issue.id.toLowerCase()
          ) {
            drawBottomLine = true
            break
          }
        }
      }
    }

    const childLane = seriesChildLaneByParent.get(nodeId)
    const seriesConnectorFromLane = childLane != null ? childLane + laneOffset : null

    const linkedPr =
      linkedPrs[node.issue.id] ??
      linkedPrs[node.issue.id.toLowerCase()] ??
      linkedPrs[node.issue.id.toUpperCase()] ??
      null
    const agentStatus =
      agentStatuses[node.issue.id] ??
      agentStatuses[node.issue.id.toLowerCase()] ??
      agentStatuses[node.issue.id.toUpperCase()] ??
      null

    let hasHiddenParent = false
    let hiddenParentIsSeriesMode = false

    if (hiddenParentInfo) {
      const info = hiddenParentInfo.get(nodeId)
      if (info) {
        hasHiddenParent = info.hasHiddenParent
        hiddenParentIsSeriesMode = info.hiddenParentIsSeriesMode

        if (hasHiddenParent && hiddenParentIsSeriesMode && drawBottomLine) {
          hasHiddenParent = false
        }
      }
    }

    const branchName = generateBranchName(node.issue)

    result.push({
      type: 'issue',
      issueId: node.issue.id,
      title: node.issue.title ?? '',
      description: node.issue.description ?? null,
      branchName,
      lane,
      marker,
      parentLane,
      isFirstChild,
      isLastChild: false,
      isSeriesChild,
      drawTopLine,
      drawBottomLine,
      seriesConnectorFromLane,
      issueType: node.issue.type ?? IssueType.TASK,
      status: node.issue.status ?? IssueStatus.DRAFT,
      hasDescription: !!(node.issue.description && node.issue.description.trim()),
      linkedPr,
      agentStatus,
      assignedTo: node.issue.assignedTo ?? null,
      drawLane0Connector: false,
      isLastLane0Connector: false,
      drawLane0PassThrough: false,
      lane0Color: groupLane0Color,
      hasHiddenParent,
      hiddenParentIsSeriesMode,
      executionMode: node.issue.executionMode ?? ExecutionMode.SERIES,
      parentIssues: node.issue.parentIssues ?? null,
      multiParentIndex: null,
      multiParentTotal: null,
      hasParallelChildren: false,
      parentIssueId: null,
      parentLaneReservations: [],
    })
  }
}

function getMarker(node: TaskGraphNodeResponse): TaskGraphMarkerType {
  const status = node.issue?.status

  switch (status) {
    case IssueStatus.COMPLETE:
      return TaskGraphMarkerType.Complete
    case IssueStatus.CLOSED:
    case IssueStatus.ARCHIVED:
      return TaskGraphMarkerType.Closed
    default:
      return node.isActionable ? TaskGraphMarkerType.Actionable : TaskGraphMarkerType.Open
  }
}

// ============================================================================
// Tree View Layout Functions
// ============================================================================

/** Node with computed tree view lane */
interface TreeViewNode {
  node: TaskGraphNodeResponse
  lane: number
  parentLane: number | null
  parentNode: TaskGraphNodeResponse | null
}

/**
 * Computes tree view layout for a group of nodes.
 * Uses BFS from roots to assign lanes (root=0, children=parent+1).
 */
function computeTreeViewLayout(
  group: TaskGraphNodeResponse[],
  maxDepth: number,
  _allNodesByIssueId: Map<string, TaskGraphNodeResponse>
): TreeViewNode[] {
  if (group.length === 0) return []

  // Build child map: parent ID -> child nodes
  const childrenByParent = new Map<string, TaskGraphNodeResponse[]>()
  const nodeById = new Map<string, TaskGraphNodeResponse>()

  for (const node of group) {
    if (!node.issue?.id) continue
    nodeById.set(node.issue.id.toLowerCase(), node)
  }

  // Find roots (nodes with no parents in this group)
  const roots: TaskGraphNodeResponse[] = []
  for (const node of group) {
    if (!node.issue?.id) continue

    let hasParentInGroup = false
    for (const parentRef of node.issue.parentIssues ?? []) {
      if (parentRef.parentIssue && nodeById.has(parentRef.parentIssue.toLowerCase())) {
        hasParentInGroup = true
        const parentId = parentRef.parentIssue.toLowerCase()
        const children = childrenByParent.get(parentId) ?? []
        children.push(node)
        childrenByParent.set(parentId, children)
      }
    }

    if (!hasParentInGroup) {
      roots.push(node)
    }
  }

  // Count how many parents each node has in the group (for multi-parent detection)
  const parentCountInGroup = new Map<string, number>()
  for (const node of group) {
    if (!node.issue?.id) continue
    let count = 0
    for (const parentRef of node.issue.parentIssues ?? []) {
      if (parentRef.parentIssue && nodeById.has(parentRef.parentIssue.toLowerCase())) {
        count++
      }
    }
    parentCountInGroup.set(node.issue.id.toLowerCase(), count)
  }

  // DFS from roots to assign lanes
  const result: TreeViewNode[] = []
  const enqueueCount = new Map<string, number>()
  const queue: Array<{
    node: TaskGraphNodeResponse
    lane: number
    parentNode: TaskGraphNodeResponse | null
  }> = []

  // Add roots to queue (lane 0)
  for (const root of roots) {
    if (root.issue?.id) {
      const rootId = root.issue.id.toLowerCase()
      if ((enqueueCount.get(rootId) ?? 0) === 0) {
        queue.push({ node: root, lane: 0, parentNode: null })
        enqueueCount.set(rootId, 1)
      }
    }
  }

  while (queue.length > 0) {
    const { node, lane, parentNode } = queue.shift()!

    // Filter by maxDepth
    if (lane > maxDepth) continue

    result.push({
      node,
      lane,
      parentLane: parentNode
        ? (result.find(
            (r) => r.node.issue?.id?.toLowerCase() === parentNode.issue?.id?.toLowerCase()
          )?.lane ?? null)
        : null,
      parentNode,
    })

    // Add children to front of queue (DFS) sorted by sortOrder
    const nodeId = node.issue?.id?.toLowerCase()
    if (nodeId) {
      const children = childrenByParent.get(nodeId) ?? []
      const childEntries: typeof queue = []
      for (const child of children) {
        if (!child.issue?.id) continue
        const childId = child.issue.id.toLowerCase()
        const timesEnqueued = enqueueCount.get(childId) ?? 0
        const totalParents = parentCountInGroup.get(childId) ?? 1

        // Allow multi-parent nodes to be enqueued once per parent
        if (timesEnqueued < totalParents) {
          childEntries.push({ node: child, lane: lane + 1, parentNode: node })
          enqueueCount.set(childId, timesEnqueued + 1)
        }
      }
      // Sort children by sortOrder from parentIssues reference
      childEntries.sort((a, b) => {
        const aSortOrder =
          a.node.issue?.parentIssues?.find((p) => p.parentIssue?.toLowerCase() === nodeId)
            ?.sortOrder ?? ''
        const bSortOrder =
          b.node.issue?.parentIssues?.find((p) => p.parentIssue?.toLowerCase() === nodeId)
            ?.sortOrder ?? ''
        return aSortOrder.localeCompare(bSortOrder)
      })
      queue.unshift(...childEntries)
    }
  }

  return result
}

/**
 * Computes hidden parent info for tree view nodes.
 */
function computeHiddenParentInfoTreeView(
  treeViewNodes: TreeViewNode[],
  allGroupNodes: TaskGraphNodeResponse[],
  allNodesByIssueId: Map<string, TaskGraphNodeResponse>,
  _maxDepth: number
): Map<string, { hasHiddenParent: boolean; hiddenParentIsSeriesMode: boolean }> {
  const result = new Map<string, { hasHiddenParent: boolean; hiddenParentIsSeriesMode: boolean }>()
  const visibleIds = new Set(
    treeViewNodes.map((t) => t.node.issue?.id?.toLowerCase()).filter(Boolean)
  )

  for (const treeNode of treeViewNodes) {
    if (!treeNode.node.issue?.id) continue

    let hasHiddenParent = false
    let hiddenParentIsSeriesMode = false

    // Check each parent reference
    for (const parentRef of treeNode.node.issue.parentIssues ?? []) {
      if (!parentRef.parentIssue) continue

      // Find the parent node in all nodes (not just visible)
      const parentNode = allNodesByIssueId.get(parentRef.parentIssue.toLowerCase())

      if (parentNode) {
        // Check if parent is in the group but filtered out
        const parentInGroup = allGroupNodes.some(
          (n) => n.issue?.id?.toLowerCase() === parentRef.parentIssue?.toLowerCase()
        )

        if (parentInGroup && !visibleIds.has(parentRef.parentIssue.toLowerCase())) {
          hasHiddenParent = true
          hiddenParentIsSeriesMode = parentNode.issue?.executionMode === ExecutionMode.SERIES
        }
      }
    }

    result.set(treeNode.node.issue.id.toLowerCase(), { hasHiddenParent, hiddenParentIsSeriesMode })
  }

  return result
}

/**
 * Renders tree view nodes to render lines.
 */
function renderGroupTreeView(
  result: TaskGraphRenderLine[],
  treeViewNodes: TreeViewNode[],
  agentStatuses: Record<string, AgentStatusData>,
  linkedPrs: Record<string, TaskGraphLinkedPr>,
  hiddenParentInfo?: Map<string, { hasHiddenParent: boolean; hiddenParentIsSeriesMode: boolean }>
): void {
  if (treeViewNodes.length === 0) return

  // Pre-compute series child relationships and children count
  const childrenCountByParent = new Map<string, number>()
  const childrenRendered = new Map<string, number>()
  const seriesChildLaneByParent = new Map<string, number>()

  for (const treeNode of treeViewNodes) {
    if (!treeNode.node.issue?.id) continue
    if (!treeNode.parentNode?.issue?.id) continue

    const parentId = treeNode.parentNode.issue.id.toLowerCase()
    childrenCountByParent.set(parentId, (childrenCountByParent.get(parentId) ?? 0) + 1)

    // Track series child lanes
    if (treeNode.parentNode.issue.executionMode === ExecutionMode.SERIES) {
      seriesChildLaneByParent.set(parentId, treeNode.lane)
    }
  }

  for (let i = 0; i < treeViewNodes.length; i++) {
    const treeNode = treeViewNodes[i]
    const node = treeNode.node
    if (!node.issue?.id) continue

    const nodeId = node.issue.id.toLowerCase()
    const lane = treeNode.lane
    const marker = getMarker(node)
    const parentNode = treeNode.parentNode
    const parentLane = treeNode.parentLane

    let isFirstChild = false
    let isLastChild = false
    if (parentNode?.issue?.id && parentLane !== null && parentLane < lane) {
      const parentId = parentNode.issue.id.toLowerCase()
      const count = (childrenRendered.get(parentId) ?? 0) + 1
      childrenRendered.set(parentId, count)
      isFirstChild = count === 1
      isLastChild = count === (childrenCountByParent.get(parentId) ?? 0)
    }

    // Determine if this is a series child
    const isSeriesChild =
      parentNode != null && parentNode.issue?.executionMode === ExecutionMode.SERIES

    // Compute DrawTopLine and DrawBottomLine for series children
    let drawTopLine = false
    let drawBottomLine = false

    if (isSeriesChild && parentNode?.issue?.id) {
      // For series children, connect vertically
      // First series child connects to parent above
      if (isFirstChild) {
        drawTopLine = true
      }
      // Check if there's a sibling series child above
      if (i > 0) {
        const prevTreeNode = treeViewNodes[i - 1]
        if (
          prevTreeNode.parentNode?.issue?.id?.toLowerCase() === parentNode.issue.id.toLowerCase() &&
          prevTreeNode.parentNode?.issue?.executionMode === ExecutionMode.SERIES
        ) {
          drawTopLine = true
        }
      }

      // Check if there's a sibling series child below
      const nextTreeNode = treeViewNodes[i + 1]
      if (
        nextTreeNode &&
        nextTreeNode.parentNode?.issue?.id?.toLowerCase() === parentNode.issue.id.toLowerCase() &&
        nextTreeNode.parentNode?.issue?.executionMode === ExecutionMode.SERIES
      ) {
        drawBottomLine = true
      }
    }

    // Compute SeriesConnectorFromLane
    const childLane = seriesChildLaneByParent.get(nodeId)
    const seriesConnectorFromLane = childLane != null ? childLane : null

    // Get linked PR and agent status
    const linkedPr =
      linkedPrs[node.issue.id] ??
      linkedPrs[node.issue.id.toLowerCase()] ??
      linkedPrs[node.issue.id.toUpperCase()] ??
      null
    const agentStatus =
      agentStatuses[node.issue.id] ??
      agentStatuses[node.issue.id.toLowerCase()] ??
      agentStatuses[node.issue.id.toUpperCase()] ??
      null

    // Get hidden parent info
    let hasHiddenParent = false
    let hiddenParentIsSeriesMode = false

    if (hiddenParentInfo) {
      const info = hiddenParentInfo.get(nodeId)
      if (info) {
        hasHiddenParent = info.hasHiddenParent
        hiddenParentIsSeriesMode = info.hiddenParentIsSeriesMode

        if (hasHiddenParent && hiddenParentIsSeriesMode && drawBottomLine) {
          hasHiddenParent = false
        }
      }
    }

    // Generate branch name
    const branchName = generateBranchName(node.issue)

    // Compute hasParallelChildren
    const childCount = childrenCountByParent.get(nodeId) ?? 0
    const hasParallelChildren =
      childCount > 0 &&
      (node.issue.executionMode ?? ExecutionMode.SERIES) === ExecutionMode.PARALLEL

    result.push({
      type: 'issue',
      issueId: node.issue.id,
      title: node.issue.title ?? '',
      description: node.issue.description ?? null,
      branchName,
      lane,
      marker,
      parentLane,
      isFirstChild,
      isLastChild,
      isSeriesChild,
      drawTopLine,
      drawBottomLine,
      seriesConnectorFromLane,
      issueType: node.issue.type ?? IssueType.TASK,
      status: node.issue.status ?? IssueStatus.DRAFT,
      hasDescription: !!(node.issue.description && node.issue.description.trim()),
      linkedPr,
      agentStatus,
      assignedTo: node.issue.assignedTo ?? null,
      drawLane0Connector: false,
      isLastLane0Connector: false,
      drawLane0PassThrough: false,
      lane0Color: null, // No lane 0 connector color in tree view
      hasHiddenParent,
      hiddenParentIsSeriesMode,
      executionMode: node.issue.executionMode ?? ExecutionMode.SERIES,
      parentIssues: node.issue.parentIssues ?? null,
      multiParentIndex: null,
      multiParentTotal: null,
      hasParallelChildren,
      parentIssueId: parentNode?.issue?.id ?? null,
      parentLaneReservations: [],
    })
  }
}
