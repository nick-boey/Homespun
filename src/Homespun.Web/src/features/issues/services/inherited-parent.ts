/**
 * Inherited parent computation for issue creation.
 *
 * When creating a new issue adjacent to an existing issue, this service
 * determines what parent (if any) the new issue should inherit to become
 * a sibling of the reference issue.
 */

import type { TaskGraphResponse, TaskGraphNodeResponse } from '@/api'
import { computeMidpoint } from '../utils/lex-order'

/**
 * Result of computing inherited parent info.
 */
export interface InheritedParentInfo {
  /** The parent issue ID to inherit, or null if the new issue should be an orphan. */
  parentIssueId: string | null
  /** The sort order for the new issue within the parent's children. */
  sortOrder: string | null
}

/**
 * Computes the inherited parent info for a new issue being created
 * adjacent to a reference issue.
 *
 * This implements the "sibling creation" behavior:
 * - If the reference issue has a parent, the new issue inherits that parent
 * - If the reference issue has no parent, the new issue is an orphan
 *
 * @param taskGraph - The current task graph data
 * @param referenceIssueId - The ID of the issue adjacent to where the new issue will be created
 * @param isAbove - True if creating above the reference issue (Shift+O), false if below (o)
 * @returns The inherited parent info, or null if reference issue not found
 */
export function computeInheritedParentInfo(
  taskGraph: TaskGraphResponse | null | undefined,
  referenceIssueId: string | undefined,
  isAbove: boolean
): InheritedParentInfo | null {
  if (!taskGraph?.nodes || !referenceIssueId) {
    return null
  }

  // Find the reference issue node
  const referenceNode = findNodeByIssueId(taskGraph.nodes, referenceIssueId)
  if (!referenceNode?.issue) {
    return null
  }

  // Get the reference issue's first parent
  const parentRef = referenceNode.issue.parentIssues?.[0]
  if (!parentRef?.parentIssue) {
    // Reference issue has no parent - new issue will be an orphan
    return { parentIssueId: null, sortOrder: null }
  }

  // Reference issue has a parent - new issue inherits that parent
  const parentIssueId = parentRef.parentIssue

  // Compute sort order for positioning among siblings
  const sortOrder = computeSortOrderForSibling(
    taskGraph.nodes,
    parentIssueId,
    referenceIssueId,
    isAbove
  )

  return { parentIssueId, sortOrder }
}

/**
 * Finds a node in the task graph by issue ID.
 */
function findNodeByIssueId(
  nodes: TaskGraphNodeResponse[],
  issueId: string
): TaskGraphNodeResponse | undefined {
  return nodes.find((n) => n.issue?.id?.toLowerCase() === issueId.toLowerCase())
}

/**
 * Computes the sort order for a new sibling issue.
 *
 * @param nodes - All nodes in the task graph
 * @param parentIssueId - The parent issue ID
 * @param referenceIssueId - The sibling reference issue ID
 * @param isAbove - True if inserting above the reference, false if below
 * @returns The computed sort order string
 */
function computeSortOrderForSibling(
  nodes: TaskGraphNodeResponse[],
  parentIssueId: string,
  referenceIssueId: string,
  isAbove: boolean
): string {
  // Find all siblings (children of the same parent)
  const siblings = nodes
    .filter((n) => {
      const parentRef = n.issue?.parentIssues?.[0]
      return parentRef?.parentIssue?.toLowerCase() === parentIssueId.toLowerCase()
    })
    .map((n) => ({
      issueId: n.issue!.id!,
      sortOrder: n.issue!.parentIssues?.[0]?.sortOrder ?? 'V',
      row: n.row ?? 0,
    }))
    // Sort by row (visual order in task graph)
    .sort((a, b) => a.row - b.row)

  // Find the reference sibling's index
  const referenceIndex = siblings.findIndex(
    (s) => s.issueId.toLowerCase() === referenceIssueId.toLowerCase()
  )

  if (referenceIndex < 0) {
    // Reference not found among siblings - use default
    return computeMidpoint(null, null)
  }

  const referenceSibling = siblings[referenceIndex]

  if (isAbove) {
    // Insert above reference (before in sort order)
    const previousSibling = referenceIndex > 0 ? siblings[referenceIndex - 1] : null
    return computeMidpoint(previousSibling?.sortOrder ?? null, referenceSibling.sortOrder)
  } else {
    // Insert below reference (after in sort order)
    const nextSibling = referenceIndex < siblings.length - 1 ? siblings[referenceIndex + 1] : null
    return computeMidpoint(referenceSibling.sortOrder, nextSibling?.sortOrder ?? null)
  }
}
