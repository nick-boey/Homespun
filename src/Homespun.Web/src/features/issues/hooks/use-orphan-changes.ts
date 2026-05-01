/**
 * Hook for fetching the project-wide orphan-changes list.
 *
 * Server endpoint: `GET /api/projects/{projectId}/orphan-changes` →
 * `List<SnapshotOrphan>`. Deduped by change name across main + every
 * branch.
 *
 * Lifecycle: same shared SignalR channel as `useOpenSpecStates` — there is
 * no dedicated OpenSpec channel today.
 */

import { useEffect } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { OpenSpecDecorations, type SnapshotOrphan } from '@/api'
import { useSignalR } from '@/hooks/use-signalr'
import { registerNotificationHubEvents } from '@/lib/signalr/notification-hub'

export const orphanChangesQueryKey = (projectId: string) => ['orphan-changes', projectId] as const

export interface UseOrphanChangesResult {
  orphanChanges: SnapshotOrphan[] | undefined
  isLoading: boolean
  isError: boolean
  error: Error | null
  refetch: () => void
}

export function useOrphanChanges(projectId: string): UseOrphanChangesResult {
  const queryClient = useQueryClient()
  const queryKey = orphanChangesQueryKey(projectId)

  const query = useQuery({
    queryKey,
    queryFn: async () => {
      const response = await OpenSpecDecorations.getApiProjectsByProjectIdOrphanChanges({
        path: { projectId },
      })
      if (response.error || !response.data) {
        throw new Error(response.error?.detail ?? 'Failed to fetch orphan changes')
      }
      return response.data
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
    orphanChanges: query.data,
    isLoading: query.isLoading,
    isError: query.isError,
    error: query.error,
    refetch: query.refetch,
  }
}
