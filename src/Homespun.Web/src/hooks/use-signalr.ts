/**
 * React hook for managing SignalR connections.
 */

import { useEffect, useRef, useCallback, useState, useSyncExternalStore } from 'react'
import type { HubConnection } from '@microsoft/signalr'
import {
  createHubConnection,
  startConnection,
  stopConnection,
  getConnectionStatus,
  type ConnectionOptions,
} from '@/lib/signalr/connection'
import type { ConnectionStatus } from '@/types/signalr'

export interface UseSignalROptions extends Omit<
  ConnectionOptions,
  'onStatusChange' | 'onReconnected'
> {
  /** Whether to automatically connect on mount */
  autoConnect?: boolean
  /** Callback when connection status changes */
  onStatusChange?: (status: ConnectionStatus, error?: string) => void
  /** Callback when successfully reconnected (useful for re-joining groups) */
  onReconnected?: () => void
}

export interface UseSignalRResult {
  /** The SignalR connection instance */
  connection: HubConnection | null
  /** Current connection status */
  status: ConnectionStatus
  /** Last error message, if any */
  error: string | undefined
  /** Manually start the connection */
  connect: () => Promise<boolean>
  /** Manually stop the connection */
  disconnect: () => Promise<void>
}

/**
 * Hook for managing a SignalR hub connection.
 *
 * @example
 * ```tsx
 * const { connection, status, connect, disconnect } = useSignalR({
 *   hubUrl: '/hubs/claudecode',
 *   autoConnect: true,
 *   onReconnected: () => {
 *     // Re-join session groups after reconnect
 *     connection?.invoke('JoinSession', sessionId);
 *   },
 * });
 * ```
 */
export function useSignalR(options: UseSignalROptions): UseSignalRResult {
  const {
    hubUrl,
    autoConnect = true,
    onStatusChange,
    onReconnected,
    ...connectionOptions
  } = options

  const connectionRef = useRef<HubConnection | null>(null)
  const [status, setStatus] = useState<ConnectionStatus>('disconnected')
  const [error, setError] = useState<string | undefined>()

  // Handle status changes
  const handleStatusChange = useCallback(
    (newStatus: ConnectionStatus, errorMessage?: string) => {
      setStatus(newStatus)
      setError(errorMessage)
      onStatusChange?.(newStatus, errorMessage)
    },
    [onStatusChange]
  )

  // Handle reconnection
  const handleReconnected = useCallback(() => {
    onReconnected?.()
  }, [onReconnected])

  // Initialize connection
  useEffect(() => {
    const connection = createHubConnection({
      hubUrl,
      ...connectionOptions,
      onStatusChange: handleStatusChange,
      onReconnected: handleReconnected,
    })

    connectionRef.current = connection

    if (autoConnect) {
      startConnection(connection, handleStatusChange)
    }

    // Cleanup on unmount
    return () => {
      stopConnection(connection)
      connectionRef.current = null
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [hubUrl]) // Only recreate connection when hubUrl changes

  // Manual connect function
  const connect = useCallback(async (): Promise<boolean> => {
    const connection = connectionRef.current
    if (!connection) {
      handleStatusChange('disconnected', 'No connection available')
      return false
    }
    return startConnection(connection, handleStatusChange)
  }, [handleStatusChange])

  // Manual disconnect function
  const disconnect = useCallback(async (): Promise<void> => {
    const connection = connectionRef.current
    if (connection) {
      await stopConnection(connection)
      handleStatusChange('disconnected')
    }
  }, [handleStatusChange])

  return {
    connection: connectionRef.current,
    status,
    error,
    connect,
    disconnect,
  }
}

/**
 * Hook for subscribing to SignalR events.
 * Automatically cleans up subscriptions on unmount.
 *
 * @example
 * ```tsx
 * useSignalREvent(connection, 'SessionStarted', (session) => {
 *   console.log('Session started:', session);
 * });
 * ```
 */
export function useSignalREvent<T extends unknown[]>(
  connection: HubConnection | null,
  eventName: string,
  handler: (...args: T) => void
): void {
  useEffect(() => {
    if (!connection) return

    const wrappedHandler = (...args: unknown[]) => handler(...(args as T))
    connection.on(eventName, wrappedHandler)

    return () => {
      connection.off(eventName, wrappedHandler)
    }
  }, [connection, eventName, handler])
}

/**
 * Hook for invoking a SignalR hub method.
 * Returns a function that invokes the method and handles errors.
 *
 * @example
 * ```tsx
 * const joinSession = useSignalRInvoke<[string], void>(connection, 'JoinSession');
 * await joinSession(sessionId);
 * ```
 */
export function useSignalRInvoke<TArgs extends unknown[], TResult>(
  connection: HubConnection | null,
  methodName: string
): (...args: TArgs) => Promise<TResult | undefined> {
  return useCallback(
    async (...args: TArgs): Promise<TResult | undefined> => {
      if (!connection) {
        console.warn(`Cannot invoke ${methodName}: no connection`)
        return undefined
      }

      try {
        return await connection.invoke<TResult>(methodName, ...args)
      } catch (error) {
        console.error(`Error invoking ${methodName}:`, error)
        throw error
      }
    },
    [connection, methodName]
  )
}

/**
 * Hook to track connection state from the SignalR HubConnection.
 * Uses useSyncExternalStore for proper subscription to external state.
 */
export function useConnectionState(connection: HubConnection | null): ConnectionStatus {
  // Create a stable subscribe function
  const subscribe = useCallback(
    (onStoreChange: () => void) => {
      if (!connection) return () => {}

      // Subscribe to all state change events
      connection.onreconnecting(onStoreChange)
      connection.onreconnected(onStoreChange)
      connection.onclose(onStoreChange)

      // Poll for state changes since SignalR doesn't have a connecting event
      const interval = setInterval(onStoreChange, 100)

      return () => {
        clearInterval(interval)
      }
    },
    [connection]
  )

  // Get current snapshot
  const getSnapshot = useCallback((): ConnectionStatus => {
    if (!connection) return 'disconnected'
    return getConnectionStatus(connection.state)
  }, [connection])

  // Server snapshot for SSR (always disconnected)
  const getServerSnapshot = useCallback((): ConnectionStatus => 'disconnected', [])

  return useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot)
}
