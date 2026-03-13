import { useMutation, useQueryClient } from '@tanstack/react-query'
import { PullRequests, type FullRefreshResult } from '@/api'
import { openPullRequestsQueryKey } from './use-open-pull-requests'
import { mergedPullRequestsQueryKey } from './use-merged-pull-requests'
import { taskGraphQueryKey } from '@/features/issues/hooks/use-task-graph'

export interface UseFullRefreshResult {
  fullRefresh: (projectId: string) => Promise<FullRefreshResult>
  isPending: boolean
  isSuccess: boolean
  isError: boolean
  error: Error | null
}

/**
 * Hook for performing a full refresh of pull requests from GitHub.
 * Downloads all open, closed, and merged PRs and updates the cache.
 * Invalidates the open PR, merged PR, and task graph queries after successful refresh.
 *
 * @returns Mutation function and state
 */
export function useFullRefresh(): UseFullRefreshResult {
  const queryClient = useQueryClient()

  const mutation = useMutation({
    mutationFn: async (projectId: string) => {
      const response = await PullRequests.postApiProjectsByProjectIdFullRefresh({
        path: { projectId },
      })

      if (response.error || !response.data) {
        throw new Error(response.error?.detail ?? 'Failed to refresh pull requests')
      }

      return response.data
    },
    onSuccess: (_data, projectId) => {
      // Invalidate PR and task graph queries to refresh the data
      queryClient.invalidateQueries({ queryKey: openPullRequestsQueryKey(projectId) })
      queryClient.invalidateQueries({ queryKey: mergedPullRequestsQueryKey(projectId) })
      queryClient.invalidateQueries({ queryKey: taskGraphQueryKey(projectId) })
    },
  })

  return {
    fullRefresh: mutation.mutateAsync,
    isPending: mutation.isPending,
    isSuccess: mutation.isSuccess,
    isError: mutation.isError,
    error: mutation.error,
  }
}
