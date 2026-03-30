/**
 * Inherited parent computation for issue creation.
 *
 * When creating a new issue adjacent to an existing issue, this service
 * determines what parent (if any) the new issue should inherit to become
 * a sibling of the reference issue.
 */

import type { TaskGraphResponse, TaskGraphNodeResponse } from '@/api'

/**
 * Result of computing inherited parent info.
 */
export interface InheritedParentInfo {
  /** The parent issue ID to inherit, or null if the new issue should be an orphan. */
  parentIssueId: string | null
  /** The sibling issue ID for positioning, or null if no sibling context. */
  siblingIssueId: string | null
  /** If true, insert before the sibling; if false, insert after. */
  insertBefore: boolean
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
    return { parentIssueId: null, siblingIssueId: null, insertBefore: false }
  }

  // Reference issue has a parent - new issue inherits that parent
  // Position relative to the reference issue
  return {
    parentIssueId: parentRef.parentIssue,
    siblingIssueId: referenceIssueId,
    insertBefore: isAbove,
  }
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
