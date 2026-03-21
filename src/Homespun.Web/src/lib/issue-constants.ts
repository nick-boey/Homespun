import { IssueStatus, IssueType } from '@/api/generated'

/**
 * Issue status and type constants.
 *
 * These values are now serialized as camelCase strings matching the C# enum definitions
 * in Fleece.Core.Models. The API types are imported from the generated OpenAPI client.
 */

/**
 * Issue status enum values matching Fleece.Core.Models.IssueStatus
 * Using the generated API types for type safety
 */
export const ISSUE_STATUS = IssueStatus

export type IssueStatusValue = IssueStatus

/**
 * Issue status display labels
 */
export const ISSUE_STATUS_LABELS: Record<IssueStatus, string> = {
  [IssueStatus.DRAFT]: 'Draft',
  [IssueStatus.OPEN]: 'Open',
  [IssueStatus.PROGRESS]: 'In Progress',
  [IssueStatus.REVIEW]: 'Review',
  [IssueStatus.COMPLETE]: 'Complete',
  [IssueStatus.ARCHIVED]: 'Archived',
  [IssueStatus.CLOSED]: 'Closed',
  [IssueStatus.DELETED]: 'Deleted',
}

/**
 * Compact issue status display labels for space-constrained buttons
 */
export const ISSUE_STATUS_COMPACT_LABELS: Record<IssueStatus, string> = {
  [IssueStatus.DRAFT]: 'Draft',
  [IssueStatus.OPEN]: 'Open',
  [IssueStatus.PROGRESS]: 'Progress',
  [IssueStatus.REVIEW]: 'Review',
  [IssueStatus.COMPLETE]: 'Complete',
  [IssueStatus.ARCHIVED]: 'Archived',
  [IssueStatus.CLOSED]: 'Closed',
  [IssueStatus.DELETED]: 'Deleted',
}

/**
 * Issue status color classes for badges
 */
export const ISSUE_STATUS_COLORS: Record<IssueStatus, string> = {
  [IssueStatus.DRAFT]: 'bg-gray-500/20 text-gray-700 dark:text-gray-400',
  [IssueStatus.OPEN]: 'bg-blue-500/20 text-blue-700 dark:text-blue-400',
  [IssueStatus.PROGRESS]: 'bg-yellow-500/20 text-yellow-700 dark:text-yellow-400',
  [IssueStatus.REVIEW]: 'bg-purple-500/20 text-purple-700 dark:text-purple-400',
  [IssueStatus.COMPLETE]: 'bg-green-500/20 text-green-700 dark:text-green-400',
  [IssueStatus.ARCHIVED]: 'bg-gray-500/20 text-gray-700 dark:text-gray-400',
  [IssueStatus.CLOSED]: 'bg-gray-500/20 text-gray-700 dark:text-gray-400',
  [IssueStatus.DELETED]: 'bg-red-500/20 text-red-700 dark:text-red-400',
}

/**
 * Issue status options for select dropdowns.
 * Ordered by typical workflow progression.
 */
export const ISSUE_STATUS_OPTIONS = [
  { value: IssueStatus.DRAFT, label: 'Draft' },
  { value: IssueStatus.OPEN, label: 'Open' },
  { value: IssueStatus.PROGRESS, label: 'In Progress' },
  { value: IssueStatus.REVIEW, label: 'Review' },
  { value: IssueStatus.COMPLETE, label: 'Complete' },
  { value: IssueStatus.CLOSED, label: 'Closed' },
  { value: IssueStatus.ARCHIVED, label: 'Archived' },
]

/**
 * Issue type enum values matching Fleece.Core.Models.IssueType
 * Using the generated API types for type safety
 */
export const ISSUE_TYPE = IssueType

export type IssueTypeValue = IssueType

/**
 * Issue type display labels
 */
export const ISSUE_TYPE_LABELS: Record<IssueType, string> = {
  [IssueType.TASK]: 'Task',
  [IssueType.BUG]: 'Bug',
  [IssueType.CHORE]: 'Chore',
  [IssueType.FEATURE]: 'Feature',
  [IssueType.IDEA]: 'Idea',
  [IssueType.VERIFY]: 'Verify',
}

/**
 * Issue type options for select dropdowns
 */
export const ISSUE_TYPE_OPTIONS = [
  { value: IssueType.TASK, label: 'Task' },
  { value: IssueType.BUG, label: 'Bug' },
  { value: IssueType.CHORE, label: 'Chore' },
  { value: IssueType.FEATURE, label: 'Feature' },
  { value: IssueType.VERIFY, label: 'Verify' },
]

/**
 * Helper function to get status label with fallback
 */
export function getStatusLabel(status: IssueStatus | undefined | null): string {
  if (status === undefined || status === null) return 'Unknown'
  return ISSUE_STATUS_LABELS[status] ?? 'Unknown'
}

/**
 * Helper function to get status color class with fallback
 */
export function getStatusColorClass(status: IssueStatus | undefined | null): string {
  if (status === undefined || status === null) return ISSUE_STATUS_COLORS[IssueStatus.DRAFT]
  return ISSUE_STATUS_COLORS[status] ?? ISSUE_STATUS_COLORS[IssueStatus.DRAFT]
}

/**
 * Helper function to get type label with fallback
 */
export function getTypeLabel(type: IssueType | undefined | null): string {
  if (type === undefined || type === null) return 'Task'
  return ISSUE_TYPE_LABELS[type] ?? 'Task'
}
