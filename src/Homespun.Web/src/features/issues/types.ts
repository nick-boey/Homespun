import { IssueType } from '@/api'
import type { TaskGraphNodeResponse } from '@/api/generated/types.gen'

/**
 * Keyboard edit mode states for the task graph.
 */
export const KeyboardEditMode = {
  /** Normal viewing mode - keyboard commands navigate the task graph. */
  Viewing: 'Viewing',
  /** Editing an existing issue's title. */
  EditingExisting: 'EditingExisting',
  /** Creating a new issue inline. */
  CreatingNew: 'CreatingNew',
  /** Selecting an agent prompt from the dropdown. */
  SelectingAgentPrompt: 'SelectingAgentPrompt',
  /** Selecting a move target (for Make Child Of / Make Parent Of operations). */
  SelectingMoveTarget: 'SelectingMoveTarget',
} as const

export type KeyboardEditMode = (typeof KeyboardEditMode)[keyof typeof KeyboardEditMode]

/**
 * Type of move operation being performed.
 */
export const MoveOperationType = {
  /** Make the source issue a child of the target issue. */
  AsChildOf: 'AsChildOf',
  /** Make the target issue a child of the source issue. */
  AsParentOf: 'AsParentOf',
  /** Remove a specific parent from the source issue. */
  RemoveParent: 'RemoveParent',
} as const

export type MoveOperationType = (typeof MoveOperationType)[keyof typeof MoveOperationType]

/**
 * Cursor position when entering edit mode.
 */
export const EditCursorPosition = {
  /** Cursor at the start of the text (i command). */
  Start: 'Start',
  /** Cursor at the end of the text (a command). */
  End: 'End',
  /** Text cleared and cursor at start (r command). */
  Replace: 'Replace',
} as const

export type EditCursorPosition = (typeof EditCursorPosition)[keyof typeof EditCursorPosition]

/**
 * State for inline editing of an issue title.
 */
export interface InlineEditState {
  /** The issue ID being edited. */
  issueId: string
  /** The current title text being edited. */
  title: string
  /** The original title before editing (for cancel/revert). */
  originalTitle: string
  /** Where the cursor should be positioned when entering edit mode. */
  cursorPosition: EditCursorPosition
}

/**
 * Represents a pending new issue that exists only client-side until saved.
 */
export interface PendingNewIssue {
  /** Position in the render list where this issue should be inserted. */
  insertAtIndex: number
  /** The title being entered for the new issue. */
  title: string
  /** Parent ID set when Shift+Tab is pressed to make this a child of the adjacent issue. */
  pendingParentId?: string
  /** Child ID set when Tab is pressed to make this a parent of the adjacent issue. */
  pendingChildId?: string
  /** Sort order for series parent positioning. */
  sortOrder?: string
  /** True if created with Shift+O (above current), false for o (below current). */
  isAbove: boolean
  /** Reference issue ID used to determine placement context. */
  referenceIssueId?: string
  /** Inherited parent issue ID from the reference issue's parent (sibling creation). */
  inheritedParentIssueId?: string
  /** Inherited sort order for the parent relationship. */
  inheritedParentSortOrder?: string
}

/**
 * Search state for the task graph.
 */
export interface SearchState {
  /** The current search term being searched for. */
  searchTerm: string
  /** True when the search bar is open and user is typing. */
  isSearching: boolean
  /** True when search is committed and user is navigating results with n/N. */
  isSearchEmbedded: boolean
  /** Indices in the render list that match the search term. */
  matchingIndices: number[]
  /** Current position in matchingIndices for n/N navigation. */
  currentMatchIndex: number
}

/**
 * Move operation state for the task graph.
 */
export interface MoveOperationState {
  /** The current move operation type when in SelectingMoveTarget mode. */
  currentMoveOperation?: MoveOperationType
  /** The source issue ID when in SelectingMoveTarget mode. */
  moveSourceIssueId?: string
}

/**
 * A simplified render line representation for navigation.
 * This captures the essential fields needed for keyboard navigation from TaskGraphNodeResponse.
 */
export interface TaskGraphRenderLine {
  /** The issue ID. */
  issueId: string
  /** The issue title. */
  title: string
  /** The lane (column) position in the graph. */
  lane: number
  /** The parent lane (column) if this issue has a parent. */
  parentLane?: number
  /** The issue type. */
  issueType: IssueType
  /** Whether this issue is actionable (next in sequence). */
  isActionable: boolean
}

/**
 * Converts TaskGraphNodeResponse array to TaskGraphRenderLine array.
 */
export function toRenderLines(nodes: TaskGraphNodeResponse[]): TaskGraphRenderLine[] {
  return nodes
    .filter((node) => node.issue?.id)
    .map((node) => ({
      issueId: node.issue!.id!,
      title: node.issue!.title ?? '',
      lane: node.lane ?? 0,
      parentLane: node.issue?.parentIssues?.[0]?.parentIssue
        ? findParentLane(nodes, node.issue.parentIssues[0].parentIssue)
        : undefined,
      issueType: node.issue!.type ?? IssueType.TASK,
      isActionable: node.isActionable ?? false,
    }))
}

/**
 * Finds the lane of a parent issue by its ID.
 */
function findParentLane(nodes: TaskGraphNodeResponse[], parentId: string): number | undefined {
  const parentNode = nodes.find((n) => n.issue?.id === parentId)
  return parentNode?.lane
}

/**
 * Sibling move info for a selected issue.
 */
export interface SiblingMoveInfo {
  canMoveUp: boolean
  canMoveDown: boolean
  hasSingleParent: boolean
}

/**
 * Move direction for sibling reordering.
 */
export const MoveDirection = {
  Up: 'Up',
  Down: 'Down',
} as const

export type MoveDirection = (typeof MoveDirection)[keyof typeof MoveDirection]

/**
 * Issue type cycling order: Task -> Bug -> Feature -> Chore -> Task
 */
export const TYPE_CYCLE_ORDER = [0, 1, 2, 3] as const // Task=0, Bug=1, Feature=2, Chore=3

/**
 * Gets the next issue type in the cycling order.
 */
export function getNextIssueType(currentType: number): number {
  const index = TYPE_CYCLE_ORDER.indexOf(currentType as (typeof TYPE_CYCLE_ORDER)[number])
  if (index < 0) return TYPE_CYCLE_ORDER[0]
  return TYPE_CYCLE_ORDER[(index + 1) % TYPE_CYCLE_ORDER.length]
}

/**
 * Type cycling debounce duration in milliseconds (3 seconds).
 */
export const TYPE_CYCLE_DEBOUNCE_MS = 3000

/**
 * View mode for the task graph display.
 */
export const ViewMode = {
  /** Next view - shows actionable issues at root with parents as leaves. */
  Next: 'next',
  /** Tree view - traditional hierarchy with parents at left, children progressing right. */
  Tree: 'tree',
} as const

export type ViewMode = (typeof ViewMode)[keyof typeof ViewMode]

/**
 * Render mode for the task graph visualization.
 */
export const RenderMode = {
  /** SVG render mode - traditional row-by-row SVG rendering. */
  Svg: 'svg',
  /** Canvas render mode - Konva canvas with full edge paths. */
  Canvas: 'canvas',
} as const

export type RenderMode = (typeof RenderMode)[keyof typeof RenderMode]
