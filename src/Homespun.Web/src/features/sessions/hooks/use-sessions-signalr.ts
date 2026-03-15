import { useEffect } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useClaudeCodeHub } from '@/providers/signalr-provider'
import { registerClaudeCodeHubEvents } from '@/lib/signalr/claude-code-hub'
import { invalidateAllSessionsQueries } from './use-sessions'

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
 */
export function useSessionsSignalR(): void {
  const queryClient = useQueryClient()
  const { connection, isConnected } = useClaudeCodeHub()

  useEffect(() => {
    if (!connection || !isConnected) return

    const invalidate = () => {
      invalidateAllSessionsQueries(queryClient)
    }

    const cleanup = registerClaudeCodeHubEvents(connection, {
      onSessionStarted: invalidate,
      onSessionStopped: invalidate,
      onSessionStatusChanged: invalidate,
      onSessionError: invalidate,
      onSessionResultReceived: invalidate,
      onSessionModeModelChanged: invalidate,
    })

    return cleanup
  }, [connection, isConnected, queryClient])
}
