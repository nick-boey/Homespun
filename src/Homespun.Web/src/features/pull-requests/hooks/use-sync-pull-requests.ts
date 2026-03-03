import { useMutation, useQueryClient } from '@tanstack/react-query'
import { PullRequests, type SyncResult } from '@/api'
import { openPullRequestsQueryKey } from './use-open-pull-requests'
import { mergedPullRequestsQueryKey } from './use-merged-pull-requests'

export interface UseSyncPullRequestsResult {
  syncPullRequests: (projectId: string) => Promise<SyncResult>
  isPending: boolean
  isSuccess: boolean
  isError: boolean
  error: Error | null
}

/**
 * Hook for syncing pull requests from GitHub.
 * Invalidates the open and merged PR queries after successful sync.
 *
 * @returns Mutation function and state
 */
export function useSyncPullRequests(): UseSyncPullRequestsResult {
  const queryClient = useQueryClient()

  const mutation = useMutation({
    mutationFn: async (projectId: string) => {
      const response = await PullRequests.postApiProjectsByProjectIdSync({
        path: { projectId },
      })

      if (response.error || !response.data) {
        throw new Error(response.error?.detail ?? 'Failed to sync pull requests')
      }

      return response.data
    },
    onSuccess: (_data, projectId) => {
      // Invalidate PR queries to refresh the data
      queryClient.invalidateQueries({ queryKey: openPullRequestsQueryKey(projectId) })
      queryClient.invalidateQueries({ queryKey: mergedPullRequestsQueryKey(projectId) })
    },
  })

  return {
    syncPullRequests: mutation.mutateAsync,
    isPending: mutation.isPending,
    isSuccess: mutation.isSuccess,
    isError: mutation.isError,
    error: mutation.error,
  }
}
