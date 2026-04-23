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
  /**
   * Maximum initial-connect attempts before giving up (0 = unlimited).
   * `withAutomaticReconnect` only engages after a successful first connect,
   * so this retry loop is what keeps the client alive when the server is
   * not yet reachable at page load (dev-live cold start, server bounce
   * before the tab reloads, etc.).
   */
  maxInitialAttempts?: number
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
  maxInitialAttempts: 0, // Unlimited
  baseReconnectDelay: 1000, // 1 second
  maxReconnectDelay: 30000, // 30 seconds
}

type ConnectionOpts = ConnectionOptions & typeof DEFAULT_OPTIONS
const CONNECTION_OPTS = new WeakMap<signalR.HubConnection, ConnectionOpts>()

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

  CONNECTION_OPTS.set(connection, opts)

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

const CONNECT_RETRY_JITTER = 0.3
// Exposed so tests can stub it out — test code replaces sleep with
// `(_ms) => Promise.resolve()` to avoid real delays.
let sleep: (ms: number) => Promise<void> = (ms) => new Promise((resolve) => setTimeout(resolve, ms))

export const _internal = {
  setSleepForTesting(fn: (ms: number) => Promise<void>) {
    sleep = fn
  },
  setOptsForTesting(connection: signalR.HubConnection, overrides: Partial<ConnectionOpts>) {
    const merged: ConnectionOpts = {
      ...(DEFAULT_OPTIONS as ConnectionOpts),
      ...(CONNECTION_OPTS.get(connection) ?? {}),
      ...overrides,
    }
    CONNECTION_OPTS.set(connection, merged)
  },
}

function computeInitialRetryDelay(attempt: number, base: number, cap: number): number {
  const exponential = base * Math.pow(2, attempt)
  const jitter = Math.random() * CONNECT_RETRY_JITTER * exponential
  return Math.min(exponential + jitter, cap)
}

/**
 * Starts a SignalR connection with exponential-backoff retry on initial
 * failure. `withAutomaticReconnect` only engages after a successful first
 * connect, so this loop is what keeps the client alive when the server is
 * not yet reachable at page load. Between attempts the status is reported
 * as `reconnecting` so the UI reconnect banner reflects accurate state.
 *
 * Returns `true` on success, `false` if `maxInitialAttempts` is exhausted.
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

  const opts = CONNECTION_OPTS.get(connection) ?? (DEFAULT_OPTIONS as ConnectionOpts)
  const maxAttempts = opts.maxInitialAttempts

  let attempt = 0

  while (true) {
    onStatusChange?.('connecting')
    try {
      await connection.start()
      onStatusChange?.('connected')
      return true
    } catch (error) {
      const errorMessage = error instanceof Error ? error.message : 'Unknown connection error'
      attempt += 1
      if (maxAttempts > 0 && attempt >= maxAttempts) {
        onStatusChange?.('disconnected', errorMessage)
        return false
      }
      // Signal that the client is still trying. `reconnecting` matches the
      // status used by `withAutomaticReconnect` after a drop, so the UI
      // banner logic doesn't need to special-case initial-connect.
      onStatusChange?.('reconnecting', errorMessage)
      const delay = computeInitialRetryDelay(
        attempt - 1,
        opts.baseReconnectDelay,
        opts.maxReconnectDelay
      )
      await sleep(delay)
    }
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
