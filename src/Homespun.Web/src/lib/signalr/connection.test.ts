/**
 * Tests for SignalR connection management.
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import * as signalR from '@microsoft/signalr'
import { startConnection, stopConnection, getConnectionStatus, _internal } from './connection'

describe('startConnection', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    _internal.setSleepForTesting(() => Promise.resolve())
  })

  afterEach(() => {
    _internal.setSleepForTesting((ms) => new Promise((r) => setTimeout(r, ms)))
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

  it('retries on initial failure and succeeds on a later attempt', async () => {
    const mockConnection = {
      state: signalR.HubConnectionState.Disconnected,
      start: vi
        .fn()
        .mockRejectedValueOnce(new Error('Connection failed'))
        .mockRejectedValueOnce(new Error('Connection failed'))
        .mockResolvedValueOnce(undefined),
    } as unknown as signalR.HubConnection

    const onStatusChange = vi.fn()
    const result = await startConnection(mockConnection, onStatusChange)

    expect(result).toBe(true)
    expect(mockConnection.start).toHaveBeenCalledTimes(3)
    // Status transitions through reconnecting between attempts, and ends on connected.
    const calls = onStatusChange.mock.calls.map((c) => c[0])
    expect(calls).toEqual([
      'connecting',
      'reconnecting',
      'connecting',
      'reconnecting',
      'connecting',
      'connected',
    ])
  })

  it('gives up after maxInitialAttempts', async () => {
    const mockConnection = {
      state: signalR.HubConnectionState.Disconnected,
      start: vi.fn().mockRejectedValue(new Error('Connection failed')),
    } as unknown as signalR.HubConnection
    _internal.setOptsForTesting(mockConnection, { maxInitialAttempts: 2 })

    const onStatusChange = vi.fn()
    const result = await startConnection(mockConnection, onStatusChange)

    expect(result).toBe(false)
    expect(mockConnection.start).toHaveBeenCalledTimes(2)
    expect(onStatusChange).toHaveBeenLastCalledWith('disconnected', 'Connection failed')
  })

  it('reports Unknown connection error for non-Error rejections after give-up', async () => {
    const mockConnection = {
      state: signalR.HubConnectionState.Disconnected,
      start: vi.fn().mockRejectedValue('string error'),
    } as unknown as signalR.HubConnection
    _internal.setOptsForTesting(mockConnection, { maxInitialAttempts: 1 })

    const onStatusChange = vi.fn()
    const result = await startConnection(mockConnection, onStatusChange)

    expect(result).toBe(false)
    expect(onStatusChange).toHaveBeenLastCalledWith('disconnected', 'Unknown connection error')
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
