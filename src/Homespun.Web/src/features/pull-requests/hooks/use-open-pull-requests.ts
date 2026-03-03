import { useQuery } from '@tanstack/react-query'
import { PullRequests, type PullRequestWithStatus } from '@/api'

export const openPullRequestsQueryKey = (projectId: string) =>
  ['open-pull-requests', projectId] as const

export interface UseOpenPullRequestsResult {
  pullRequests: PullRequestWithStatus[] | undefined
  isLoading: boolean
  isSuccess: boolean
  isError: boolean
  error: Error | null
  refetch: () => void
}

/**
 * Hook for fetching open pull requests for a project.
 *
 * @param projectId - The project ID to fetch open PRs for
 * @returns Open pull requests data and query state
 */
export function useOpenPullRequests(projectId: string): UseOpenPullRequestsResult {
  const query = useQuery({
    queryKey: openPullRequestsQueryKey(projectId),
    queryFn: async () => {
      const response = await PullRequests.getApiProjectsByProjectIdPullRequestsOpen({
        path: { projectId },
      })

      if (response.error || !response.data) {
        throw new Error(response.error?.detail ?? 'Failed to fetch open pull requests')
      }

      return response.data
    },
    enabled: !!projectId,
  })

  return {
    pullRequests: query.data,
    isLoading: query.isLoading,
    isSuccess: query.isSuccess,
    isError: query.isError,
    error: query.error,
    refetch: query.refetch,
  }
}
