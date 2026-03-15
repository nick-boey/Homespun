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
} from '@/api'
import { ExecutionMode, IssueStatus, IssueType } from '@/api'
import { getPriorityColor } from './priority-colors'
import { generateBranchName } from './branch-name'

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
 * Computes the layout for a task graph, optionally filtering nodes by depth.
 *
 * @param taskGraph - The task graph response to layout
 * @param maxDepth - Maximum depth (lane) to display. Default is 3 (shows lanes 0-3).
 *                   Use Infinity to show all levels.
 * @returns List of render lines for the task graph view
 */
export function computeLayout(
  taskGraph: TaskGraphResponse | null | undefined,
  maxDepth = 3
): TaskGraphRenderLine[] {
  if (!taskGraph) {
    return []
  }

  const nodes = taskGraph.nodes ?? []
  const mergedPrs = taskGraph.mergedPrs ?? []
  const agentStatuses = taskGraph.agentStatuses ?? {}
  const linkedPrs = taskGraph.linkedPrs ?? {}

  // Return early if no nodes and no PRs
  if (nodes.length === 0 && mergedPrs.length === 0) {
    return []
  }

  const result: TaskGraphRenderLine[] = []

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

    // First PR: no top line (nothing above it)
    // Last PR: has bottom line only if there are issues below
    // Middle PRs: both top and bottom lines
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

  // When merged PRs exist, offset all lanes by +1 to make room for the
  // vertical connector line in lane 0 that connects PRs to issues
  const laneOffset = mergedPrs.length > 0 ? 1 : 0

  // Build lookup for hidden parent detection
  const allNodesByIssueId = new Map<string, TaskGraphNodeResponse>()
  for (const node of nodes) {
    if (node.issue?.id) {
      allNodesByIssueId.set(node.issue.id.toLowerCase(), node)
    }
  }

  const groups = groupNodes(nodes)
  for (const group of groups) {
    // Filter nodes by maxDepth within each group
    // First compute minLane for this group to normalize lanes
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

    renderGroup(result, visibleNodes, group, agentStatuses, linkedPrs, laneOffset, hiddenParentInfo)
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
      // First pass: find the last issue that actually gets a connector
      // (blocked series siblings don't get connectors)
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

      // Second pass: apply the flags
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

  return result
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

    // Check each parent reference
    for (const parentRef of node.issue.parentIssues ?? []) {
      if (!parentRef.parentIssue) continue

      // Find the parent node in all nodes (not just visible)
      const parentNode = allNodesByIssueId.get(parentRef.parentIssue.toLowerCase())

      if (parentNode) {
        // Check if parent is in this group and was filtered out
        const parentInGroup = allGroupNodes.some(
          (n) => n.issue?.id?.toLowerCase() === parentRef.parentIssue?.toLowerCase()
        )

        if (parentInGroup && !visibleIds.has(parentRef.parentIssue.toLowerCase())) {
          // Parent was filtered out
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

      // Find parent in allGroupNodes that is NOT in visible nodes
      const hiddenParent = allGroupNodes.find(
        (n) =>
          n.issue?.id?.toLowerCase() === parentRef.parentIssue?.toLowerCase() &&
          !visibleNodeIds.has(n.issue?.id?.toLowerCase())
      )

      if (hiddenParent) {
        hiddenParentByNode.set(node.issue.id.toLowerCase(), hiddenParent)
        break // Use first found hidden parent
      }
    }
  }

  // Pre-compute series child lane by parent: for parents with Series execution mode
  // Store the original (non-offset) lanes for internal calculations
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
    const lane = baseLane + laneOffset // Apply lane offset for final position
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

    // Determine if this is a series child (parent has Series execution mode)
    // Also check hidden parent for nodes whose visible parent was filtered out
    const seriesHiddenParent = hiddenParentByNode.get(nodeId)
    const isSeriesChild =
      (parentNode != null && parentNode.issue?.executionMode === ExecutionMode.SERIES) ||
      (seriesHiddenParent != null &&
        seriesHiddenParent.issue?.executionMode === ExecutionMode.SERIES)

    // Compute DrawTopLine (uses base lanes for calculations)
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

        // Previous node is a parallel child whose junction is at this node's lane
        if (
          !prevIsSeriesChild &&
          prevBaseParentLane != null &&
          prevBaseParentLane === baseLane &&
          prevBaseParentLane > prevBaseLane
        ) {
          drawTopLine = true
        }

        // Previous node is a series sibling of the same parent (vertical continuity)
        if (
          isSeriesChild &&
          prevIsSeriesChild &&
          prevParentNode?.issue?.id &&
          parentNode?.issue?.id &&
          prevParentNode.issue.id.toLowerCase() === parentNode.issue.id.toLowerCase()
        ) {
          drawTopLine = true
        }

        // Check for series siblings with same hidden parent (when visible parent is filtered out)
        if (!drawTopLine) {
          const currentHiddenParent = hiddenParentByNode.get(nodeId)
          if (currentHiddenParent?.issue?.executionMode === ExecutionMode.SERIES) {
            const prevHiddenParent = hiddenParentByNode.get(prevNodeId)
            if (
              prevHiddenParent?.issue?.id &&
              currentHiddenParent?.issue?.id &&
              prevHiddenParent.issue.id.toLowerCase() === currentHiddenParent.issue.id.toLowerCase()
            ) {
              drawTopLine = true
            }
          }
        }
      }
    }

    // Compute DrawBottomLine:
    // - For nodes with visible series parent: always true (connects to the visible parent)
    // - For nodes with hidden series parent: true only if there's a next sibling with same hidden parent
    const hasVisibleSeriesParent =
      parentNode != null && parentNode.issue?.executionMode === ExecutionMode.SERIES
    let drawBottomLine = hasVisibleSeriesParent

    // For nodes with hidden series parent, check if there's a next sibling
    if (!drawBottomLine) {
      const bottomLineHiddenParent = hiddenParentByNode.get(nodeId)
      if (bottomLineHiddenParent?.issue?.executionMode === ExecutionMode.SERIES) {
        // Check if there's a next sibling with same hidden parent
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

    // Compute SeriesConnectorFromLane: set when this node is a parent receiving series children
    // Apply lane offset to the series connector lane as well
    const childLane = seriesChildLaneByParent.get(nodeId)
    const seriesConnectorFromLane = childLane != null ? childLane + laneOffset : null

    // Get linked PR and agent status for this issue
    // Use case-insensitive lookup to match server-side behavior
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

    // Get hidden parent info (defaults to false if not provided)
    let hasHiddenParent = false
    let hiddenParentIsSeriesMode = false

    if (hiddenParentInfo) {
      const info = hiddenParentInfo.get(nodeId)
      if (info) {
        hasHiddenParent = info.hasHiddenParent
        hiddenParentIsSeriesMode = info.hiddenParentIsSeriesMode

        // For series siblings with hidden parent, only show indicator on last sibling
        // (the one without a connection to the next sibling)
        if (hasHiddenParent && hiddenParentIsSeriesMode && drawBottomLine) {
          hasHiddenParent = false
        }
      }
    }

    // Generate branch name for the issue
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
