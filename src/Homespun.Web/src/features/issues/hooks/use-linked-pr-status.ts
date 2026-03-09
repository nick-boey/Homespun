import { useQuery } from '@tanstack/react-query'
import { IssuePrStatus } from '@/api'

export function useLinkedPrStatus(projectId: string, issueId: string | undefined, enabled = true) {
  return useQuery({
    queryKey: ['issue-pr-status', projectId, issueId],
    queryFn: async () => {
      if (!issueId) return null

      const response = await IssuePrStatus.getApiIssuePrStatusByProjectIdByIssueId({
        path: { projectId, issueId },
      })

      return response.data
    },
    enabled: enabled && !!issueId,
    staleTime: 30000, // 30 seconds
    refetchInterval: 60000, // 1 minute
  })
}
