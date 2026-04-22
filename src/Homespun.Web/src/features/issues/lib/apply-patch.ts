import type { TaskGraphResponse } from '@/api'
import type { IssueFieldPatch } from '@/types/signalr'

/**
 * Applies a structure-preserving field patch to the matching node in a
 * `TaskGraphResponse`. Returns a new response object with an immutable
 * update to the node whose `issue.id` matches `issueId`. Non-null patch
 * fields overlay the existing values; null / undefined fields on the patch
 * are ignored.
 *
 * No-ops (returns the input reference unchanged) when:
 * - `response` is undefined
 * - the target issue is not present in `response.nodes`
 *
 * Never mutates the input response, nodes array, or node objects.
 */
export function applyPatch(
  response: TaskGraphResponse | undefined,
  issueId: string,
  patch: IssueFieldPatch
): TaskGraphResponse | undefined {
  if (!response) return response
  if (!response.nodes || response.nodes.length === 0) return response

  let matched = false
  const nextNodes = response.nodes.map((node) => {
    if (!node.issue || node.issue.id !== issueId) return node
    matched = true
    return {
      ...node,
      issue: mergeIssue(node.issue, patch),
    }
  })

  if (!matched) return response

  return {
    ...response,
    nodes: nextNodes,
  }
}

function mergeIssue<T extends Record<string, unknown>>(issue: T, patch: IssueFieldPatch): T {
  const merged: Record<string, unknown> = { ...issue }
  for (const [key, value] of Object.entries(patch)) {
    if (value !== undefined && value !== null) {
      merged[key] = value
    }
  }
  return merged as T
}
