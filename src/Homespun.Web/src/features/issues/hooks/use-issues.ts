/**
 * Hook for fetching the visible-set list of issues for a project.
 *
 * Server filter: open + ancestors-of-open + explicit `include` ids
 * + open-PR-linked when `includeOpenPrLinked=true`. See design.md D1.
 *
 * Lifecycle:
 *   - Initial fetch on mount (TanStack Query)
 *   - Live updates via the unified `IssueChanged` SignalR event with
 *     idempotent merge (`applyIssueChanged`) — no refetch on event.
 *   - On `onreconnected`, the query is invalidated to force a refetch
 *     so any events missed during the disconnected window are recovered.
 */

import { useEffect } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { Issues, type IssueResponse } from '@/api'
import { useSignalR } from '@/hooks/use-signalr'
import { registerNotificationHubEvents } from '@/lib/signalr/notification-hub'
import { applyIssueChanged } from '../lib/apply-issue-changed'

export interface UseIssuesOptions {
  /** Closed issue ids to forcibly include (and pull their ancestors in). */
  include?: readonly string[]
  /** Pull in issues attached to open PRs even when otherwise filtered out. */
  includeOpenPrLinked?: boolean
  /** Bypass the visible-set filter and return the raw list. */
  includeAll?: boolean
}

export const issuesQueryKey = (projectId: string, options: UseIssuesOptions = {}) =>
  [
    'issues',
    projectId,
    options.include ? [...options.include].sort().join(',') : '',
    !!options.includeOpenPrLinked,
    !!options.includeAll,
  ] as const

export interface UseIssuesResult {
  issues: IssueResponse[] | undefined
  isLoading: boolean
  isSuccess: boolean
  isError: boolean
  error: Error | null
  refetch: () => void
}

export function useIssues(projectId: string, options: UseIssuesOptions = {}): UseIssuesResult {
  const queryClient = useQueryClient()
  const queryKey = issuesQueryKey(projectId, options)

  const query = useQuery({
    queryKey,
    queryFn: async () => {
      const include = options.include?.length ? options.include.join(',') : undefined
      const response = await Issues.getApiProjectsByProjectIdIssues({
        path: { projectId },
        query: {
          include,
          includeOpenPrLinked: options.includeOpenPrLinked ?? undefined,
          includeAll: options.includeAll ?? undefined,
        },
      })
      if (response.error || !response.data) {
        throw new Error(response.error?.detail ?? 'Failed to fetch issues')
      }
      return response.data
    },
    enabled: !!projectId,
    staleTime: Number.POSITIVE_INFINITY,
  })

  // Live-merge issue mutations. Idempotent — local POST response and SignalR
  // echo can both apply without coordination.
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
      onIssueChanged: (changedProjectId, kind, issueId, issue) => {
        if (changedProjectId !== projectId) return
        if (!issueId) {
          // Bulk event — invalidate the issues query so the next fetch
          // refreshes the visible-set.
          queryClient.invalidateQueries({ queryKey })
          return
        }
        queryClient.setQueryData<IssueResponse[]>(queryKey, (old) =>
          applyIssueChanged(old, { kind, issueId, issue })
        )
      },
    })
  }, [connection, projectId, queryClient, queryKey])

  return {
    issues: query.data,
    isLoading: query.isLoading,
    isSuccess: query.isSuccess,
    isError: query.isError,
    error: query.error,
    refetch: query.refetch,
  }
}
