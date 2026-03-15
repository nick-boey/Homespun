/**
 * Generates a branch name for an issue.
 * Matches BranchNameGenerator.GenerateBranchName from C#.
 *
 * Format: {type}/{branchId}+{issueId}
 * Example: task/my-feature+abc123
 */

import { IssueType } from '@/api'
import type { IssueResponse } from '@/api'

const ISSUE_TYPE_PREFIXES: Record<IssueType, string> = {
  [IssueType.TASK]: 'task',
  [IssueType.FEATURE]: 'feature',
  [IssueType.BUG]: 'bug',
  [IssueType.CHORE]: 'chore',
  [IssueType.IDEA]: 'idea',
  [IssueType.VERIFY]: 'verify',
}

/**
 * Generate a branch name for an issue.
 *
 * @param issue - The issue to generate a branch name for
 * @returns Branch name in format: {type}/{branchId}+{issueId}
 */
export function generateBranchName(issue: IssueResponse | null | undefined): string | null {
  if (!issue?.id) return null

  const typePrefix = ISSUE_TYPE_PREFIXES[issue.type ?? IssueType.TASK] ?? 'task'
  const branchId = issue.workingBranchId || slugify(issue.title ?? '')

  if (!branchId) {
    return `${typePrefix}/${issue.id}`
  }

  return `${typePrefix}/${branchId}+${issue.id}`
}

/**
 * Convert a string to a URL-friendly slug.
 */
function slugify(text: string): string {
  return text
    .toLowerCase()
    .trim()
    .replace(/[^\w\s-]/g, '') // Remove non-word chars
    .replace(/[\s_-]+/g, '-') // Replace spaces/underscores with hyphens
    .replace(/^-+|-+$/g, '') // Trim leading/trailing hyphens
    .substring(0, 50) // Limit length
}
