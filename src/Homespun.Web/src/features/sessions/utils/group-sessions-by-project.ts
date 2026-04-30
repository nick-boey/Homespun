import { ClaudeSessionStatus } from '@/api'
import type { SessionSummary } from '@/api/generated/types.gen'

/**
 * Filter `STOPPED` sessions out, then group the remaining sessions by
 * `projectId`, sorting each group by `createdAt` ascending (oldest first).
 *
 * If `knownProjectIds` is provided, sessions whose `projectId` is not in
 * that set are dropped — this mirrors the sidebar requirement that
 * sessions belonging to an unknown project SHALL NOT render.
 */
export function groupSessionsByProject(
  sessions: readonly SessionSummary[] | undefined,
  knownProjectIds?: ReadonlySet<string>
): Map<string, SessionSummary[]> {
  const groups = new Map<string, SessionSummary[]>()
  if (!sessions) return groups

  for (const session of sessions) {
    if (session.status === ClaudeSessionStatus.STOPPED) continue
    const projectId = session.projectId
    if (!projectId) continue
    if (knownProjectIds && !knownProjectIds.has(projectId)) continue

    const existing = groups.get(projectId)
    if (existing) {
      existing.push(session)
    } else {
      groups.set(projectId, [session])
    }
  }

  for (const list of groups.values()) {
    list.sort((a, b) => {
      const aTime = a.createdAt ?? ''
      const bTime = b.createdAt ?? ''
      if (aTime < bTime) return -1
      if (aTime > bTime) return 1
      return 0
    })
  }

  return groups
}
