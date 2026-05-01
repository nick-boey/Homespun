/**
 * Idempotent client-side merge for the unified `IssueChanged` SignalR event.
 *
 * Per design.md D2: replace-by-id is idempotent — applying the same canonical
 * issue twice is a no-op, so no echo-suppression / request-id plumbing is
 * needed. The local POST response and the SignalR echo can both apply the
 * mutation without coordination.
 */

import type { IssueResponse } from '@/api'
import type { IssueChangeKind } from '@/types/signalr'

export interface IssueChangedEvent {
  kind: IssueChangeKind
  issueId: string
  issue: IssueResponse | null
}

/**
 * Apply an `IssueChanged` event to a list of cached issues. Returns a new
 * array (never mutates input).
 *
 * - `created` / `updated` → replace-by-id; insert when absent
 * - `deleted` → drop the matching id
 *
 * Order does not matter — apply twice and you get the same end state.
 */
export function applyIssueChanged(
  cache: readonly IssueResponse[] | undefined,
  event: IssueChangedEvent
): IssueResponse[] {
  const list = cache ?? []

  if (event.kind === 'deleted') {
    return list.filter((issue) => issue.id !== event.issueId)
  }

  if (!event.issue) {
    return [...list]
  }

  const idx = list.findIndex((issue) => issue.id === event.issueId)
  if (idx === -1) {
    return [...list, event.issue]
  }
  const next = list.slice()
  next[idx] = event.issue
  return next
}
