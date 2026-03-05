import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement, type ReactNode } from 'react'
import { Sessions } from '@/api'
import type { SessionSummary, ClaudeSessionStatus, SessionMode } from '@/api/generated'
import { SessionsList } from './sessions-list'

// Mock the SessionCard component
vi.mock('./session-card', () => ({
  SessionCard: ({ session }: { session: SessionSummary }) =>
    createElement(
      'div',
      { 'data-testid': 'session-card', 'data-session-id': session.id },
      session.entityId
    ),
}))

// Mock the SessionCardSkeleton component
vi.mock('./session-card-skeleton', () => ({
  SessionCardSkeleton: () => createElement('div', { 'data-testid': 'session-card-skeleton' }),
}))

vi.mock('@/api', () => ({
  Sessions: {
    getApiSessions: vi.fn(),
    deleteApiSessionsById: vi.fn(),
  },
}))

vi.mock('@/providers/signalr-provider', () => ({
  useClaudeCodeHub: vi.fn(() => ({
    connection: null,
    status: 'disconnected',
    error: undefined,
    methods: null,
    isConnected: false,
    isReconnecting: false,
  })),
}))

// Active session (Running status = 2)
const activeSession: SessionSummary = {
  id: 'session-1',
  entityId: 'issue:abc123',
  projectId: 'project-1',
  model: 'claude-sonnet',
  mode: 1 as SessionMode,
  status: 2 as ClaudeSessionStatus, // Running
  createdAt: '2024-01-01T10:00:00Z',
  lastActivityAt: '2024-01-01T10:30:00Z',
  messageCount: 15,
  totalCostUsd: 0.25,
  containerId: 'container-1',
  containerName: 'session-container-1',
}

// Archived sessions (Stopped = 6, Error = 7)
const stoppedSession: SessionSummary = {
  id: 'session-2',
  entityId: 'issue:def456',
  projectId: 'project-2',
  model: 'claude-opus',
  mode: 0 as SessionMode,
  status: 6 as ClaudeSessionStatus, // Stopped
  createdAt: '2024-01-01T09:00:00Z',
  lastActivityAt: '2024-01-01T09:15:00Z',
  messageCount: 5,
  totalCostUsd: 0.1,
  containerId: null,
  containerName: null,
}

const errorSession: SessionSummary = {
  id: 'session-3',
  entityId: 'issue:ghi789',
  projectId: 'project-1',
  model: 'claude-sonnet',
  mode: 1 as SessionMode,
  status: 7 as ClaudeSessionStatus, // Error
  createdAt: '2024-01-02T08:00:00Z',
  lastActivityAt: '2024-01-02T08:05:00Z',
  messageCount: 2,
  totalCostUsd: 0.05,
  containerId: null,
  containerName: null,
}

const mockSessions: SessionSummary[] = [activeSession, stoppedSession, errorSession]

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
    },
  })
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  )
}

describe('SessionsList', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('shows loading skeletons while fetching', async () => {
    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockReturnValue(new Promise(() => {})) // Never resolves

    render(<SessionsList />, { wrapper: createWrapper() })

    expect(screen.getAllByTestId('session-card-skeleton')).toHaveLength(6)
  })

  it('displays active sessions in active tab by default', async () => {
    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockResolvedValueOnce({ data: mockSessions })

    render(<SessionsList />, { wrapper: createWrapper() })

    // Active tab is shown by default
    await waitFor(() => {
      const activeCards = screen.getAllByTestId('session-card')
      expect(activeCards).toHaveLength(1)
      expect(activeCards[0]).toHaveAttribute('data-session-id', 'session-1')
    })
  })

  it('displays archived sessions in archived tab', async () => {
    const user = userEvent.setup()
    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockResolvedValueOnce({ data: mockSessions })

    render(<SessionsList />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByTestId('session-card')).toBeInTheDocument()
    })

    // Switch to archived tab
    await user.click(screen.getByRole('tab', { name: /archived/i }))

    await waitFor(() => {
      const archivedCards = screen.getAllByTestId('session-card')
      expect(archivedCards).toHaveLength(2)
      // Sessions are sorted by lastActivityAt, so session-3 comes first
      expect(archivedCards[0]).toHaveAttribute('data-session-id', 'session-3')
      expect(archivedCards[1]).toHaveAttribute('data-session-id', 'session-2')
    })
  })

  it('displays empty state when no sessions exist', async () => {
    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockResolvedValueOnce({ data: [] })

    render(<SessionsList />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('No sessions yet')).toBeInTheDocument()
    })
  })

  it('displays error state with retry button', async () => {
    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockRejectedValueOnce(new Error('Network error'))

    render(<SessionsList />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('Error loading sessions')).toBeInTheDocument()
    })

    const retryButton = screen.getByRole('button', { name: /retry/i })
    expect(retryButton).toBeInTheDocument()
  })

  it('shows correct tab counts', async () => {
    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockResolvedValueOnce({ data: mockSessions })

    render(<SessionsList />, { wrapper: createWrapper() })

    await waitFor(() => {
      const activeTab = screen.getByRole('tab', { name: /active \(1\)/i })
      const archivedTab = screen.getByRole('tab', { name: /archived \(2\)/i })
      expect(activeTab).toBeInTheDocument()
      expect(archivedTab).toBeInTheDocument()
    })
  })

  it('shows empty state for active tab when only archived sessions exist', async () => {
    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockResolvedValueOnce({ data: [stoppedSession, errorSession] })

    render(<SessionsList />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('No active sessions')).toBeInTheDocument()
    })
  })

  it('shows empty state for archived tab when only active sessions exist', async () => {
    const user = userEvent.setup()
    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockResolvedValueOnce({ data: [activeSession] })

    render(<SessionsList />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByTestId('session-card')).toBeInTheDocument()
    })

    // Switch to archived tab
    await user.click(screen.getByRole('tab', { name: /archived/i }))

    await waitFor(() => {
      expect(screen.getByText('No archived sessions')).toBeInTheDocument()
    })
  })

  // Note: Dropdown interaction test removed due to Radix UI testing limitations
  // The filtering functionality is tested through integration tests

  it('uses responsive grid layout for cards', async () => {
    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockResolvedValueOnce({ data: mockSessions })

    render(<SessionsList />, { wrapper: createWrapper() })

    await waitFor(() => {
      const grid = screen.getByTestId('session-card').parentElement
      expect(grid).toHaveClass('grid', 'gap-4', 'grid-cols-1', 'md:grid-cols-2', 'lg:grid-cols-3')
    })
  })
})
