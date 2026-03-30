/**
 * Computes virtual node data for the pending new issue preview.
 *
 * When a user presses 'o' or 'Shift+O' to create a new issue,
 * this function computes where the virtual preview node should appear
 * on the Konva canvas and where the inline editor should be positioned.
 */

import type { TaskGraphIssueRenderLine } from '../../services'
import type { PendingNewIssue } from '../../types'
import { KeyboardEditMode } from '../../types'

/**
 * Data for rendering a virtual node preview during issue creation.
 */
export interface VirtualNodeData {
  /** Row index where the new issue will be inserted (0-based). */
  insertionRowIndex: number
  /** Lane for the virtual node on the canvas. */
  virtualLane: number
  /** The reference render line that the new issue is adjacent to. */
  refLine: TaskGraphIssueRenderLine
}

/**
 * Computes virtual node data for a pending new issue.
 *
 * @param pendingNewIssue - The pending issue being created
 * @param editMode - Current keyboard edit mode
 * @param issueRenderLines - The current list of issue render lines
 * @returns Virtual node data, or null if not in creation mode
 */
export function computeVirtualNodeData(
  pendingNewIssue: PendingNewIssue | null,
  editMode: KeyboardEditMode,
  issueRenderLines: TaskGraphIssueRenderLine[]
): VirtualNodeData | null {
  if (!pendingNewIssue || editMode !== KeyboardEditMode.CreatingNew) return null

  const refIndex = issueRenderLines.findIndex(
    (line) => line.issueId === pendingNewIssue.referenceIssueId
  )
  if (refIndex < 0) return null

  const insertionRowIndex = pendingNewIssue.isAbove ? refIndex : refIndex + 1
  const refLine = issueRenderLines[refIndex]

  const virtualLane = computeVirtualLane(refLine, pendingNewIssue)

  return { insertionRowIndex, virtualLane, refLine }
}

/**
 * Computes the lane for a virtual node based on the pending issue state.
 *
 * - Default (sibling): same lane as reference issue
 * - Tab (pendingChildId set): one lane to the right (parent position)
 * - Shift+Tab (pendingParentId set): one lane to the left (child position)
 */
export function computeVirtualLane(
  refLine: TaskGraphIssueRenderLine,
  pendingNewIssue: PendingNewIssue
): number {
  if (pendingNewIssue.pendingChildId) {
    // Tab: new issue becomes parent of reference → one lane right
    return refLine.lane + 1
  }
  if (pendingNewIssue.pendingParentId) {
    // Shift+Tab: new issue becomes child of reference → one lane left
    return Math.max(0, refLine.lane - 1)
  }
  // Default: sibling → same lane
  return refLine.lane
}

/**
 * Computes the display row index for an issue, accounting for the
 * insertion of a virtual node row.
 *
 * Rows at or after the insertion point shift down by one to make space.
 *
 * @param rowIndex - The original row index
 * @param insertionRowIndex - Where the virtual row is inserted, or null if none
 * @returns The display row index for positioning
 */
export function getDisplayRowIndex(rowIndex: number, insertionRowIndex: number | null): number {
  if (insertionRowIndex === null) return rowIndex
  return rowIndex >= insertionRowIndex ? rowIndex + 1 : rowIndex
}
