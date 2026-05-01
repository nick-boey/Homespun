/**
 * Hook for fetching the per-issue linked-PR map for a project.
 *
 * Server endpoint: `GET /api/projects/{projectId}/linked-prs` →
 * `Dictionary<issueId, LinkedPr>`. PRs without a `FleeceIssueId` or
 * without a GitHub PR number are excluded.
 *
 * Lifecycle:
 *   - Initial fetch on mount.
 *   - Invalidated on the existing PR-state SignalR channel
 *     (`onIssuesChanged` with `Updated` kind for the issue carrying the
 *     PR) and on hub reconnect.
 *
 * Note: this hook also subscribes to `IssueChanged` so PR-link mutations
 * driven by issue updates flow through the same channel as the issue
 * cache. There is no dedicated PR-state channel today — the PR sync
 * service emits its updates via the issue mutation pipeline.
 */

import { useEffect } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { PullRequests, type LinkedPr } from '@/api'
import { useSignalR } from '@/hooks/use-signalr'
import { registerNotificationHubEvents } from '@/lib/signalr/notification-hub'

export const linkedPrsQueryKey = (projectId: string) => ['linked-prs', projectId] as const

export interface UseLinkedPrsResult {
  linkedPrs: Record<string, LinkedPr> | undefined
  isLoading: boolean
  isError: boolean
  error: Error | null
  refetch: () => void
}

export function useLinkedPrs(projectId: string): UseLinkedPrsResult {
  const queryClient = useQueryClient()
  const queryKey = linkedPrsQueryKey(projectId)

  const query = useQuery({
    queryKey,
    queryFn: async () => {
      const response = await PullRequests.getApiProjectsByProjectIdLinkedPrs({
        path: { projectId },
      })
      if (response.error || !response.data) {
        throw new Error(response.error?.detail ?? 'Failed to fetch linked PRs')
      }
      return response.data as Record<string, LinkedPr>
    },
    enabled: !!projectId,
    staleTime: Number.POSITIVE_INFINITY,
  })

  const { connection } = useSignalR({
    hubUrl: '/hubs/notifications',
    autoConnect: true,
    onReconnected: () => {
      queryClient.invalidateQueries({ queryKey })
    },
  })

  useEffect(() => {
    if (!connection) return
    return registerNotificationHubEvents(connection, {
      onIssueChanged: (changedProjectId) => {
        if (changedProjectId !== projectId) return
        queryClient.invalidateQueries({ queryKey })
      },
    })
  }, [connection, projectId, queryClient, queryKey])

  return {
    linkedPrs: query.data,
    isLoading: query.isLoading,
    isError: query.isError,
    error: query.error,
    refetch: query.refetch,
  }
}
