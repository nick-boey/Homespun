/**
 * Tests for SignalR connection management.
 */

import { describe, it, expect, vi, beforeEach } from 'vitest'
import * as signalR from '@microsoft/signalr'
import { startConnection, stopConnection, getConnectionStatus } from './connection'

describe('startConnection', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('starts the connection and calls onStatusChange', async () => {
    const mockConnection = {
      state: signalR.HubConnectionState.Disconnected,
      start: vi.fn().mockResolvedValue(undefined),
    } as unknown as signalR.HubConnection

    const onStatusChange = vi.fn()
    const result = await startConnection(mockConnection, onStatusChange)

    expect(result).toBe(true)
    expect(mockConnection.start).toHaveBeenCalled()
    expect(onStatusChange).toHaveBeenCalledWith('connecting')
    expect(onStatusChange).toHaveBeenCalledWith('connected')
  })

  it('returns true if already connected', async () => {
    const mockConnection = {
      state: signalR.HubConnectionState.Connected,
      start: vi.fn(),
    } as unknown as signalR.HubConnection

    const result = await startConnection(mockConnection)

    expect(result).toBe(true)
    expect(mockConnection.start).not.toHaveBeenCalled()
  })

  it('handles connection errors', async () => {
    const mockConnection = {
      state: signalR.HubConnectionState.Disconnected,
      start: vi.fn().mockRejectedValue(new Error('Connection failed')),
    } as unknown as signalR.HubConnection

    const onStatusChange = vi.fn()
    const result = await startConnection(mockConnection, onStatusChange)

    expect(result).toBe(false)
    expect(onStatusChange).toHaveBeenCalledWith('disconnected', 'Connection failed')
  })

  it('handles non-Error rejection', async () => {
    const mockConnection = {
      state: signalR.HubConnectionState.Disconnected,
      start: vi.fn().mockRejectedValue('string error'),
    } as unknown as signalR.HubConnection

    const onStatusChange = vi.fn()
    const result = await startConnection(mockConnection, onStatusChange)

    expect(result).toBe(false)
    expect(onStatusChange).toHaveBeenCalledWith('disconnected', 'Unknown connection error')
  })
})

describe('stopConnection', () => {
  it('stops the connection', async () => {
    const mockConnection = {
      state: signalR.HubConnectionState.Connected,
      stop: vi.fn().mockResolvedValue(undefined),
    } as unknown as signalR.HubConnection

    await stopConnection(mockConnection)

    expect(mockConnection.stop).toHaveBeenCalled()
  })

  it('does nothing if already disconnected', async () => {
    const mockConnection = {
      state: signalR.HubConnectionState.Disconnected,
      stop: vi.fn(),
    } as unknown as signalR.HubConnection

    await stopConnection(mockConnection)

    expect(mockConnection.stop).not.toHaveBeenCalled()
  })

  it('handles stop errors gracefully', async () => {
    const consoleSpy = vi.spyOn(console, 'warn').mockImplementation(() => {})
    const mockConnection = {
      state: signalR.HubConnectionState.Connected,
      stop: vi.fn().mockRejectedValue(new Error('Stop failed')),
    } as unknown as signalR.HubConnection

    // Should not throw
    await stopConnection(mockConnection)

    expect(consoleSpy).toHaveBeenCalled()
    consoleSpy.mockRestore()
  })
})

describe('getConnectionStatus', () => {
  it('returns connected for Connected state', () => {
    expect(getConnectionStatus(signalR.HubConnectionState.Connected)).toBe('connected')
  })

  it('returns connecting for Connecting state', () => {
    expect(getConnectionStatus(signalR.HubConnectionState.Connecting)).toBe('connecting')
  })

  it('returns reconnecting for Reconnecting state', () => {
    expect(getConnectionStatus(signalR.HubConnectionState.Reconnecting)).toBe('reconnecting')
  })

  it('returns disconnected for Disconnected state', () => {
    expect(getConnectionStatus(signalR.HubConnectionState.Disconnected)).toBe('disconnected')
  })

  it('returns disconnected for Disconnecting state', () => {
    expect(getConnectionStatus(signalR.HubConnectionState.Disconnecting)).toBe('disconnected')
  })
})
