import { useQuery } from '@tanstack/react-query'
import { IssuePrStatus } from '@/api'

/**
 * Hook to fetch PR status for an issue
 * @param projectId The project ID
 * @param issueId The issue ID
 * @returns PR status data, loading state, and error
 */
export function useIssuePrStatus(projectId: string, issueId: string) {
  const {
    data: prStatus,
    isLoading,
    error
  } = useQuery({
    queryKey: ['issue-pr-status', projectId, issueId],
    queryFn: async () => {
      if (!projectId || !issueId) {
        return undefined
      }

      const response = await IssuePrStatus.getApiIssuePrStatusByProjectIdByIssueId({
        projectId,
        issueId
      })

      return response.data
    },
    enabled: !!projectId && !!issueId,
    staleTime: 30 * 1000, // 30 seconds
    gcTime: 5 * 60 * 1000 // 5 minutes
  })

  return {
    prStatus,
    isLoading,
    error
  }
}