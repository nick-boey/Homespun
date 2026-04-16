import { useQuery } from '@tanstack/react-query'
import { Sessions, type ResumableSession } from '@/api'

export const sessionHistoryQueryKey = (entityId?: string) => ['session-history', entityId] as const

export interface UseSessionHistoryResult {
  data: ResumableSession[] | undefined
  isLoading: boolean
  error: Error | null
}

/**
 * Fetches resumable session history for a specific entity.
 * Returns past sessions ordered by last activity.
 */
export function useSessionHistory(entityId: string | undefined | null): UseSessionHistoryResult {
  const query = useQuery({
    queryKey: sessionHistoryQueryKey(entityId ?? undefined),
    queryFn: async () => {
      const response = await Sessions.getApiSessionsEntityByEntityIdResumable({
        path: { entityId: entityId! },
      })
      return response.data
    },
    enabled: !!entityId,
  })

  return {
    data: query.data ?? undefined,
    isLoading: query.isLoading,
    error: query.error,
  }
}
