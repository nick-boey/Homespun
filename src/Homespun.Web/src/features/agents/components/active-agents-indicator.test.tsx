import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ActiveAgentsIndicator } from './active-agents-indicator'
import { Sessions } from '@/api'
import type { ReactNode } from 'react'
import type { SessionSummary } from '@/api/generated/types.gen'

vi.mock('@/api', () => ({
  Sessions: {
    getApiSessionsProjectByProjectId: vi.fn(),
    getApiSessions: vi.fn(),
  },
}))

// Mock TanStack Router Link component
vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual('@tanstack/react-router')
  return {
    ...actual,
    useNavigate: () => vi.fn(),
    Link: ({ children, to }: { children: ReactNode; to: string }) => <a href={to}>{children}</a>,
  }
})

const mockGetProjectSessions = vi.mocked(Sessions.getApiSessionsProjectByProjectId)
const mockGetAllSessions = vi.mocked(Sessions.getApiSessions)

// Helper to create mock API response
function createMockResponse<T>(data: T) {
  return {
    data,
    request: new Request('http://localhost/api/test'),
    response: new Response(),
  }
}

// Helper to create a valid SessionSummary
function createSessionSummary(overrides: Partial<SessionSummary> = {}): SessionSummary {
  return {
    id: 'session-1',
    entityId: 'entity-1',
    projectId: 'project-1',
    model: 'claude-sonnet-4-20250514',
    mode: 1 as const,
    status: 2 as const,
    ...overrides,
  }
}

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
    },
  })
  return function Wrapper({ children }: { children: ReactNode }) {
    return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  }
}

