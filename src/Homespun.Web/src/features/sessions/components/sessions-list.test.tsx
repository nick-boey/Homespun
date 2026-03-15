import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement, type ReactNode } from 'react'
import { Issues, PullRequests } from '@/api'
import type { SessionSummary } from '@/api/generated/types.gen'
import { ClaudeSessionStatus, SessionMode } from '@/api/generated/types.gen'
import { SessionsList } from './sessions-list'
import { useSessions, useStopSession } from '../hooks/use-sessions'
import { useProjects } from '@/features/projects'

vi.mock('@/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api')>()
  return {
    ...actual,
    Sessions: {
      getApiSessions: vi.fn(),
      deleteApiSessionsById: vi.fn(),
    },
    Issues: {
      getApiIssuesByIssueId: vi.fn(),
    },
    PullRequests: {
      getApiPullRequestsById: vi.fn(),
    },
  }
})

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

vi.mock('../hooks/use-sessions', () => ({
  ...vi.importActual('../hooks/use-sessions'),
  useSessions: vi.fn(),
  useStopSession: vi.fn(() => ({
    mutate: vi.fn(),
    isPending: false,
  })),
  sessionsQueryKey: ['sessions'],
}))

vi.mock('@/features/projects', () => ({
  useProjects: vi.fn(),
}))

// Active session (Running status)
const activeSession: SessionSummary = {
  id: 'session-1',
  entityId: 'issue-abc123',
  projectId: 'project-1',
  model: 'sonnet',
  mode: SessionMode.BUILD,
  status: ClaudeSessionStatus.RUNNING,
  createdAt: '2024-01-01T10:00:00Z',
  lastActivityAt: '2024-01-01T10:30:00Z',
  messageCount: 15,
  totalCostUsd: 0.25,
}

// Archived sessions (Stopped, Error)
const stoppedSession: SessionSummary = {
  id: 'session-2',
  entityId: 'issue-def456',
  projectId: 'project-2',
  model: 'opus',
  mode: SessionMode.PLAN,
  status: ClaudeSessionStatus.STOPPED,
  createdAt: '2024-01-01T09:00:00Z',
  lastActivityAt: '2024-01-01T09:15:00Z',
  messageCount: 5,
  totalCostUsd: 0.1,
}

const errorSession: SessionSummary = {
  id: 'session-3',
  entityId: 'issue-ghi789',
  projectId: 'project-1',
  model: 'sonnet',
  mode: SessionMode.BUILD,
  status: ClaudeSessionStatus.ERROR,
  createdAt: '2024-01-02T08:00:00Z',
  lastActivityAt: '2024-01-02T08:05:00Z',
  messageCount: 2,
  totalCostUsd: 0.05,
}

