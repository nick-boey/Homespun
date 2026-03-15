import { useQuery, useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { Sessions } from '@/api'

export const sessionsQueryKey = ['sessions'] as const
export const allSessionsCountQueryKey = ['all-sessions-count'] as const

/**
 * Invalidates all session-related queries.
 * This includes the main sessions query, the all-sessions-count query used by the header indicator,
 * and all project-specific session queries.
 */
export async function invalidateAllSessionsQueries(queryClient: QueryClient): Promise<void> {
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: sessionsQueryKey }),
    queryClient.invalidateQueries({ queryKey: allSessionsCountQueryKey }),
    queryClient.invalidateQueries({
      predicate: (query) =>
        Array.isArray(query.queryKey) && query.queryKey[0] === 'project-sessions',
    }),
  ])
}

export function useSessions() {
  return useQuery({
    queryKey: sessionsQueryKey,
    queryFn: async () => {
      const response = await Sessions.getApiSessions()
      return response.data
    },
    refetchInterval: 5000, // Poll every 5 seconds for real-time updates
  })
}

export function useStopSession() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (sessionId: string) => {
      await Sessions.deleteApiSessionsById({ path: { id: sessionId } })
    },
    onSuccess: () => {
      invalidateAllSessionsQueries(queryClient)
    },
  })
}
