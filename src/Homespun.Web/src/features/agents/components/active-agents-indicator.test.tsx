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
})
