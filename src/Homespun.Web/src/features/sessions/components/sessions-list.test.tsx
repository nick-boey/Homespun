import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement, type ReactNode } from 'react'
import { Sessions } from '@/api'
import type { SessionSummary, ClaudeSessionStatus, SessionMode } from '@/api/generated'
import { SessionsList } from './sessions-list'

vi.mock('@/api', () => ({
  Sessions: {
    getApiSessions: vi.fn(),
    deleteApiSessionsById: vi.fn(),
  },
}))

vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    params,
    ...props
  }: {
    children: React.ReactNode
    to: string
    params?: Record<string, string>
  }) => {
    let href = to
    if (params) {
      for (const [key, value] of Object.entries(params)) {
        href = href.replace(`$${key}`, value)
      }
    }
    return createElement('a', { href, ...props }, children)
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
  entityId: 'issue-abc123',
  projectId: 'project-1',
  model: 'claude-sonnet-4-20250514',
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
  entityId: 'issue-def456',
  projectId: 'project-2',
  model: 'claude-opus-4-20250514',
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
  entityId: 'issue-ghi789',
  projectId: 'project-1',
  model: 'claude-sonnet-4-20250514',
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
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: queryClient }, children)
}

describe('SessionsList', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('shows loading skeletons while fetching', async () => {
    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockReturnValue(new Promise(() => {})) // Never resolves

    render(<SessionsList />, { wrapper: createWrapper() })

    expect(screen.getAllByTestId('session-row-skeleton')).toHaveLength(3)
  })

  it('displays active session in active tab when loaded', async () => {
    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockResolvedValueOnce({ data: mockSessions })

    render(<SessionsList />, { wrapper: createWrapper() })

    // Active tab is shown by default
    await waitFor(() => {
      expect(screen.getByText('session-1')).toBeInTheDocument()
    })

    // Archived sessions should not be visible in active tab
    expect(screen.queryByText('session-2')).not.toBeInTheDocument()
    expect(screen.queryByText('session-3')).not.toBeInTheDocument()
  })

  it('displays archived sessions in archived tab', async () => {
    const user = userEvent.setup()
    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockResolvedValueOnce({ data: mockSessions })

    render(<SessionsList />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('session-1')).toBeInTheDocument()
    })

    // Click archived tab
    const archivedTab = screen.getByRole('tab', { name: /archived/i })
    await user.click(archivedTab)

    // Now we should see archived sessions
    await waitFor(() => {
      expect(screen.getByText('session-2')).toBeInTheDocument()
    })
    expect(screen.getByText('session-3')).toBeInTheDocument()

    // Active session should not be visible
    expect(screen.queryByText('session-1')).not.toBeInTheDocument()
  })

  it('displays empty state when no sessions', async () => {
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

    expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument()
  })

  it('shows session status badge for Running session', async () => {
    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockResolvedValueOnce({ data: [activeSession] })

    render(<SessionsList />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('Running')).toBeInTheDocument()
    })
  })

  it('shows session status badges in archived tab', async () => {
    const user = userEvent.setup()
    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockResolvedValueOnce({ data: mockSessions })

    render(<SessionsList />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('session-1')).toBeInTheDocument()
    })

    // Switch to archived tab
    const archivedTab = screen.getByRole('tab', { name: /archived/i })
    await user.click(archivedTab)

    await waitFor(() => {
      expect(screen.getByText('Stopped')).toBeInTheDocument()
    })
    expect(screen.getByText('Error')).toBeInTheDocument()
  })

  it('navigates to session on row click', async () => {
    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockResolvedValueOnce({ data: [activeSession] })

    render(<SessionsList />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('session-1')).toBeInTheDocument()
    })

    // Check that session rows are links
    const sessionLink = screen.getByRole('link', { name: /session-1/i })
    expect(sessionLink).toHaveAttribute('href', '/sessions/session-1')
  })

  it('shows stop action for active sessions', async () => {
    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockResolvedValueOnce({ data: [activeSession] })

    render(<SessionsList />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('session-1')).toBeInTheDocument()
    })

    // Active sessions should have a stop button
    expect(screen.getByRole('button', { name: /stop/i })).toBeInTheDocument()
  })

  it('does not show stop action for archived sessions', async () => {
    const user = userEvent.setup()
    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockResolvedValueOnce({ data: mockSessions })

    render(<SessionsList />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('session-1')).toBeInTheDocument()
    })

    // Switch to archived tab
    const archivedTab = screen.getByRole('tab', { name: /archived/i })
    await user.click(archivedTab)

    await waitFor(() => {
      expect(screen.getByText('session-2')).toBeInTheDocument()
    })

    // Archived sessions should not have stop button
    const table = screen.getByRole('table')
    const stopButtons = within(table).queryAllByRole('button', { name: /stop/i })
    expect(stopButtons).toHaveLength(0)
  })

  it('shows correct tab counts', async () => {
    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockResolvedValueOnce({ data: mockSessions })

    render(<SessionsList />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByRole('tab', { name: /active \(1\)/i })).toBeInTheDocument()
    })
    expect(screen.getByRole('tab', { name: /archived \(2\)/i })).toBeInTheDocument()
  })

  it('shows confirmation dialog when stopping session', async () => {
    const user = userEvent.setup()
    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockResolvedValueOnce({ data: [activeSession] })

    render(<SessionsList />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('session-1')).toBeInTheDocument()
    })

    // Click stop button
    const stopButton = screen.getByRole('button', { name: /stop/i })
    await user.click(stopButton)

    // Confirmation dialog should appear
    await waitFor(() => {
      expect(screen.getByRole('alertdialog')).toBeInTheDocument()
    })
    expect(screen.getByRole('heading', { name: 'Stop Session' })).toBeInTheDocument()
    expect(screen.getByText(/are you sure you want to stop this session/i)).toBeInTheDocument()
  })

  it('calls stop mutation when confirmed', async () => {
    const user = userEvent.setup()
    const mockGetApiSessions = Sessions.getApiSessions as Mock
    const mockDeleteApiSessionsById = Sessions.deleteApiSessionsById as Mock
    mockGetApiSessions.mockResolvedValueOnce({ data: [activeSession] })
    mockDeleteApiSessionsById.mockResolvedValueOnce({})

    render(<SessionsList />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('session-1')).toBeInTheDocument()
    })

    // Click stop button
    const stopButton = screen.getByRole('button', { name: /stop/i })
    await user.click(stopButton)

    // Click confirm in dialog
    await waitFor(() => {
      expect(screen.getByRole('alertdialog')).toBeInTheDocument()
    })
    const confirmButton = screen.getByRole('button', { name: /stop session/i })
    await user.click(confirmButton)

    await waitFor(() => {
      expect(mockDeleteApiSessionsById).toHaveBeenCalledWith({
        path: { id: 'session-1' },
      })
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
})
