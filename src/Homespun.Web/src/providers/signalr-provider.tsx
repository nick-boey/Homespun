/**
 * SignalR Provider component that manages connections to both hubs.
 */

import {
  createContext,
  useContext,
  useEffect,
  useRef,
  useState,
  useCallback,
  useMemo,
  type ReactNode,
} from 'react'
import type { HubConnection } from '@microsoft/signalr'
import { trace, type Span } from '@opentelemetry/api'
import { createHubConnection, startConnection, stopConnection } from '@/lib/signalr/connection'
import {
  createClaudeCodeHubMethods,
  type ClaudeCodeHubMethods,
} from '@/lib/signalr/claude-code-hub'
import {
  createNotificationHubMethods,
  type NotificationHubMethods,
} from '@/lib/signalr/notification-hub'
import type { ConnectionStatus } from '@/types/signalr'

const HUB_TRACER = 'homespun.web.signalr'
const CONNECT_SPAN_NAME = 'homespun.signalr.client.connect'

// ============================================================================
// Context Types
// ============================================================================

export interface SignalRContextValue {
  // Claude Code Hub
  claudeCodeConnection: HubConnection | null
  claudeCodeStatus: ConnectionStatus
  claudeCodeError: string | undefined
  claudeCodeMethods: ClaudeCodeHubMethods | null

  // Notification Hub
  notificationConnection: HubConnection | null
  notificationStatus: ConnectionStatus
  notificationError: string | undefined
  notificationMethods: NotificationHubMethods | null

  // Combined status
  isConnecting: boolean
  isConnected: boolean
  isReconnecting: boolean

  // Actions
  connect: () => Promise<void>
  disconnect: () => Promise<void>
}

// ============================================================================
// Hub URLs
// ============================================================================

export const HUB_URLS = {
  claudeCode: '/hubs/claudecode',
  notifications: '/hubs/notifications',
} as const

// ============================================================================
// Context
// ============================================================================

const SignalRContext = createContext<SignalRContextValue | null>(null)

// ============================================================================
// Provider Props
// ============================================================================

export interface SignalRProviderProps {
  children: ReactNode
  /** Whether to automatically connect on mount */
  autoConnect?: boolean
  /** Callback when Claude Code hub reconnects */
  onClaudeCodeReconnected?: () => void
  /** Callback when Notification hub reconnects */
  onNotificationReconnected?: () => void
}

// ============================================================================
// Provider Component
// ============================================================================

