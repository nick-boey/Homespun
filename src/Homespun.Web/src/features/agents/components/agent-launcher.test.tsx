import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AgentLauncher } from './agent-launcher'
import { AgentPrompts, Sessions } from '@/api'
import type { ReactNode } from 'react'
import type { AgentPrompt, ClaudeSession } from '@/api/generated/types.gen'

vi.mock('@/api', () => ({
  AgentPrompts: {
    getApiAgentPromptsAvailableForProjectByProjectId: vi.fn(),
  },
  Sessions: {
    postApiSessions: vi.fn(),
  },
}))

const mockGetAgentPrompts = vi.mocked(AgentPrompts.getApiAgentPromptsAvailableForProjectByProjectId)
const mockPostApiSessions = vi.mocked(Sessions.postApiSessions)

// Helper to create mock API response
function createMockResponse<T>(data: T) {
  return {
    data,
    error: undefined,
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

const mockPrompts: AgentPrompt[] = [
  {
    id: 'prompt-1',
    name: 'Build Feature',
    initialMessage: 'Build the feature',
    mode: 1 as const, // Build
  },
  {
    id: 'prompt-2',
    name: 'Plan Task',
    initialMessage: 'Create a plan',
    mode: 0 as const, // Plan
  },
]

function createMockSession(overrides: Partial<ClaudeSession> = {}): ClaudeSession {
  return {
    id: 'session-123',
    entityId: 'issue-456',
    projectId: 'project-123',
    workingDirectory: '/workdir',
    model: 'claude-sonnet-4-20250514',
    mode: 1 as const,
    status: 0 as const,
    ...overrides,
  }
}

describe('AgentLauncher', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockGetAgentPrompts.mockResolvedValue(createMockResponse(mockPrompts))
  })

  it('renders prompt selector dropdown', async () => {
    render(
      <AgentLauncher projectId="project-123" entityId="issue-456" workingDirectory="/workdir" />,
      { wrapper: createWrapper() }
    )

    await waitFor(() => {
      expect(screen.getByRole('combobox', { name: /prompt/i })).toBeInTheDocument()
    })
  })

  it('renders model selector dropdown', async () => {
    render(
      <AgentLauncher projectId="project-123" entityId="issue-456" workingDirectory="/workdir" />,
      { wrapper: createWrapper() }
    )

    await waitFor(() => {
      expect(screen.getByRole('combobox', { name: /model/i })).toBeInTheDocument()
    })
  })

  it('renders start button', async () => {
    render(
      <AgentLauncher projectId="project-123" entityId="issue-456" workingDirectory="/workdir" />,
      { wrapper: createWrapper() }
    )

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /start agent/i })).toBeInTheDocument()
    })
  })

  it('calls onStart callback when agent is started successfully', async () => {
    const user = userEvent.setup()
    const onStart = vi.fn()

    mockPostApiSessions.mockResolvedValueOnce(createMockResponse(createMockSession()))

    render(
      <AgentLauncher
        projectId="project-123"
        entityId="issue-456"
        workingDirectory="/workdir"
        onStart={onStart}
      />,
      { wrapper: createWrapper() }
    )

    // Wait for prompts to load
    await waitFor(() => {
      expect(screen.getByRole('combobox', { name: /prompt/i })).toBeInTheDocument()
    })

    // Click start button
    await user.click(screen.getByRole('button', { name: /start agent/i }))

    await waitFor(() => {
      expect(onStart).toHaveBeenCalled()
    })
  })

  it('shows loader while starting agent', async () => {
    const user = userEvent.setup()

    // Create a delayed promise that we can control
    let resolvePromise: (value: ReturnType<typeof createMockResponse>) => void
    const delayedPromise = new Promise<ReturnType<typeof createMockResponse>>((resolve) => {
      resolvePromise = resolve
    })

    mockPostApiSessions.mockReturnValue(delayedPromise as never)

    render(
      <AgentLauncher projectId="project-123" entityId="issue-456" workingDirectory="/workdir" />,
      { wrapper: createWrapper() }
    )

    // Wait for prompts to load
    await waitFor(() => {
      expect(screen.getByRole('combobox', { name: /prompt/i })).toBeInTheDocument()
    })

    // Click start button
    await user.click(screen.getByRole('button', { name: /start agent/i }))

    // Should show loading state
    expect(screen.getByRole('button', { name: /start agent/i })).toBeDisabled()

    // Clean up by resolving the promise
    resolvePromise!(createMockResponse(createMockSession()))
  })

  it('persists model selection in localStorage', async () => {
    // Testing localStorage persistence by checking the initial state is more reliable
    // since Radix UI's Select has complex interaction patterns in tests.

    // Pre-set the localStorage value
    localStorage.setItem('agent-launcher-model', 'claude-opus-4-20250514')

    render(
      <AgentLauncher projectId="project-123" entityId="issue-456" workingDirectory="/workdir" />,
      { wrapper: createWrapper() }
    )

    // Wait for component to load
    await waitFor(() => {
      expect(screen.getByRole('combobox', { name: /model/i })).toBeInTheDocument()
    })

    // The model selector should show Opus since we pre-set localStorage
    const modelSelector = screen.getByRole('combobox', { name: /model/i })
    expect(modelSelector).toHaveTextContent(/opus/i)
  })
})
