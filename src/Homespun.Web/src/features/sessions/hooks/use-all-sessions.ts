import { useMemo } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Sessions } from '@/api'
import type { SessionSummary } from '@/api/generated/types.gen'
import { groupSessionsByProject } from '../utils/group-sessions-by-project'
import { allSessionsQueryKey } from './use-sessions'

export { allSessionsQueryKey }

/**
 * Fetches every session across every project as a single list.
 *
 * The query key is registered under the namespace invalidated by
 * `invalidateAllSessionsQueries`, so SignalR session lifecycle events keep
 * the sidebar live without manual refresh.
 *
 * Consumers typically pair this with `groupSessionsByProject` (re-exported
 * here as a memoized helper) to render per-project session children.
 */
export function useAllSessions() {
  return useQuery({
    queryKey: allSessionsQueryKey,
    queryFn: async (): Promise<SessionSummary[]> => {
      const response = await Sessions.getApiSessions()
      return response.data as SessionSummary[]
    },
    refetchInterval: 5000,
  })
}

/**
 * Memoized selector wrapping `groupSessionsByProject`.
 *
 * Returns a `Map<projectId, SessionSummary[]>` with `STOPPED` sessions
 * filtered out and each group sorted by `createdAt` ascending.
 */
export function useGroupedSessionsByProject(
  sessions: SessionSummary[] | undefined,
  knownProjectIds?: ReadonlySet<string>
): Map<string, SessionSummary[]> {
  return useMemo(
    () => groupSessionsByProject(sessions, knownProjectIds),
    [sessions, knownProjectIds]
  )
}
