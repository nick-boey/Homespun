import { useMutation, useQueryClient } from '@tanstack/react-query'
import { IssuesAgent, type IssueDiffResponse } from '@/api'
import { issuesDiffQueryKey } from './use-issues-diff'

export function useRefreshIssuesDiff() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: async (sessionId: string): Promise<IssueDiffResponse | null> => {
      const response = await IssuesAgent.postApiIssuesAgentBySessionIdRefreshDiff({
        path: { sessionId },
      })

      if (response.error) {
        throw new Error(response.error?.detail ?? 'Failed to refresh issues diff')
      }

      return response.data ?? null
    },
    onSuccess: (data, sessionId) => {
      // Update the issues diff query cache with the fresh data
      if (data) {
        queryClient.setQueryData(issuesDiffQueryKey(sessionId), data)
      }
    },
  })
}
