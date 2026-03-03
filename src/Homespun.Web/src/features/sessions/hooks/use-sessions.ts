import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { Sessions } from '@/api'

export const sessionsQueryKey = ['sessions'] as const

export function useSessions() {
  return useQuery({
    queryKey: sessionsQueryKey,
    queryFn: async () => {
      const response = await Sessions.getApiSessions()
      return response.data
    },
  })
}

export function useStopSession() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (sessionId: string) => {
      await Sessions.deleteApiSessionsById({ path: { id: sessionId } })
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: sessionsQueryKey })
    },
  })
}
