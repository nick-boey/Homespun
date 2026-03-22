import { useEffect } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useClaudeCodeHub } from '@/providers/signalr-provider'
import { registerClaudeCodeHubEvents } from '@/lib/signalr/claude-code-hub'
import {
  invalidateAllSessionsQueries,
  invalidateTaskGraphQueries,
} from '@/features/sessions/hooks/use-sessions'

/**
 * Hook that subscribes to SignalR session events globally and invalidates session queries.
 * This provides real-time updates for the header status indicator regardless of which page is active.
 *
 * Unlike useSessionsSignalR (used in SessionsList), this hook is meant to be mounted at the
 * application root level to ensure session status changes are reflected in the global header
 * indicator immediately.
 *
 * Subscribe to the following events:
 * - SessionStarted: When a new session is created
 * - SessionStopped: When a session is stopped
 * - SessionStatusChanged: When session status changes (e.g., running -> waiting)
 * - SessionError: When a session encounters an error
 * - SessionResultReceived: When session results are received
 * - SessionModeModelChanged: When session mode or model changes
 */
export function useGlobalSessionsSignalR(): void {
  const queryClient = useQueryClient()
  const { connection, isConnected } = useClaudeCodeHub()

  useEffect(() => {
    if (!connection || !isConnected) return

    const invalidate = () => {
      invalidateAllSessionsQueries(queryClient)
    }

    // Session lifecycle events should also invalidate task graphs
    // since task graph nodes show agent status rings
    const invalidateSessionAndTaskGraphs = () => {
      invalidateAllSessionsQueries(queryClient)
      invalidateTaskGraphQueries(queryClient)
    }

    const cleanup = registerClaudeCodeHubEvents(connection, {
      onSessionStarted: invalidateSessionAndTaskGraphs,
      onSessionStopped: invalidateSessionAndTaskGraphs,
      onSessionStatusChanged: invalidateSessionAndTaskGraphs,
      onSessionError: invalidate,
      onSessionResultReceived: invalidate,
      onSessionModeModelChanged: invalidate,
    })

    return cleanup
  }, [connection, isConnected, queryClient])
}
