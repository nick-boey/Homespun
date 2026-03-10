import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AgentControlPanel } from './agent-control-panel'
import { Sessions } from '@/api'
import type { ReactNode } from 'react'
import type { ClaudeSession } from '@/api/generated/types.gen'

vi.mock('@/api', () => ({
  Sessions: {
    deleteApiSessionsById: vi.fn(),
    postApiSessionsByIdInterrupt: vi.fn(),
  },
}))

const mockDeleteSession = vi.mocked(Sessions.deleteApiSessionsById)
const mockInterruptSession = vi.mocked(Sessions.postApiSessionsByIdInterrupt)

// Helper to create mock API response
function createMockResponse<T>(data: T) {
  return {
    data,
    request: new Request('http://localhost/api/test'),
    response: new Response(),
  }
}

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  })
  return function Wrapper({ children }: { children: ReactNode }) {
    return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  }
}

const mockSession: ClaudeSession = {
  id: 'session-123',
  entityId: 'issue-456',
  projectId: 'project-789',
  model: 'sonnet',
  mode: 1 as const, // Build
  status: 2 as const, // Running
  workingDirectory: '/workdir',
  createdAt: new Date().toISOString(),
  totalCostUsd: 0.05,
  totalDurationMs: 120000,
}

describe('AgentControlPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('displays session information', () => {
    render(<AgentControlPanel session={mockSession} />, { wrapper: createWrapper() })

    expect(screen.getByText(/sonnet/i)).toBeInTheDocument()
    expect(screen.getByText(/build/i)).toBeInTheDocument()
  })

  it('shows stop button when session is running', () => {
    render(<AgentControlPanel session={mockSession} />, { wrapper: createWrapper() })

    // There may be multiple stop buttons (ThinkingBar + control button)
    const stopButtons = screen.getAllByRole('button', { name: /stop/i })
    expect(stopButtons.length).toBeGreaterThan(0)
  })

  it('calls onStop when stop button is clicked', async () => {
    const user = userEvent.setup()
    const onStop = vi.fn()

    mockDeleteSession.mockResolvedValueOnce(createMockResponse(undefined))

    render(<AgentControlPanel session={mockSession} onStop={onStop} />, {
      wrapper: createWrapper(),
    })

    // Get all stop buttons and click the one from the control panel (variant="destructive")
    const stopButtons = screen.getAllByRole('button', { name: /stop/i })
    // Click the destructive button (control panel stop button)
    const controlPanelStopButton =
      stopButtons.find((btn) => btn.getAttribute('data-variant') === 'destructive') ??
      stopButtons[0]
    await user.click(controlPanelStopButton)

    await waitFor(() => {
      expect(mockDeleteSession).toHaveBeenCalledWith({
        path: { id: 'session-123' },
      })
    })

    await waitFor(() => {
      expect(onStop).toHaveBeenCalled()
    })
  })

  it('shows pause button when session is running', () => {
    render(<AgentControlPanel session={mockSession} />, { wrapper: createWrapper() })

    expect(screen.getByRole('button', { name: /pause/i })).toBeInTheDocument()
  })

  it('calls interrupt API when pause button is clicked', async () => {
    const user = userEvent.setup()

    mockInterruptSession.mockResolvedValueOnce(createMockResponse(undefined))

    render(<AgentControlPanel session={mockSession} />, { wrapper: createWrapper() })

    await user.click(screen.getByRole('button', { name: /pause/i }))

    await waitFor(() => {
      expect(mockInterruptSession).toHaveBeenCalledWith({
        path: { id: 'session-123' },
      })
    })
  })

  it('displays token usage statistics', () => {
    render(<AgentControlPanel session={mockSession} />, { wrapper: createWrapper() })

    // Check for cost display
    expect(screen.getByText(/\$0\.05/)).toBeInTheDocument()
  })

  it('shows ThinkingBar for running status', () => {
    render(<AgentControlPanel session={mockSession} />, { wrapper: createWrapper() })

    // ThinkingBar shows "Working" text
    expect(screen.getByText(/working/i)).toBeInTheDocument()
  })

  it('hides control buttons when session is stopped', () => {
    const stoppedSession: ClaudeSession = { ...mockSession, status: 5 as const }

    render(<AgentControlPanel session={stoppedSession} />, { wrapper: createWrapper() })

    expect(screen.queryByRole('button', { name: /stop/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /pause/i })).not.toBeInTheDocument()
  })
})
