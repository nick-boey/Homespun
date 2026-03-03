import { useQuery } from '@tanstack/react-query'
import { PullRequests, type PullRequestWithTime } from '@/api'

export const mergedPullRequestsQueryKey = (projectId: string) =>
  ['merged-pull-requests', projectId] as const

export interface UseMergedPullRequestsResult {
  pullRequests: PullRequestWithTime[] | undefined
  isLoading: boolean
  isSuccess: boolean
  isError: boolean
  error: Error | null
  refetch: () => void
}

/**
 * Hook for fetching recently merged pull requests for a project.
 *
 * @param projectId - The project ID to fetch merged PRs for
 * @returns Merged pull requests data and query state
 */
export function useMergedPullRequests(projectId: string): UseMergedPullRequestsResult {
  const query = useQuery({
    queryKey: mergedPullRequestsQueryKey(projectId),
    queryFn: async () => {
      const response = await PullRequests.getApiProjectsByProjectIdPullRequestsMerged({
        path: { projectId },
      })

      if (response.error || !response.data) {
        throw new Error(response.error?.detail ?? 'Failed to fetch merged pull requests')
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
