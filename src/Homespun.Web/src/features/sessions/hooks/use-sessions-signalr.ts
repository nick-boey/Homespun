import { useEffect } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useClaudeCodeHub } from '@/providers/signalr-provider'
import { registerClaudeCodeHubEvents } from '@/lib/signalr/claude-code-hub'
import { invalidateAllSessionsQueries, invalidateTaskGraphQueries } from './use-sessions'
import { sessionHistoryQueryKey } from './use-session-history'

/**
 * Hook that subscribes to SignalR session events and invalidates all session queries.
 * This provides real-time updates across all components that display session data.
 *
 * Subscribe to the following events:
 * - SessionStarted: When a new session is created
 * - SessionStopped: When a session is stopped
 * - SessionStatusChanged: When session status changes (e.g., running -> waiting)
 * - SessionError: When a session encounters an error
 * - SessionResultReceived: When session results are received
 * - SessionModeModelChanged: When session mode or model changes
 * - SessionContextCleared: When context is cleared and a new session starts
 */
export function useSessionsSignalR(): void {
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

    // When context is cleared, we need to invalidate session history as well
    const invalidateOnContextCleared = (
      _oldSessionId: string,
      newSession: { projectId?: string; entityId?: string }
    ) => {
      invalidateAllSessionsQueries(queryClient)
      invalidateTaskGraphQueries(queryClient)
      // Invalidate session history for this entity to show the new session
      if (newSession.projectId && newSession.entityId) {
        queryClient.invalidateQueries({
          queryKey: sessionHistoryQueryKey(newSession.projectId, newSession.entityId),
        })
      }
    }

    const cleanup = registerClaudeCodeHubEvents(connection, {
      onSessionStarted: invalidateSessionAndTaskGraphs,
      onSessionStopped: invalidateSessionAndTaskGraphs,
      onSessionStatusChanged: invalidateSessionAndTaskGraphs,
      onSessionError: invalidate,
      onSessionResultReceived: invalidate,
      onSessionModeModelChanged: invalidate,
      onSessionContextCleared: invalidateOnContextCleared,
    })

    return cleanup
  }, [connection, isConnected, queryClient])
}
