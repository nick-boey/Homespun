import { useQuery } from '@tanstack/react-query'
import { Sessions, type SessionCacheSummary } from '@/api'

export const sessionHistoryQueryKey = (projectId?: string, entityId?: string) =>
  ['session-history', projectId, entityId] as const

export interface UseSessionHistoryResult {
  data: SessionCacheSummary[] | undefined
  isLoading: boolean
  error: Error | null
}

/**
 * Fetches session history for a specific entity (issue or PR) within a project.
 * Returns all past sessions ordered by creation date.
 */
export function useSessionHistory(
  projectId: string | undefined | null,
  entityId: string | undefined | null
): UseSessionHistoryResult {
  const query = useQuery({
    queryKey: sessionHistoryQueryKey(projectId ?? undefined, entityId ?? undefined),
    queryFn: async () => {
      const response = await Sessions.getApiSessionsHistoryByProjectIdByEntityId({
        path: { projectId: projectId!, entityId: entityId! },
      })
      return response.data
    },
    enabled: !!projectId && !!entityId,
  })

  return {
    data: query.data ?? undefined,
    isLoading: query.isLoading,
    error: query.error,
  }
}
