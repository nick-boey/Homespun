/**
 * Issue status and type constants.
 *
 * These values must match the C# enum definitions in Fleece.Core.Models:
 *
 * IssueStatus: Open=0, Progress=1, Review=2, Complete=3, Archived=4, Closed=5, Deleted=6
 * IssueType: Task=0, Bug=1, Chore=2, Feature=3, Idea=4, Verify=5
 */

/**
 * Issue status enum values matching Fleece.Core.Models.IssueStatus
 */
export const ISSUE_STATUS = {
  Open: 0,
  Progress: 1,
  Review: 2,
  Complete: 3,
  Archived: 4,
  Closed: 5,
  Deleted: 6,
} as const

export type IssueStatusValue = (typeof ISSUE_STATUS)[keyof typeof ISSUE_STATUS]

/**
 * Issue status display labels
 */
export const ISSUE_STATUS_LABELS: Record<number, string> = {
  [ISSUE_STATUS.Open]: 'Open',
  [ISSUE_STATUS.Progress]: 'In Progress',
  [ISSUE_STATUS.Review]: 'Review',
  [ISSUE_STATUS.Complete]: 'Complete',
  [ISSUE_STATUS.Archived]: 'Archived',
  [ISSUE_STATUS.Closed]: 'Closed',
  [ISSUE_STATUS.Deleted]: 'Deleted',
}

/**
 * Issue status color classes for badges
 */
export const ISSUE_STATUS_COLORS: Record<number, string> = {
  [ISSUE_STATUS.Open]: 'bg-blue-500/20 text-blue-700 dark:text-blue-400',
  [ISSUE_STATUS.Progress]: 'bg-yellow-500/20 text-yellow-700 dark:text-yellow-400',
  [ISSUE_STATUS.Review]: 'bg-purple-500/20 text-purple-700 dark:text-purple-400',
  [ISSUE_STATUS.Complete]: 'bg-green-500/20 text-green-700 dark:text-green-400',
  [ISSUE_STATUS.Archived]: 'bg-gray-500/20 text-gray-700 dark:text-gray-400',
  [ISSUE_STATUS.Closed]: 'bg-gray-500/20 text-gray-700 dark:text-gray-400',
  [ISSUE_STATUS.Deleted]: 'bg-red-500/20 text-red-700 dark:text-red-400',
}

/**
 * Issue status options for select dropdowns.
 * Ordered by typical workflow progression.
 */
export const ISSUE_STATUS_OPTIONS = [
  { value: String(ISSUE_STATUS.Open), label: 'Open' },
  { value: String(ISSUE_STATUS.Progress), label: 'In Progress' },
  { value: String(ISSUE_STATUS.Review), label: 'Review' },
  { value: String(ISSUE_STATUS.Complete), label: 'Complete' },
  { value: String(ISSUE_STATUS.Closed), label: 'Closed' },
  { value: String(ISSUE_STATUS.Archived), label: 'Archived' },
]

/**
 * Issue type enum values matching Fleece.Core.Models.IssueType
 */
export const ISSUE_TYPE = {
  Task: 0,
  Bug: 1,
  Chore: 2,
  Feature: 3,
  Idea: 4,
  Verify: 5,
} as const

export type IssueTypeValue = (typeof ISSUE_TYPE)[keyof typeof ISSUE_TYPE]

/**
 * Issue type display labels
 */
export const ISSUE_TYPE_LABELS: Record<number, string> = {
  [ISSUE_TYPE.Task]: 'Task',
  [ISSUE_TYPE.Bug]: 'Bug',
  [ISSUE_TYPE.Chore]: 'Chore',
  [ISSUE_TYPE.Feature]: 'Feature',
  [ISSUE_TYPE.Idea]: 'Idea',
  [ISSUE_TYPE.Verify]: 'Verify',
}

/**
 * Issue type options for select dropdowns
 */
export const ISSUE_TYPE_OPTIONS = [
  { value: String(ISSUE_TYPE.Task), label: 'Task' },
  { value: String(ISSUE_TYPE.Bug), label: 'Bug' },
  { value: String(ISSUE_TYPE.Chore), label: 'Chore' },
  { value: String(ISSUE_TYPE.Feature), label: 'Feature' },
  { value: String(ISSUE_TYPE.Verify), label: 'Verify' },
]

/**
 * Helper function to get status label with fallback
 */
export function getStatusLabel(status: number | undefined | null): string {
  if (status === undefined || status === null) return 'Unknown'
  return ISSUE_STATUS_LABELS[status] ?? 'Unknown'
}

/**
 * Helper function to get status color class with fallback
 */
export function getStatusColorClass(status: number | undefined | null): string {
  if (status === undefined || status === null) return ISSUE_STATUS_COLORS[ISSUE_STATUS.Open]
  return ISSUE_STATUS_COLORS[status] ?? ISSUE_STATUS_COLORS[ISSUE_STATUS.Open]
}

/**
 * Helper function to get type label with fallback
 */
export function getTypeLabel(type: number | undefined | null): string {
  if (type === undefined || type === null) return 'Task'
  return ISSUE_TYPE_LABELS[type] ?? 'Task'
}
