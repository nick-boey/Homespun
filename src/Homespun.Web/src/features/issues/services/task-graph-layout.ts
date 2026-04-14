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
 * Uses tree view layout: parents at left (lane 0), children progressing right.
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

  // Return early if no nodes
  if (nodes.length === 0) {
    return []
  }

  const result: TaskGraphRenderLine[] = []
  const agentStatuses = taskGraph.agentStatuses ?? {}
  const linkedPrs = taskGraph.linkedPrs ?? {}

  // Build lookup for hidden parent detection
  const allNodesByIssueId = new Map<string, TaskGraphNodeResponse>()
  for (const node of nodes) {
    if (node.issue?.id) {
      allNodesByIssueId.set(node.issue.id.toLowerCase(), node)
    }
  }

  const groups = groupNodes(nodes)
  for (const group of groups) {
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

  // Post-process: compute parentLaneReservations for parallel parent children
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
    // Add reservations to all rows between first and last child (inclusive)
    for (const [pid, range] of childRanges) {
      const parent = parallelParents.get(pid)!
      for (let i = range.firstIdx; i <= range.lastIdx; i++) {
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
