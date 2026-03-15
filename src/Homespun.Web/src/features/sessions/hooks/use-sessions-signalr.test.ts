import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement, type ReactNode } from 'react'
import type { HubConnection } from '@microsoft/signalr'
import { useSessionsSignalR } from './use-sessions-signalr'
import * as signalrProvider from '@/providers/signalr-provider'
import * as useSessions from './use-sessions'

// Mock the signalr provider
vi.mock('@/providers/signalr-provider', () => ({
  useClaudeCodeHub: vi.fn(),
}))

// Mock the invalidateAllSessionsQueries function
vi.mock('./use-sessions', async (importOriginal) => {
  const actual = await importOriginal<typeof import('./use-sessions')>()
  return {
    ...actual,
    invalidateAllSessionsQueries: vi.fn(),
  }
})

function createMockConnection(): HubConnection & {
  _handlers: Map<string, (...args: unknown[]) => void>
  simulateEvent: (name: string, ...args: unknown[]) => void
} {
  const handlers = new Map<string, (...args: unknown[]) => void>()

  return {
    on: vi.fn((name: string, handler: (...args: unknown[]) => void) => {
      handlers.set(name, handler)
    }),
    off: vi.fn((name: string) => {
      handlers.delete(name)
    }),
    invoke: vi.fn().mockResolvedValue(undefined),
    _handlers: handlers,
    simulateEvent: (name: string, ...args: unknown[]) => {
      const handler = handlers.get(name)
      if (handler) {
        handler(...args)
      }
    },
  } as unknown as HubConnection & {
    _handlers: Map<string, (...args: unknown[]) => void>
    simulateEvent: (name: string, ...args: unknown[]) => void
  }
}

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
    },
  })
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: queryClient }, children)
}

