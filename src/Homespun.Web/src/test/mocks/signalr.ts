/**
 * Mock SignalR HubConnection for testing.
 */

import { vi, type Mock } from 'vitest'
import type { HubConnectionState } from '@microsoft/signalr'

export interface MockHubConnection {
  state: HubConnectionState
  start: Mock<() => Promise<void>>
  stop: Mock<() => Promise<void>>
  invoke: Mock<(...args: unknown[]) => Promise<unknown>>
  on: Mock<(methodName: string, handler: (...args: unknown[]) => void) => void>
  off: Mock<(methodName: string, handler: (...args: unknown[]) => void) => void>
  onreconnecting: Mock<(callback: (error?: Error) => void) => void>
  onreconnected: Mock<(callback: (connectionId?: string) => void) => void>
  onclose: Mock<(callback: (error?: Error) => void) => void>
  _handlers: Map<string, Set<(...args: unknown[]) => void>>
  _reconnectingCallbacks: Array<(error?: Error) => void>
  _reconnectedCallbacks: Array<(connectionId?: string) => void>
  _closeCallbacks: Array<(error?: Error) => void>
  // Test helpers
  simulateEvent: (eventName: string, ...args: unknown[]) => void
  simulateReconnecting: (error?: Error) => void
  simulateReconnected: (connectionId?: string) => void
  simulateClose: (error?: Error) => void
  setState: (state: HubConnectionState) => void
}

export function createMockHubConnection(): MockHubConnection {
  const handlers = new Map<string, Set<(...args: unknown[]) => void>>()
  const reconnectingCallbacks: Array<(error?: Error) => void> = []
  const reconnectedCallbacks: Array<(connectionId?: string) => void> = []
  const closeCallbacks: Array<(error?: Error) => void> = []

  let state: HubConnectionState = 'Disconnected' as HubConnectionState

  const mockConnection: MockHubConnection = {
    state,
    _handlers: handlers,
    _reconnectingCallbacks: reconnectingCallbacks,
    _reconnectedCallbacks: reconnectedCallbacks,
    _closeCallbacks: closeCallbacks,

    start: vi.fn().mockImplementation(async () => {
      state = 'Connected' as HubConnectionState
      mockConnection.state = state
    }),

    stop: vi.fn().mockImplementation(async () => {
      state = 'Disconnected' as HubConnectionState
      mockConnection.state = state
    }),

    invoke: vi.fn().mockResolvedValue(undefined),

    on: vi.fn().mockImplementation((methodName: string, handler: (...args: unknown[]) => void) => {
      if (!handlers.has(methodName)) {
        handlers.set(methodName, new Set())
      }
      handlers.get(methodName)!.add(handler)
    }),

    off: vi.fn().mockImplementation((methodName: string, handler: (...args: unknown[]) => void) => {
      handlers.get(methodName)?.delete(handler)
    }),

    onreconnecting: vi.fn().mockImplementation((callback: (error?: Error) => void) => {
      reconnectingCallbacks.push(callback)
    }),

    onreconnected: vi.fn().mockImplementation((callback: (connectionId?: string) => void) => {
      reconnectedCallbacks.push(callback)
    }),

    onclose: vi.fn().mockImplementation((callback: (error?: Error) => void) => {
      closeCallbacks.push(callback)
    }),

    // Test helpers
    simulateEvent: (eventName: string, ...args: unknown[]) => {
      const eventHandlers = handlers.get(eventName)
      if (eventHandlers) {
        for (const handler of eventHandlers) {
          handler(...args)
        }
      }
    },

    simulateReconnecting: (error?: Error) => {
      state = 'Reconnecting' as HubConnectionState
      mockConnection.state = state
      for (const callback of reconnectingCallbacks) {
        callback(error)
      }
    },

    simulateReconnected: (connectionId?: string) => {
      state = 'Connected' as HubConnectionState
      mockConnection.state = state
      for (const callback of reconnectedCallbacks) {
        callback(connectionId)
      }
    },

    simulateClose: (error?: Error) => {
      state = 'Disconnected' as HubConnectionState
      mockConnection.state = state
      for (const callback of closeCallbacks) {
        callback(error)
      }
    },

    setState: (newState: HubConnectionState) => {
      state = newState
      mockConnection.state = state
    },
  }

  return mockConnection
}