export function SignalRProvider({
  children,
  autoConnect = true,
  onClaudeCodeReconnected,
  onNotificationReconnected,
}: SignalRProviderProps) {
  // Claude Code Hub state - use state for connections to avoid ref access during render
  const [claudeCodeConnection, setClaudeCodeConnection] = useState<HubConnection | null>(null)
  const [claudeCodeStatus, setClaudeCodeStatus] = useState<ConnectionStatus>('disconnected')
  const [claudeCodeError, setClaudeCodeError] = useState<string | undefined>()
  const [claudeCodeMethods, setClaudeCodeMethods] = useState<ClaudeCodeHubMethods | null>(null)

  // Notification Hub state
  const [notificationConnection, setNotificationConnection] = useState<HubConnection | null>(null)
  const [notificationStatus, setNotificationStatus] = useState<ConnectionStatus>('disconnected')
  const [notificationError, setNotificationError] = useState<string | undefined>()
  const [notificationMethods, setNotificationMethods] = useState<NotificationHubMethods | null>(
    null
  )

  // Store stable refs for callbacks
  const onClaudeCodeReconnectedRef = useRef(onClaudeCodeReconnected)
  const onNotificationReconnectedRef = useRef(onNotificationReconnected)

  // Long-lived span that records the Claude Code hub's connection lifetime.
  // Lifecycle transitions (connect/disconnect/reconnecting/reconnected) are
  // attached as span events, not separate spans — one span per page load is
  // enough signal for "did the socket wobble?" questions in Seq and keeps the
  // trace tree readable.
  const claudeCodeLifecycleSpanRef = useRef<Span | null>(null)

  useEffect(() => {
    onClaudeCodeReconnectedRef.current = onClaudeCodeReconnected
  }, [onClaudeCodeReconnected])

  useEffect(() => {
    onNotificationReconnectedRef.current = onNotificationReconnected
  }, [onNotificationReconnected])

  // Initialize connections
  useEffect(() => {
    const tracer = trace.getTracer(HUB_TRACER)
    const lifecycleSpan = tracer.startSpan(CONNECT_SPAN_NAME)
    claudeCodeLifecycleSpanRef.current = lifecycleSpan

    const addLifecycleEvent = (status: ConnectionStatus, error?: string) => {
      const span = claudeCodeLifecycleSpanRef.current
      if (!span) return
      const attrs: Record<string, string> = { 'homespun.signalr.status': status }
      if (error) attrs['exception.message'] = error
      span.addEvent(`signalr.${status}`, attrs)
    }

    // Create Claude Code hub connection
    const claudeCodeConn = createHubConnection({
      hubUrl: HUB_URLS.claudeCode,
      onStatusChange: (status, error) => {
        setClaudeCodeStatus(status)
        setClaudeCodeError(error)
        addLifecycleEvent(status, error)
      },
      onReconnected: () => {
        addLifecycleEvent('connected')
        onClaudeCodeReconnectedRef.current?.()
      },
    })
    setClaudeCodeConnection(claudeCodeConn)
    setClaudeCodeMethods(createClaudeCodeHubMethods(claudeCodeConn))

    // Create Notification hub connection
    const notificationConn = createHubConnection({
      hubUrl: HUB_URLS.notifications,
      onStatusChange: (status, error) => {
        setNotificationStatus(status)
        setNotificationError(error)
      },
      onReconnected: () => onNotificationReconnectedRef.current?.(),
    })
    setNotificationConnection(notificationConn)
    setNotificationMethods(createNotificationHubMethods(notificationConn))

    // Auto-connect if enabled
    if (autoConnect) {
      startConnection(claudeCodeConn, (status, error) => {
        setClaudeCodeStatus(status)
        setClaudeCodeError(error)
        addLifecycleEvent(status, error)
      })
      startConnection(notificationConn, (status, error) => {
        setNotificationStatus(status)
        setNotificationError(error)
      })
    }

    // Cleanup on unmount
    return () => {
      stopConnection(claudeCodeConn)
      stopConnection(notificationConn)
      setClaudeCodeConnection(null)
      setNotificationConnection(null)
      const span = claudeCodeLifecycleSpanRef.current
      if (span) {
        span.addEvent('signalr.teardown')
        span.end()
        claudeCodeLifecycleSpanRef.current = null
      }
    }
  }, [autoConnect])

  // Manual connect function
  const connect = useCallback(async () => {
    if (claudeCodeConnection) {
      await startConnection(claudeCodeConnection, (status, error) => {
        setClaudeCodeStatus(status)
        setClaudeCodeError(error)
      })
    }

    if (notificationConnection) {
      await startConnection(notificationConnection, (status, error) => {
        setNotificationStatus(status)
        setNotificationError(error)
      })
    }
  }, [claudeCodeConnection, notificationConnection])

  // Manual disconnect function
  const disconnect = useCallback(async () => {
    if (claudeCodeConnection) {
      await stopConnection(claudeCodeConnection)
      setClaudeCodeStatus('disconnected')
    }

    if (notificationConnection) {
      await stopConnection(notificationConnection)
      setNotificationStatus('disconnected')
    }
  }, [claudeCodeConnection, notificationConnection])

  // Compute combined status
  const isConnecting = claudeCodeStatus === 'connecting' || notificationStatus === 'connecting'
  const isConnected = claudeCodeStatus === 'connected' && notificationStatus === 'connected'
  const isReconnecting =
    claudeCodeStatus === 'reconnecting' || notificationStatus === 'reconnecting'

  const contextValue = useMemo<SignalRContextValue>(
    () => ({
      claudeCodeConnection,
      claudeCodeStatus,
      claudeCodeError,
      claudeCodeMethods,
      notificationConnection,
      notificationStatus,
      notificationError,
      notificationMethods,
      isConnecting,
      isConnected,
      isReconnecting,
      connect,
      disconnect,
    }),
    [
      claudeCodeConnection,
      claudeCodeStatus,
      claudeCodeError,
      claudeCodeMethods,
      notificationConnection,
      notificationStatus,
      notificationError,
      notificationMethods,
      isConnecting,
      isConnected,
      isReconnecting,
      connect,
      disconnect,
    ]
  )

  return <SignalRContext.Provider value={contextValue}>{children}</SignalRContext.Provider>
}

// ============================================================================
// Hooks
// ============================================================================

/**
 * Hook to access the full SignalR context.
 */
export function useSignalRContext(): SignalRContextValue {
  const context = useContext(SignalRContext)
  if (!context) {
    throw new Error('useSignalRContext must be used within a SignalRProvider')
  }
  return context
}

/**
 * Hook to access the Claude Code hub connection and methods.
 */
export function useClaudeCodeHub() {
  const { claudeCodeConnection, claudeCodeStatus, claudeCodeError, claudeCodeMethods } =
    useSignalRContext()

  return {
    connection: claudeCodeConnection,
    status: claudeCodeStatus,
    error: claudeCodeError,
    methods: claudeCodeMethods,
    isConnected: claudeCodeStatus === 'connected',
    isReconnecting: claudeCodeStatus === 'reconnecting',
  }
}

/**
 * Hook to access the Notification hub connection and methods.
 */
export function useNotificationHub() {
  const { notificationConnection, notificationStatus, notificationError, notificationMethods } =
    useSignalRContext()

  return {
    connection: notificationConnection,
    status: notificationStatus,
    error: notificationError,
    methods: notificationMethods,
    isConnected: notificationStatus === 'connected',
    isReconnecting: notificationStatus === 'reconnecting',
  }
}

/**
 * Hook to access combined connection status.
 */
export function useSignalRStatus() {
  const { isConnecting, isConnected, isReconnecting, connect, disconnect } = useSignalRContext()

  return {
    isConnecting,
    isConnected,
    isReconnecting,
    connect,
    disconnect,
  }
}