const mockSessionSummaries: SessionSummary[] = [activeSession, stoppedSession, errorSession]

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

    // Setup default mocks
    vi.mocked(useSessions).mockReturnValue({
      data: mockSessionSummaries,
      isLoading: false,
      isError: false,
      error: null,
      refetch: vi.fn(),
    } as unknown as ReturnType<typeof useSessions>)

    vi.mocked(useProjects).mockReturnValue({
      data: [
        { id: 'project-1', name: 'Project One' },
        { id: 'project-2', name: 'Project Two' },
      ],
      isLoading: false,
      isError: false,
      error: null,
      refetch: vi.fn(),
    } as unknown as ReturnType<typeof useProjects>)

    // Setup API mocks for entity info
    vi.mocked(Issues.getApiIssuesByIssueId).mockImplementation(({ path }) => {
      const mockIssues: Record<string, { id: string; title: string }> = {
        'issue-abc123': { id: 'issue-abc123', title: 'Fix login bug' },
        'issue-def456': { id: 'issue-def456', title: 'Update documentation' },
        'issue-ghi789': { id: 'issue-ghi789', title: 'Refactor auth module' },
      }
      return Promise.resolve({
        data: mockIssues[path.issueId] || null,
        error: undefined,
        request: new Request('http://test'),
        response: new Response(),
      })
    })

    vi.mocked(PullRequests.getApiPullRequestsById).mockResolvedValue({
      data: { id: 'pr-456', title: 'Add new feature' },
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    } as Awaited<ReturnType<typeof PullRequests.getApiPullRequestsById>>)
  })

  it('shows loading skeletons while fetching', async () => {
    vi.mocked(useSessions).mockReturnValue({
      data: undefined,
      isLoading: true,
      isError: false,
      error: null,
      refetch: vi.fn(),
    } as unknown as ReturnType<typeof useSessions>)

    render(<SessionsList />, { wrapper: createWrapper() })

    // The component now shows session card skeletons
    const skeletonCards = document.querySelectorAll('[data-slot="card"]')
    expect(skeletonCards.length).toBe(6) // Grid shows 6 skeleton cards
  })

  it('displays active session in active tab when loaded', async () => {
    render(<SessionsList />, { wrapper: createWrapper() })

    // Active tab is shown by default
    await waitFor(() => {
      expect(screen.getByText('Fix login bug')).toBeInTheDocument()
    })

    // Should show "Issue" badge
    expect(screen.getByText('Issue')).toBeInTheDocument()

    // Should show Running status
    expect(screen.getByText('Running')).toBeInTheDocument()

    // Archived sessions should not be visible in active tab
    expect(screen.queryByText('Stopped')).not.toBeInTheDocument()
    expect(screen.queryByText('Error')).not.toBeInTheDocument()
  })

  it('displays archived sessions in archived tab', async () => {
    const user = userEvent.setup()

    render(<SessionsList />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('Fix login bug')).toBeInTheDocument()
    })

    // Click archived tab
    const archivedTab = screen.getByRole('tab', { name: /archived/i })
    await user.click(archivedTab)

    // Now we should see archived sessions with their titles
    await waitFor(() => {
      expect(screen.getByText('Stopped')).toBeInTheDocument()
    })
    expect(screen.getByText('Error')).toBeInTheDocument()

    // Should show entity titles
    await waitFor(() => {
      expect(screen.getByText('Update documentation')).toBeInTheDocument()
    })
    expect(screen.getByText('Refactor auth module')).toBeInTheDocument()

    // Active session should not be visible
    expect(screen.queryByText('Running')).not.toBeInTheDocument()
  })

  it('displays empty state when no sessions', async () => {
    vi.mocked(useSessions).mockReturnValue({
      data: [],
      isLoading: false,
      isError: false,
      error: null,
      refetch: vi.fn(),
    } as unknown as ReturnType<typeof useSessions>)

    render(<SessionsList />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('No sessions yet')).toBeInTheDocument()
    })
  })

  it('displays error state with retry button', async () => {
    vi.mocked(useSessions).mockReturnValue({
      data: undefined,
      isLoading: false,
      isError: true,
      error: new Error('Network error'),
      refetch: vi.fn(),
    } as unknown as ReturnType<typeof useSessions>)

    render(<SessionsList />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('Error loading sessions')).toBeInTheDocument()
    })

    expect(screen.getByRole('button', { name: /retry/i })).toBeInTheDocument()
  })

  it('shows session status badge for Running session', async () => {
    render(<SessionsList />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('Running')).toBeInTheDocument()
    })
  })

  it('shows session status badges in archived tab', async () => {
    const user = userEvent.setup()

    render(<SessionsList />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('Fix login bug')).toBeInTheDocument()
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
    render(<SessionsList />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('Fix login bug')).toBeInTheDocument()
    })

    // Check that chat button is a link
    const chatLink = screen.getByRole('link', { name: /Chat/i })
    expect(chatLink).toHaveAttribute('href', '/sessions/session-1')
  })

  it('shows stop action for active sessions', async () => {
    render(<SessionsList />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('Fix login bug')).toBeInTheDocument()
    })

    // Active sessions should have a stop button
    expect(screen.getByRole('button', { name: /stop/i })).toBeInTheDocument()
  })

  it('does not show stop action for archived sessions', async () => {
    const user = userEvent.setup()

    render(<SessionsList />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('Fix login bug')).toBeInTheDocument()
    })

    // Switch to archived tab
    const archivedTab = screen.getByRole('tab', { name: /archived/i })
    await user.click(archivedTab)

    await waitFor(() => {
      expect(screen.getByText('Stopped')).toBeInTheDocument()
    })

    // Archived sessions should not have stop button
    const stopButtons = screen.queryAllByRole('button', { name: /stop/i })
    expect(stopButtons).toHaveLength(0)
  })

  it('shows correct tab counts', async () => {
    render(<SessionsList />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByRole('tab', { name: /active \(1\)/i })).toBeInTheDocument()
    })
    expect(screen.getByRole('tab', { name: /archived \(2\)/i })).toBeInTheDocument()
  })

  it('shows confirmation dialog when stopping session', async () => {
    const user = userEvent.setup()

    render(<SessionsList />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('Fix login bug')).toBeInTheDocument()
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
    const mockMutate = vi.fn()
    vi.mocked(useStopSession).mockReturnValue({
      mutate: mockMutate,
      isPending: false,
    } as unknown as ReturnType<typeof useStopSession>)

    render(<SessionsList />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('Fix login bug')).toBeInTheDocument()
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
      expect(mockMutate).toHaveBeenCalledWith('session-1')
    })
  })

  it('shows empty state for active tab when only archived sessions exist', async () => {
    vi.mocked(useSessions).mockReturnValue({
      data: [stoppedSession, errorSession],
      isLoading: false,
      isError: false,
      error: null,
      refetch: vi.fn(),
    } as unknown as ReturnType<typeof useSessions>)

    render(<SessionsList />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('No active sessions')).toBeInTheDocument()
    })
  })
})
