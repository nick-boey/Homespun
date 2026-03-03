/**
 * SignalR connection management with automatic reconnection.
 */

import * as signalR from '@microsoft/signalr'
import type { ConnectionStatus } from '@/types/signalr'

export interface ConnectionOptions {
  /** Hub URL path (e.g., '/hubs/claudecode') */
  hubUrl: string
  /** Maximum reconnection attempts before giving up (0 = unlimited) */
  maxReconnectAttempts?: number
  /** Base delay for exponential backoff in milliseconds */
  baseReconnectDelay?: number
  /** Maximum delay between reconnection attempts in milliseconds */
  maxReconnectDelay?: number
  /** Callback when connection status changes */
  onStatusChange?: (status: ConnectionStatus, error?: string) => void
  /** Callback when successfully reconnected */
  onReconnected?: () => void
}

const DEFAULT_OPTIONS: Required<
  Omit<ConnectionOptions, 'hubUrl' | 'onStatusChange' | 'onReconnected'>
> = {
  maxReconnectAttempts: 0, // Unlimited
  baseReconnectDelay: 1000, // 1 second
  maxReconnectDelay: 30000, // 30 seconds
}

/**
 * Creates an exponential backoff retry policy for SignalR.
 */
function createRetryPolicy(
  baseDelay: number,
  maxDelay: number,
  maxAttempts: number
): signalR.IRetryPolicy {
  return {
    nextRetryDelayInMilliseconds: (retryContext: signalR.RetryContext): number | null => {
      // If we've exceeded max attempts, stop retrying
      if (maxAttempts > 0 && retryContext.previousRetryCount >= maxAttempts) {
        return null
      }

      // Exponential backoff with jitter
      const exponentialDelay = baseDelay * Math.pow(2, retryContext.previousRetryCount)
      const jitter = Math.random() * 0.3 * exponentialDelay // Up to 30% jitter
      const delay = Math.min(exponentialDelay + jitter, maxDelay)

      return delay
    },
  }
}

/**
 * Creates and manages a SignalR hub connection.
 */
export function createHubConnection(options: ConnectionOptions): signalR.HubConnection {
  const opts = { ...DEFAULT_OPTIONS, ...options }

  const connection = new signalR.HubConnectionBuilder()
    .withUrl(opts.hubUrl)
    .withAutomaticReconnect(
      createRetryPolicy(opts.baseReconnectDelay, opts.maxReconnectDelay, opts.maxReconnectAttempts)
    )
    .configureLogging(signalR.LogLevel.Warning)
    .build()

  // Set up connection state change handlers
  connection.onreconnecting((error) => {
    opts.onStatusChange?.('reconnecting', error?.message)
  })

  connection.onreconnected(() => {
    opts.onStatusChange?.('connected')
    opts.onReconnected?.()
  })

  connection.onclose((error) => {
    opts.onStatusChange?.('disconnected', error?.message)
  })

  return connection
}

/**
 * Starts a SignalR connection with error handling.
 * Returns true if connection was successful, false otherwise.
 */
export async function startConnection(
  connection: signalR.HubConnection,
  onStatusChange?: (status: ConnectionStatus, error?: string) => void
): Promise<boolean> {
  if (connection.state === signalR.HubConnectionState.Connected) {
    return true
  }

  if (connection.state === signalR.HubConnectionState.Connecting) {
    // Wait for the connection to complete
    return new Promise((resolve) => {
      const checkState = setInterval(() => {
        if (connection.state === signalR.HubConnectionState.Connected) {
          clearInterval(checkState)
          resolve(true)
        } else if (connection.state === signalR.HubConnectionState.Disconnected) {
          clearInterval(checkState)
          resolve(false)
        }
      }, 100)
    })
  }

  onStatusChange?.('connecting')

  try {
    await connection.start()
    onStatusChange?.('connected')
    return true
  } catch (error) {
    const errorMessage = error instanceof Error ? error.message : 'Unknown connection error'
    onStatusChange?.('disconnected', errorMessage)
    return false
  }
}

/**
 * Stops a SignalR connection gracefully.
 */
export async function stopConnection(connection: signalR.HubConnection): Promise<void> {
  if (connection.state === signalR.HubConnectionState.Disconnected) {
    return
  }

  try {
    await connection.stop()
  } catch (error) {
    // Log but don't throw - we're cleaning up anyway
    console.warn('Error stopping SignalR connection:', error)
  }
}

/**
 * Gets the current connection status from SignalR state.
 */
export function getConnectionStatus(state: signalR.HubConnectionState): ConnectionStatus {
  switch (state) {
    case signalR.HubConnectionState.Connected:
      return 'connected'
    case signalR.HubConnectionState.Connecting:
      return 'connecting'
    case signalR.HubConnectionState.Reconnecting:
      return 'reconnecting'
    case signalR.HubConnectionState.Disconnected:
    case signalR.HubConnectionState.Disconnecting:
    default:
      return 'disconnected'
  }
}
