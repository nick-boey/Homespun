import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ActiveAgentsIndicator } from './active-agents-indicator'
import { Sessions, SessionMode, ClaudeSessionStatus } from '@/api'
import type { ReactNode } from 'react'
import type { SessionSummary } from '@/api/generated/types.gen'

vi.mock('@/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api')>()
  return {
    ...actual,
    Sessions: {
      getApiSessionsProjectByProjectId: vi.fn(),
      getApiSessions: vi.fn(),
    },
  }
})

// Mock TanStack Router Link component
vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual('@tanstack/react-router')
  return {
    ...actual,
    useNavigate: () => vi.fn(),
    Link: ({ children, to }: { children: ReactNode; to: string }) => <a href={to}>{children}</a>,
  }
})

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
    model: 'sonnet',
    mode: SessionMode.BUILD,
    status: ClaudeSessionStatus.RUNNING,
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

  describe('status indicators', () => {
    it('shows multiple status indicators for different agent states', async () => {
      mockGetAllSessions.mockResolvedValueOnce(
        createMockResponse([
          createSessionSummary({ id: 'session-1', status: ClaudeSessionStatus.RUNNING }),
          createSessionSummary({ id: 'session-2', status: ClaudeSessionStatus.WAITING_FOR_INPUT }),
          createSessionSummary({
            id: 'session-3',
            status: ClaudeSessionStatus.WAITING_FOR_QUESTION_ANSWER,
          }),
          createSessionSummary({
            id: 'session-4',
            status: ClaudeSessionStatus.WAITING_FOR_PLAN_EXECUTION,
          }),
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
          createSessionSummary({ id: 'session-1', status: ClaudeSessionStatus.RUNNING }),
          createSessionSummary({ id: 'session-2', status: ClaudeSessionStatus.RUNNING }),
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
          createSessionSummary({ id: 'session-1', status: ClaudeSessionStatus.ERROR }),
          createSessionSummary({ id: 'session-2', status: ClaudeSessionStatus.RUNNING }),
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
          createSessionSummary({ id: 'session-1', status: ClaudeSessionStatus.RUNNING }),
          createSessionSummary({ id: 'session-2', status: ClaudeSessionStatus.WAITING_FOR_INPUT }),
          createSessionSummary({
            id: 'session-3',
            status: ClaudeSessionStatus.WAITING_FOR_QUESTION_ANSWER,
          }),
          createSessionSummary({
            id: 'session-4',
            status: ClaudeSessionStatus.WAITING_FOR_PLAN_EXECUTION,
          }),
          createSessionSummary({ id: 'session-5', status: ClaudeSessionStatus.ERROR }),
        ])
      )

      render(<ActiveAgentsIndicator />, { wrapper: createWrapper() })

      await waitFor(() => {
        // Check count text color as a proxy for status color
        expect(screen.getByTestId('status-working-count')).toHaveClass('text-green-500')
        expect(screen.getByTestId('status-waiting-input-count')).toHaveClass('text-yellow-500')
        expect(screen.getByTestId('status-waiting-answer-count')).toHaveClass('text-purple-500')
        expect(screen.getByTestId('status-waiting-plan-count')).toHaveClass('text-orange-500')
        expect(screen.getByTestId('status-error-count')).toHaveClass('text-red-500')
      })
    })

    it('shows pinging animation only for working status', async () => {
      mockGetAllSessions.mockResolvedValueOnce(
        createMockResponse([
          createSessionSummary({ id: 'session-1', status: ClaudeSessionStatus.RUNNING }),
          createSessionSummary({ id: 'session-2', status: ClaudeSessionStatus.WAITING_FOR_INPUT }),
          createSessionSummary({ id: 'session-3', status: ClaudeSessionStatus.ERROR }),
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
        createMockResponse([
          createSessionSummary({ id: 'session-1', status: ClaudeSessionStatus.RUNNING }),
        ])
      )

      render(<ActiveAgentsIndicator />, { wrapper: createWrapper() })

      await waitFor(() => {
        const link = screen.getByRole('link')
        expect(link).toHaveAttribute('href', '/sessions')
      })
    })

    it('renders nothing when no sessions exist globally', async () => {
      mockGetAllSessions.mockResolvedValueOnce(createMockResponse<SessionSummary[]>([]))

      const { container } = render(<ActiveAgentsIndicator />, { wrapper: createWrapper() })

      await waitFor(() => {
        expect(container.firstChild).toBeNull()
      })
    })
  })
})