describe('useSessionsSignalR', () => {
  let mockConnection: ReturnType<typeof createMockConnection>

  beforeEach(() => {
    vi.clearAllMocks()
    mockConnection = createMockConnection()
  })

  it('registers SignalR event handlers when connected', () => {
    vi.mocked(signalrProvider.useClaudeCodeHub).mockReturnValue({
      connection: mockConnection,
      isConnected: true,
      status: 'connected',
      error: undefined,
      methods: null,
      isReconnecting: false,
    } as ReturnType<typeof signalrProvider.useClaudeCodeHub>)

    renderHook(() => useSessionsSignalR(), { wrapper: createWrapper() })

    expect(mockConnection.on).toHaveBeenCalledWith('SessionStarted', expect.any(Function))
    expect(mockConnection.on).toHaveBeenCalledWith('SessionStopped', expect.any(Function))
    expect(mockConnection.on).toHaveBeenCalledWith('SessionStatusChanged', expect.any(Function))
    expect(mockConnection.on).toHaveBeenCalledWith('SessionError', expect.any(Function))
    expect(mockConnection.on).toHaveBeenCalledWith('SessionResultReceived', expect.any(Function))
    expect(mockConnection.on).toHaveBeenCalledWith('SessionModeModelChanged', expect.any(Function))
  })

  it('does not register handlers when disconnected', () => {
    vi.mocked(signalrProvider.useClaudeCodeHub).mockReturnValue({
      connection: null,
      isConnected: false,
      status: 'disconnected',
      error: undefined,
      methods: null,
      isReconnecting: false,
    } as ReturnType<typeof signalrProvider.useClaudeCodeHub>)

    renderHook(() => useSessionsSignalR(), { wrapper: createWrapper() })

    expect(mockConnection.on).not.toHaveBeenCalled()
  })

  it('does not register handlers when connection exists but not connected', () => {
    vi.mocked(signalrProvider.useClaudeCodeHub).mockReturnValue({
      connection: mockConnection,
      isConnected: false,
      status: 'connecting',
      error: undefined,
      methods: null,
      isReconnecting: false,
    } as ReturnType<typeof signalrProvider.useClaudeCodeHub>)

    renderHook(() => useSessionsSignalR(), { wrapper: createWrapper() })

    expect(mockConnection.on).not.toHaveBeenCalled()
  })

  it('cleans up event handlers on unmount', () => {
    vi.mocked(signalrProvider.useClaudeCodeHub).mockReturnValue({
      connection: mockConnection,
      isConnected: true,
      status: 'connected',
      error: undefined,
      methods: null,
      isReconnecting: false,
    } as ReturnType<typeof signalrProvider.useClaudeCodeHub>)

    const { unmount } = renderHook(() => useSessionsSignalR(), { wrapper: createWrapper() })

    unmount()

    expect(mockConnection.off).toHaveBeenCalledWith('SessionStarted', expect.any(Function))
    expect(mockConnection.off).toHaveBeenCalledWith('SessionStopped', expect.any(Function))
    expect(mockConnection.off).toHaveBeenCalledWith('SessionStatusChanged', expect.any(Function))
    expect(mockConnection.off).toHaveBeenCalledWith('SessionError', expect.any(Function))
    expect(mockConnection.off).toHaveBeenCalledWith('SessionResultReceived', expect.any(Function))
    expect(mockConnection.off).toHaveBeenCalledWith('SessionModeModelChanged', expect.any(Function))
  })

  it('calls invalidateAllSessionsQueries on SessionStarted event', async () => {
    vi.mocked(signalrProvider.useClaudeCodeHub).mockReturnValue({
      connection: mockConnection,
      isConnected: true,
      status: 'connected',
      error: undefined,
      methods: null,
      isReconnecting: false,
    } as ReturnType<typeof signalrProvider.useClaudeCodeHub>)

    renderHook(() => useSessionsSignalR(), { wrapper: createWrapper() })

    mockConnection.simulateEvent('SessionStarted', { id: 'session-1' })

    await waitFor(() => {
      expect(useSessions.invalidateAllSessionsQueries).toHaveBeenCalled()
    })
  })

  it('calls invalidateAllSessionsQueries on SessionStopped event', async () => {
    vi.mocked(signalrProvider.useClaudeCodeHub).mockReturnValue({
      connection: mockConnection,
      isConnected: true,
      status: 'connected',
      error: undefined,
      methods: null,
      isReconnecting: false,
    } as ReturnType<typeof signalrProvider.useClaudeCodeHub>)

    renderHook(() => useSessionsSignalR(), { wrapper: createWrapper() })

    mockConnection.simulateEvent('SessionStopped', 'session-1')

    await waitFor(() => {
      expect(useSessions.invalidateAllSessionsQueries).toHaveBeenCalled()
    })
  })

  it('calls invalidateAllSessionsQueries on SessionStatusChanged event', async () => {
    vi.mocked(signalrProvider.useClaudeCodeHub).mockReturnValue({
      connection: mockConnection,
      isConnected: true,
      status: 'connected',
      error: undefined,
      methods: null,
      isReconnecting: false,
    } as ReturnType<typeof signalrProvider.useClaudeCodeHub>)

    renderHook(() => useSessionsSignalR(), { wrapper: createWrapper() })

    mockConnection.simulateEvent('SessionStatusChanged', 'session-1', 'running', false)

    await waitFor(() => {
      expect(useSessions.invalidateAllSessionsQueries).toHaveBeenCalled()
    })
  })

  it('calls invalidateAllSessionsQueries on SessionError event', async () => {
    vi.mocked(signalrProvider.useClaudeCodeHub).mockReturnValue({
      connection: mockConnection,
      isConnected: true,
      status: 'connected',
      error: undefined,
      methods: null,
      isReconnecting: false,
    } as ReturnType<typeof signalrProvider.useClaudeCodeHub>)

    renderHook(() => useSessionsSignalR(), { wrapper: createWrapper() })

    mockConnection.simulateEvent('SessionError', 'session-1', 'Error message', null, false)

    await waitFor(() => {
      expect(useSessions.invalidateAllSessionsQueries).toHaveBeenCalled()
    })
  })

  it('calls invalidateAllSessionsQueries on SessionResultReceived event', async () => {
    vi.mocked(signalrProvider.useClaudeCodeHub).mockReturnValue({
      connection: mockConnection,
      isConnected: true,
      status: 'connected',
      error: undefined,
      methods: null,
      isReconnecting: false,
    } as ReturnType<typeof signalrProvider.useClaudeCodeHub>)

    renderHook(() => useSessionsSignalR(), { wrapper: createWrapper() })

    mockConnection.simulateEvent('SessionResultReceived', 'session-1', 0.05, 1000)

    await waitFor(() => {
      expect(useSessions.invalidateAllSessionsQueries).toHaveBeenCalled()
    })
  })

  it('calls invalidateAllSessionsQueries on SessionModeModelChanged event', async () => {
    vi.mocked(signalrProvider.useClaudeCodeHub).mockReturnValue({
      connection: mockConnection,
      isConnected: true,
      status: 'connected',
      error: undefined,
      methods: null,
      isReconnecting: false,
    } as ReturnType<typeof signalrProvider.useClaudeCodeHub>)

    renderHook(() => useSessionsSignalR(), { wrapper: createWrapper() })

    mockConnection.simulateEvent('SessionModeModelChanged', 'session-1', 'plan', 'opus')

    await waitFor(() => {
      expect(useSessions.invalidateAllSessionsQueries).toHaveBeenCalled()
    })
  })
})