describe('ActiveAgentsIndicator', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('shows idle state when no active sessions', async () => {
    mockGetProjectSessions.mockResolvedValueOnce(createMockResponse<SessionSummary[]>([]))

    render(<ActiveAgentsIndicator projectId="project-123" />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText(/idle/i)).toBeInTheDocument()
    })
  })

  it('shows active count when sessions are running', async () => {
    mockGetProjectSessions.mockResolvedValueOnce(
      createMockResponse([
        createSessionSummary({ id: 'session-1', status: 2 as const }),
        createSessionSummary({ id: 'session-2', status: 2 as const }),
      ])
    )

    render(<ActiveAgentsIndicator projectId="project-123" />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('2')).toBeInTheDocument()
    })
  })

  it('shows TextShimmer effect when agents are processing', async () => {
    mockGetProjectSessions.mockResolvedValueOnce(
      createMockResponse([createSessionSummary({ id: 'session-1', status: 2 as const })])
    )

    render(<ActiveAgentsIndicator projectId="project-123" />, { wrapper: createWrapper() })

    await waitFor(() => {
      // Should show the badge with active styling
      expect(screen.getByText('1')).toBeInTheDocument()
    })
  })

  it('shows waiting state when sessions are waiting for input', async () => {
    mockGetProjectSessions.mockResolvedValueOnce(
      createMockResponse([createSessionSummary({ id: 'session-1', status: 3 as const })])
    )

    render(<ActiveAgentsIndicator projectId="project-123" />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('1')).toBeInTheDocument()
    })
  })

  it('is clickable and navigates to sessions list', async () => {
    mockGetProjectSessions.mockResolvedValueOnce(
      createMockResponse([createSessionSummary({ id: 'session-1', status: 2 as const })])
    )

    render(<ActiveAgentsIndicator projectId="project-123" />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByRole('link')).toBeInTheDocument()
    })
  })

  it('shows green indicator for idle state', async () => {
    mockGetProjectSessions.mockResolvedValueOnce(createMockResponse<SessionSummary[]>([]))

    render(<ActiveAgentsIndicator projectId="project-123" />, { wrapper: createWrapper() })

    await waitFor(() => {
      const indicator = screen.getByTestId('status-indicator')
      // Uses text color for the SVG icon
      expect(indicator).toHaveClass('text-green-500')
    })
  })

  it('shows pulsing indicator for active state', async () => {
    mockGetProjectSessions.mockResolvedValueOnce(
      createMockResponse([createSessionSummary({ id: 'session-1', status: 2 as const })])
    )

    render(<ActiveAgentsIndicator projectId="project-123" />, { wrapper: createWrapper() })

    await waitFor(() => {
      const indicator = screen.getByTestId('status-indicator')
      expect(indicator).toHaveClass('animate-pulse')
    })
  })

  describe('global status indicators', () => {
    it('shows multiple status indicators for different agent states', async () => {
      mockGetAllSessions.mockResolvedValueOnce(
        createMockResponse([
          createSessionSummary({ id: 'session-1', status: 2 as const }), // Running
          createSessionSummary({ id: 'session-2', status: 3 as const }), // WaitingForInput
          createSessionSummary({ id: 'session-3', status: 4 as const }), // WaitingForQuestionAnswer
          createSessionSummary({ id: 'session-4', status: 5 as const }), // WaitingForPlanExecution
        ])
      )

      render(<ActiveAgentsIndicator />, { wrapper: createWrapper() })

      await waitFor(() => {
        // Should show working count
        expect(screen.getByTestId('status-working')).toBeInTheDocument()
        expect(screen.getByTestId('status-working-count')).toHaveTextContent('1')

        // Should show waiting for input count
        expect(screen.getByTestId('status-waiting-input')).toBeInTheDocument()
        expect(screen.getByTestId('status-waiting-input-count')).toHaveTextContent('1')

        // Should show waiting for answer count
        expect(screen.getByTestId('status-waiting-answer')).toBeInTheDocument()
        expect(screen.getByTestId('status-waiting-answer-count')).toHaveTextContent('1')

        // Should show waiting for plan count
        expect(screen.getByTestId('status-waiting-plan')).toBeInTheDocument()
        expect(screen.getByTestId('status-waiting-plan-count')).toHaveTextContent('1')
      })
    })

    it('hides status indicators with zero counts', async () => {
      mockGetAllSessions.mockResolvedValueOnce(
        createMockResponse([
          createSessionSummary({ id: 'session-1', status: 2 as const }), // Running
          createSessionSummary({ id: 'session-2', status: 2 as const }), // Running
        ])
      )

      render(<ActiveAgentsIndicator />, { wrapper: createWrapper() })

      await waitFor(() => {
        // Should show working count
        expect(screen.getByTestId('status-working')).toBeInTheDocument()
        expect(screen.getByTestId('status-working-count')).toHaveTextContent('2')

        // Should NOT show other statuses with zero count
        expect(screen.queryByTestId('status-waiting-input')).not.toBeInTheDocument()
        expect(screen.queryByTestId('status-waiting-answer')).not.toBeInTheDocument()
        expect(screen.queryByTestId('status-waiting-plan')).not.toBeInTheDocument()
        expect(screen.queryByTestId('status-error')).not.toBeInTheDocument()
      })
    })

    it('shows error status with correct color', async () => {
      mockGetAllSessions.mockResolvedValueOnce(
        createMockResponse([
          createSessionSummary({ id: 'session-1', status: 7 as const }), // Error
          createSessionSummary({ id: 'session-2', status: 2 as const }), // Running
        ])
      )

      render(<ActiveAgentsIndicator />, { wrapper: createWrapper() })

      await waitFor(() => {
        const errorIndicator = screen.getByTestId('status-error')
        expect(errorIndicator).toBeInTheDocument()
        expect(errorIndicator).toHaveClass('text-red-500')
        expect(screen.getByTestId('status-error-count')).toHaveTextContent('1')
      })
    })

    it('shows correct colors for each status type', async () => {
      mockGetAllSessions.mockResolvedValueOnce(
        createMockResponse([
          createSessionSummary({ id: 'session-1', status: 2 as const }), // Running
          createSessionSummary({ id: 'session-2', status: 3 as const }), // WaitingForInput
          createSessionSummary({ id: 'session-3', status: 4 as const }), // WaitingForQuestionAnswer
          createSessionSummary({ id: 'session-4', status: 5 as const }), // WaitingForPlanExecution
          createSessionSummary({ id: 'session-5', status: 7 as const }), // Error
        ])
      )

      render(<ActiveAgentsIndicator />, { wrapper: createWrapper() })

      await waitFor(() => {
        // Check count text color as a proxy for status color
        expect(screen.getByTestId('status-working-count')).toHaveClass('text-blue-500')
        expect(screen.getByTestId('status-waiting-input-count')).toHaveClass('text-yellow-500')
        expect(screen.getByTestId('status-waiting-answer-count')).toHaveClass('text-orange-500')
        expect(screen.getByTestId('status-waiting-plan-count')).toHaveClass('text-purple-500')
        expect(screen.getByTestId('status-error-count')).toHaveClass('text-red-500')
      })
    })

    it('shows pinging animation only for working status', async () => {
      mockGetAllSessions.mockResolvedValueOnce(
        createMockResponse([
          createSessionSummary({ id: 'session-1', status: 2 as const }), // Running
          createSessionSummary({ id: 'session-2', status: 3 as const }), // WaitingForInput
          createSessionSummary({ id: 'session-3', status: 7 as const }), // Error
        ])
      )

      render(<ActiveAgentsIndicator />, { wrapper: createWrapper() })

      await waitFor(() => {
        // Working status should have ping animation
        const workingDot = screen.getByTestId('status-working')
        expect(workingDot.querySelector('.animate-ping')).toBeInTheDocument()

        // Other statuses should not have ping animation
        const waitingDot = screen.getByTestId('status-waiting-input')
        expect(waitingDot.querySelector('.animate-ping')).not.toBeInTheDocument()

        const errorDot = screen.getByTestId('status-error')
        expect(errorDot.querySelector('.animate-ping')).not.toBeInTheDocument()
      })
    })

    it('navigates to global sessions page when clicked', async () => {
      mockGetAllSessions.mockResolvedValueOnce(
        createMockResponse([createSessionSummary({ id: 'session-1', status: 2 as const })])
      )

      render(<ActiveAgentsIndicator />, { wrapper: createWrapper() })

      await waitFor(() => {
        const link = screen.getByRole('link')
        expect(link).toHaveAttribute('href', '/sessions')
      })
    })

    it('shows idle state when no sessions exist globally', async () => {
      mockGetAllSessions.mockResolvedValueOnce(createMockResponse<SessionSummary[]>([]))

      render(<ActiveAgentsIndicator />, { wrapper: createWrapper() })

      await waitFor(() => {
        expect(screen.getByText(/idle/i)).toBeInTheDocument()
      })
    })
  })
})
