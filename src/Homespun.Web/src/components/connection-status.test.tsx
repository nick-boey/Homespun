/**
 * Tests for connection status components.
 */

import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ConnectionStatus, ConnectionStatusBadge, ReconnectionBanner } from './connection-status'
import * as signalRProvider from '@/providers/signalr-provider'

// Mock the SignalR provider
vi.mock('@/providers/signalr-provider', () => ({
  useSignalRContext: vi.fn(),
}))

const mockUseSignalRContext = vi.mocked(signalRProvider.useSignalRContext)

describe('ConnectionStatus', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('shows connected status', () => {
    mockUseSignalRContext.mockReturnValue({
      claudeCodeStatus: 'connected',
      notificationStatus: 'connected',
      isConnected: true,
      isReconnecting: false,
      isConnecting: false,
    } as signalRProvider.SignalRContextValue)

    render(<ConnectionStatus showLabel />)

    expect(screen.getByText('Connected')).toBeInTheDocument()
  })

  it('shows connecting status', () => {
    mockUseSignalRContext.mockReturnValue({
      claudeCodeStatus: 'connecting',
      notificationStatus: 'disconnected',
      isConnected: false,
      isReconnecting: false,
      isConnecting: true,
    } as signalRProvider.SignalRContextValue)

    render(<ConnectionStatus showLabel />)

    expect(screen.getByText('Connecting...')).toBeInTheDocument()
  })

  it('shows reconnecting status', () => {
    mockUseSignalRContext.mockReturnValue({
      claudeCodeStatus: 'reconnecting',
      notificationStatus: 'connected',
      isConnected: false,
      isReconnecting: true,
      isConnecting: false,
    } as signalRProvider.SignalRContextValue)

    render(<ConnectionStatus showLabel />)

    expect(screen.getByText('Reconnecting...')).toBeInTheDocument()
  })

  it('shows disconnected status', () => {
    mockUseSignalRContext.mockReturnValue({
      claudeCodeStatus: 'disconnected',
      notificationStatus: 'disconnected',
      isConnected: false,
      isReconnecting: false,
      isConnecting: false,
    } as signalRProvider.SignalRContextValue)

    render(<ConnectionStatus showLabel />)

    expect(screen.getByText('Disconnected')).toBeInTheDocument()
  })

  it('shows detailed status when showDetails is true', () => {
    mockUseSignalRContext.mockReturnValue({
      claudeCodeStatus: 'connected',
      notificationStatus: 'reconnecting',
      isConnected: false,
      isReconnecting: true,
      isConnecting: false,
    } as signalRProvider.SignalRContextValue)

    render(<ConnectionStatus showDetails showLabel />)

    expect(screen.getByText(/Claude Code: Connected/)).toBeInTheDocument()
    expect(screen.getByText(/Notifications: Reconnecting.../)).toBeInTheDocument()
  })
})

describe('ConnectionStatusBadge', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('shows connected badge', () => {
    mockUseSignalRContext.mockReturnValue({
      isConnected: true,
      isReconnecting: false,
      isConnecting: false,
    } as signalRProvider.SignalRContextValue)

    render(<ConnectionStatusBadge />)

    expect(screen.getByText('Connected')).toBeInTheDocument()
  })

  it('shows reconnecting badge', () => {
    mockUseSignalRContext.mockReturnValue({
      isConnected: false,
      isReconnecting: true,
      isConnecting: false,
    } as signalRProvider.SignalRContextValue)

    render(<ConnectionStatusBadge />)

    expect(screen.getByText('Reconnecting...')).toBeInTheDocument()
  })
})

describe('ReconnectionBanner', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('does not render when connected', () => {
    mockUseSignalRContext.mockReturnValue({
      isConnected: true,
      isReconnecting: false,
      claudeCodeError: undefined,
      notificationError: undefined,
      connect: vi.fn(),
    } as unknown as signalRProvider.SignalRContextValue)

    const { container } = render(<ReconnectionBanner />)

    expect(container.firstChild).toBeNull()
  })

  it('shows reconnecting message when reconnecting', () => {
    mockUseSignalRContext.mockReturnValue({
      isConnected: false,
      isReconnecting: true,
      claudeCodeError: undefined,
      notificationError: undefined,
      connect: vi.fn(),
    } as unknown as signalRProvider.SignalRContextValue)

    render(<ReconnectionBanner />)

    expect(screen.getByText(/Attempting to reconnect/)).toBeInTheDocument()
  })

  it('shows disconnected message with retry button when disconnected', () => {
    mockUseSignalRContext.mockReturnValue({
      isConnected: false,
      isReconnecting: false,
      claudeCodeError: undefined,
      notificationError: undefined,
      connect: vi.fn(),
    } as unknown as signalRProvider.SignalRContextValue)

    render(<ReconnectionBanner />)

    expect(screen.getByText('Disconnected from server')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Retry' })).toBeInTheDocument()
  })

  it('shows error message when available', () => {
    mockUseSignalRContext.mockReturnValue({
      isConnected: false,
      isReconnecting: false,
      claudeCodeError: 'Network error',
      notificationError: undefined,
      connect: vi.fn(),
    } as unknown as signalRProvider.SignalRContextValue)

    render(<ReconnectionBanner />)

    expect(screen.getByText('(Network error)')).toBeInTheDocument()
  })

  it('calls connect when retry button is clicked', async () => {
    const user = userEvent.setup()
    const mockConnect = vi.fn()

    mockUseSignalRContext.mockReturnValue({
      isConnected: false,
      isReconnecting: false,
      claudeCodeError: undefined,
      notificationError: undefined,
      connect: mockConnect,
    } as unknown as signalRProvider.SignalRContextValue)

    render(<ReconnectionBanner />)

    await user.click(screen.getByRole('button', { name: 'Retry' }))

    expect(mockConnect).toHaveBeenCalled()
  })
})
