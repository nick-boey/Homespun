import { useMutation, useQueryClient } from '@tanstack/react-query'
import { IssuesAgent, type AcceptIssuesAgentChangesResponse } from '@/api'
import { sessionsQueryKey } from '@/features/sessions'

export function useAcceptIssues() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (sessionId: string): Promise<AcceptIssuesAgentChangesResponse | null> => {
      const response = await IssuesAgent.postApiIssuesAgentBySessionIdAccept({
        path: { sessionId },
      })

      if (response.error) {
        throw new Error(response.error?.detail ?? 'Failed to accept issues')
      }

      return response.data ?? null
    },
    onSuccess: () => {
      // Invalidate sessions query as the session is stopped
      queryClient.invalidateQueries({ queryKey: sessionsQueryKey })
    },
  })
}
