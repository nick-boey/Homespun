/**
 * Hook for fetching the per-issue OpenSpec change state map for a project.
 *
 * Server endpoint: `GET /api/projects/{projectId}/openspec-states?issues=…` →
 * `Dictionary<issueId, IssueOpenSpecState>`. The frontend supplies the
 * visible-set ids it just fetched so the per-clone scan cost is bounded.
 *
 * No dedicated OpenSpec SignalR channel exists today — OpenSpec artifact
 * changes coincide with issue mutations (an agent commits artifacts as part
 * of an issue's session). The hook therefore invalidates on `IssueChanged`.
 * Promote to a dedicated channel if the bundled cadence becomes a problem.
 */

import { useEffect } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { OpenSpecDecorations, type IssueOpenSpecState } from '@/api'
import { useSignalR } from '@/hooks/use-signalr'
import { registerNotificationHubEvents } from '@/lib/signalr/notification-hub'

export const openSpecStatesQueryKey = (
  projectId: string,
  issueIds: readonly string[] | undefined
) => ['openspec-states', projectId, issueIds ? [...issueIds].sort().join(',') : ''] as const

export interface UseOpenSpecStatesResult {
  openSpecStates: Record<string, IssueOpenSpecState> | undefined
  isLoading: boolean
  isError: boolean
  error: Error | null
  refetch: () => void
}

export function useOpenSpecStates(
  projectId: string,
  issueIds: readonly string[] | undefined
): UseOpenSpecStatesResult {
  const queryClient = useQueryClient()
  const queryKey = openSpecStatesQueryKey(projectId, issueIds)

  const query = useQuery({
    queryKey,
    queryFn: async () => {
      const response = await OpenSpecDecorations.getApiProjectsByProjectIdOpenspecStates({
        path: { projectId },
        query: issueIds && issueIds.length > 0 ? { issues: issueIds.join(',') } : undefined,
      })
      if (response.error || !response.data) {
        throw new Error(response.error?.detail ?? 'Failed to fetch OpenSpec states')
      }
      return response.data as Record<string, IssueOpenSpecState>
    },
    enabled: !!projectId,
    staleTime: Number.POSITIVE_INFINITY,
  })

  const { connection } = useSignalR({
    hubUrl: '/hubs/notifications',
    autoConnect: true,
    onReconnected: () => {
      queryClient.invalidateQueries({ queryKey: ['openspec-states', projectId] })
    },
  })

  useEffect(() => {
    if (!connection) return
    return registerNotificationHubEvents(connection, {
      onIssueChanged: (changedProjectId) => {
        if (changedProjectId !== projectId) return
        queryClient.invalidateQueries({ queryKey: ['openspec-states', projectId] })
      },
    })
  }, [connection, projectId, queryClient])

  return {
    openSpecStates: query.data,
    isLoading: query.isLoading,
    isError: query.isError,
    error: query.error,
    refetch: query.refetch,
  }
}
