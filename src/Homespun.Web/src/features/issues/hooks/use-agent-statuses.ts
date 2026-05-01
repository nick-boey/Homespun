/**
 * Hook for fetching the per-issue agent-status map for a project.
 *
 * Server endpoint: `GET /api/projects/{projectId}/agent-statuses` →
 * `Dictionary<issueId, AgentStatusData>`. Sessions are grouped by their
 * `EntityId` and the most-recent-by-`LastActivityAt` wins.
 *
 * Lifecycle: the hook invalidates on every `IssueChanged` event for the
 * project and on hub reconnect. Agent status changes are driven by
 * session-lifecycle events that always coincide with issue mutations
 * (start = create child issue, finish = update issue body, etc.) — so the
 * shared channel is sufficient. If a future agent-only status SignalR
 * channel materialises, swap it in here without changing the consumer
 * surface.
 */

import { useEffect } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { AgentStatuses, type AgentStatusData } from '@/api'
import { useSignalR } from '@/hooks/use-signalr'
import { registerNotificationHubEvents } from '@/lib/signalr/notification-hub'

export const agentStatusesQueryKey = (projectId: string) => ['agent-statuses', projectId] as const

export interface UseAgentStatusesResult {
  agentStatuses: Record<string, AgentStatusData> | undefined
  isLoading: boolean
  isError: boolean
  error: Error | null
  refetch: () => void
}

export function useAgentStatuses(projectId: string): UseAgentStatusesResult {
  const queryClient = useQueryClient()
  const queryKey = agentStatusesQueryKey(projectId)

  const query = useQuery({
    queryKey,
    queryFn: async () => {
      const response = await AgentStatuses.getApiProjectsByProjectIdAgentStatuses({
        path: { projectId },
      })
      if (response.error || !response.data) {
        throw new Error(response.error?.detail ?? 'Failed to fetch agent statuses')
      }
      return response.data as Record<string, AgentStatusData>
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
    agentStatuses: query.data,
    isLoading: query.isLoading,
    isError: query.isError,
    error: query.error,
    refetch: query.refetch,
  }
}
