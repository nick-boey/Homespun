import { useMutation, useQueryClient } from '@tanstack/react-query'
import { FleeceIssueSync, type FleecePullResult, type FleeceIssueSyncResult } from '@/api'
import { taskGraphQueryKey } from '@/features/issues/hooks/use-task-graph'
import { openPullRequestsQueryKey } from '@/features/pull-requests/hooks/use-open-pull-requests'
import { mergedPullRequestsQueryKey } from '@/features/pull-requests/hooks/use-merged-pull-requests'

export interface UseFleecePullResult {
  pull: (projectId: string) => Promise<FleecePullResult>
  isPending: boolean
  isSuccess: boolean
  isError: boolean
  error: Error | null
}

/**
 * Hook for pulling fleece issues from remote.
 * Performs a fast-forward pull and merges fleece issues.
 *
 * @returns Mutation function and state
 */
export function useFleecePull(): UseFleecePullResult {
  const queryClient = useQueryClient()

  const mutation = useMutation({
    mutationFn: async (projectId: string) => {
      const response = await FleeceIssueSync.postApiFleeceSyncByProjectIdPull({
        path: { projectId },
      })

      if (response.error || !response.data) {
        throw new Error(response.error?.detail ?? 'Failed to pull fleece issues')
      }

      return response.data
    },
    onSuccess: (_data, projectId) => {
      // Invalidate task graph to refresh the data
      queryClient.invalidateQueries({ queryKey: taskGraphQueryKey(projectId) })
    },
  })

  return {
    pull: mutation.mutateAsync,
    isPending: mutation.isPending,
    isSuccess: mutation.isSuccess,
    isError: mutation.isError,
    error: mutation.error,
  }
}

export interface UseFleeceSyncResult {
  sync: (projectId: string) => Promise<FleeceIssueSyncResult>
  isPending: boolean
  isSuccess: boolean
  isError: boolean
  error: Error | null
}

/**
 * Hook for syncing fleece issues to remote.
 * Commits all .fleece/ files and pushes to the default branch.
 *
 * @returns Mutation function and state
 */
export function useFleeceSync(): UseFleeceSyncResult {
  const queryClient = useQueryClient()

  const mutation = useMutation({
    mutationFn: async (projectId: string) => {
      const response = await FleeceIssueSync.postApiFleeceSyncByProjectIdSync({
        path: { projectId },
      })

      if (response.error || !response.data) {
        throw new Error(response.error?.detail ?? 'Failed to sync fleece issues')
      }

      return response.data
    },
    onSuccess: (_data, projectId) => {
      // Invalidate task graph to refresh the data
      queryClient.invalidateQueries({ queryKey: taskGraphQueryKey(projectId) })
    },
  })

  return {
    sync: mutation.mutateAsync,
    isPending: mutation.isPending,
    isSuccess: mutation.isSuccess,
    isError: mutation.isError,
    error: mutation.error,
  }
}

export interface UsePullAndSyncResult {
  pullAll: (projectId: string) => Promise<{
    fleecePull: FleecePullResult
    prSync: { imported?: number; updated?: number; removed?: number }
  }>
  syncAll: (projectId: string) => Promise<{
    fleeceSync: FleeceIssueSyncResult
    prSync: { imported?: number; updated?: number; removed?: number }
  }>
  isPulling: boolean
  isSyncing: boolean
}

/**
 * Hook for combined pull and sync operations.
 * Pulls fleece issues and syncs PRs from GitHub in parallel.
 *
 * @returns Combined pull and sync functions with loading state
 */
export function usePullAndSync(): UsePullAndSyncResult {
  const queryClient = useQueryClient()

  const pullMutation = useMutation({
    mutationFn: async (projectId: string) => {
      const [fleeceResponse, prResponse] = await Promise.all([
        FleeceIssueSync.postApiFleeceSyncByProjectIdPull({
          path: { projectId },
        }),
        fetch(`/api/projects/${projectId}/sync`, { method: 'POST' }).then((r) => r.json()),
      ])

      if (fleeceResponse.error || !fleeceResponse.data) {
        throw new Error(fleeceResponse.error?.detail ?? 'Failed to pull fleece issues')
      }

      return {
        fleecePull: fleeceResponse.data,
        prSync: prResponse as { imported?: number; updated?: number; removed?: number },
      }
    },
    onSuccess: (_data, projectId) => {
      queryClient.invalidateQueries({ queryKey: taskGraphQueryKey(projectId) })
      queryClient.invalidateQueries({ queryKey: openPullRequestsQueryKey(projectId) })
      queryClient.invalidateQueries({ queryKey: mergedPullRequestsQueryKey(projectId) })
    },
  })

  const syncMutation = useMutation({
    mutationFn: async (projectId: string) => {
      const [fleeceResponse, prResponse] = await Promise.all([
        FleeceIssueSync.postApiFleeceSyncByProjectIdSync({
          path: { projectId },
        }),
        fetch(`/api/projects/${projectId}/sync`, { method: 'POST' }).then((r) => r.json()),
      ])

      if (fleeceResponse.error || !fleeceResponse.data) {
        throw new Error(fleeceResponse.error?.detail ?? 'Failed to sync fleece issues')
      }

      return {
        fleeceSync: fleeceResponse.data,
        prSync: prResponse as { imported?: number; updated?: number; removed?: number },
      }
    },
    onSuccess: (_data, projectId) => {
      queryClient.invalidateQueries({ queryKey: taskGraphQueryKey(projectId) })
      queryClient.invalidateQueries({ queryKey: openPullRequestsQueryKey(projectId) })
      queryClient.invalidateQueries({ queryKey: mergedPullRequestsQueryKey(projectId) })
    },
  })

  return {
    pullAll: pullMutation.mutateAsync,
    syncAll: syncMutation.mutateAsync,
    isPulling: pullMutation.isPending,
    isSyncing: syncMutation.isPending,
  }
}
