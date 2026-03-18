import { useQuery } from '@tanstack/react-query'
import { IssuesAgent, type IssueDiffResponse } from '@/api'

export const issuesDiffQueryKey = (sessionId: string) => ['issues-diff', sessionId] as const

export function useIssuesDiff(sessionId: string | undefined) {
  return useQuery({
    queryKey: issuesDiffQueryKey(sessionId ?? ''),
    queryFn: async (): Promise<IssueDiffResponse | null> => {
      if (!sessionId) return null

      const response = await IssuesAgent.getApiIssuesAgentBySessionIdDiff({
        path: { sessionId },
      })

      if (response.error) {
        throw new Error(response.error?.detail ?? 'Failed to get issues diff')
      }

      return response.data ?? null
    },
    enabled: !!sessionId,
  })
}
