/**
 * Hook for fetching the project's merged-PR list (next-mode rendering).
 *
 * Server endpoint: `GET /api/projects/{projectId}/pull-requests/merged` →
 * `List<PullRequestWithTime>`. The endpoint returns the full list; the
 * `max` option clips client-side to keep the next-mode header concise.
 *
 * Lifecycle: invalidated on `IssueChanged` (PR sync events flow through
 * the same channel as the issue cache; there is no dedicated PR-state
 * SignalR channel today).
 */

import { useEffect } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { PullRequests, type PullRequestWithTime } from '@/api'
import { useSignalR } from '@/hooks/use-signalr'
import { registerNotificationHubEvents } from '@/lib/signalr/notification-hub'

export interface UseMergedPrsOptions {
  max?: number
}

export const mergedPrsQueryKey = (projectId: string, max: number | undefined) =>
  ['merged-prs', projectId, max ?? null] as const

export interface UseMergedPrsResult {
  mergedPrs: PullRequestWithTime[] | undefined
  isLoading: boolean
  isError: boolean
  error: Error | null
  refetch: () => void
}

export function useMergedPrs(
  projectId: string,
  options: UseMergedPrsOptions = {}
): UseMergedPrsResult {
  const queryClient = useQueryClient()
  const queryKey = mergedPrsQueryKey(projectId, options.max)

  const query = useQuery({
    queryKey,
    queryFn: async () => {
      const response = await PullRequests.getApiProjectsByProjectIdPullRequestsMerged({
        path: { projectId },
      })
      if (response.error || !response.data) {
        throw new Error(response.error?.detail ?? 'Failed to fetch merged PRs')
      }
      const list = response.data
      return options.max != null && options.max >= 0 ? list.slice(0, options.max) : list
    },
    enabled: !!projectId,
    staleTime: Number.POSITIVE_INFINITY,
  })

  const { connection } = useSignalR({
    hubUrl: '/hubs/notifications',
    autoConnect: true,
    onReconnected: () => {
      queryClient.invalidateQueries({ queryKey: ['merged-prs', projectId] })
    },
  })

  useEffect(() => {
    if (!connection) return
    return registerNotificationHubEvents(connection, {
      onIssueChanged: (changedProjectId) => {
        if (changedProjectId !== projectId) return
        queryClient.invalidateQueries({ queryKey: ['merged-prs', projectId] })
      },
    })
  }, [connection, projectId, queryClient])

  return {
    mergedPrs: query.data,
    isLoading: query.isLoading,
    isError: query.isError,
    error: query.error,
    refetch: query.refetch,
  }
}
