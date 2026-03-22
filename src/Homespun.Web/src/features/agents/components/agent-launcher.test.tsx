import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AgentLauncher } from './agent-launcher'
import { AgentPrompts, Sessions, SessionMode, ClaudeSessionStatus } from '@/api'
import type { ReactNode } from 'react'
import type { AgentPrompt, ClaudeSession } from '@/api/generated/types.gen'

vi.mock('@/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api')>()
  return {
    ...actual,
    AgentPrompts: {
      getApiAgentPromptsAvailableForProjectByProjectId: vi.fn(),
    },
    Sessions: {
      postApiSessions: vi.fn(),
    },
  }
})

// Mock useNavigate from tanstack router
const mockNavigate = vi.fn()
vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => mockNavigate,
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
    mode: SessionMode.BUILD,
  },
  {
    id: 'prompt-2',
    name: 'Plan Task',
    initialMessage: 'Create a plan',
    mode: SessionMode.PLAN,
  },
]

function createMockSession(overrides: Partial<ClaudeSession> = {}): ClaudeSession {
  return {
    id: 'session-123',
    entityId: 'issue-456',
    projectId: 'project-123',
    workingDirectory: '/workdir',
    model: 'sonnet',
    mode: SessionMode.BUILD,
    status: ClaudeSessionStatus.STARTING,
    ...overrides,
  }
}

describe('AgentLauncher', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockNavigate.mockClear()
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
    localStorage.setItem('agent-launcher-model', 'opus')

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

  it('shows None option as first in dropdown', async () => {
    const user = userEvent.setup()
    render(
      <AgentLauncher projectId="project-123" entityId="issue-456" workingDirectory="/workdir" />,
      { wrapper: createWrapper() }
    )

    // Wait for prompts to load
    await waitFor(() => {
      expect(screen.getByRole('combobox', { name: /prompt/i })).toBeInTheDocument()
    })

    // Open the prompt dropdown
    const promptSelect = screen.getByRole('combobox', { name: /prompt/i })
    await user.click(promptSelect)

    // Should have None as first option
    const options = screen.getAllByRole('option')
    expect(options[0]).toHaveTextContent('None - Start without prompt (Plan mode)')
  })

  it('navigates to session page when None is selected', async () => {
    const user = userEvent.setup()
    mockPostApiSessions.mockResolvedValueOnce(
      createMockResponse(createMockSession({ id: 'session-789' }))
    )

    render(
      <AgentLauncher projectId="project-123" entityId="issue-456" workingDirectory="/workdir" />,
      { wrapper: createWrapper() }
    )

    // Wait for prompts to load
    await waitFor(() => {
      expect(screen.getByRole('combobox', { name: /prompt/i })).toBeInTheDocument()
    })

    // Select None option
    const promptSelect = screen.getByRole('combobox', { name: /prompt/i })
    await user.click(promptSelect)
    // Use getAllByText since the text appears in both the trigger and dropdown
    const noneOptions = screen.getAllByText('None - Start without prompt (Plan mode)')
    // Click the one in the dropdown (not the selected value)
    await user.click(noneOptions[noneOptions.length - 1])

    // Click start button
    await user.click(screen.getByRole('button', { name: /start agent/i }))

    // Should navigate to session page
    await waitFor(() => {
      expect(mockNavigate).toHaveBeenCalledWith({
        to: '/sessions/$sessionId',
        params: { sessionId: 'session-789' },
      })
    })
  })

  it('starts session with plan mode when None is selected', async () => {
    const user = userEvent.setup()
    mockPostApiSessions.mockResolvedValueOnce(createMockResponse(createMockSession()))

    render(
      <AgentLauncher projectId="project-123" entityId="issue-456" workingDirectory="/workdir" />,
      { wrapper: createWrapper() }
    )

    // Wait for prompts to load
    await waitFor(() => {
      expect(screen.getByRole('combobox', { name: /prompt/i })).toBeInTheDocument()
    })

    // Select None option
    const promptSelect = screen.getByRole('combobox', { name: /prompt/i })
    await user.click(promptSelect)
    // Use getAllByText since the text appears in both the trigger and dropdown
    const noneOptions = screen.getAllByText('None - Start without prompt (Plan mode)')
    // Click the one in the dropdown (not the selected value)
    await user.click(noneOptions[noneOptions.length - 1])

    // Click start button
    await user.click(screen.getByRole('button', { name: /start agent/i }))

    // Should create session with Plan mode and no system prompt
    await waitFor(() => {
      expect(mockPostApiSessions).toHaveBeenCalledWith({
        body: expect.objectContaining({
          entityId: 'issue-456',
          projectId: 'project-123',
          workingDirectory: '/workdir',
          mode: SessionMode.PLAN,
          systemPrompt: undefined,
        }),
      })
    })
  })

  it('displays (project) suffix for override prompts in dropdown', async () => {
    const promptsWithOverride: AgentPrompt[] = [
      {
        id: 'prompt-1',
        name: 'Build Feature',
        initialMessage: 'Build the feature',
        mode: SessionMode.BUILD,
        isOverride: true,
      },
      {
        id: 'prompt-2',
        name: 'Plan Task',
        initialMessage: 'Create a plan',
        mode: SessionMode.PLAN,
        isOverride: false,
      },
    ]
    mockGetAgentPrompts.mockResolvedValue(createMockResponse(promptsWithOverride))

    const user = userEvent.setup()
    render(
      <AgentLauncher projectId="project-123" entityId="issue-456" workingDirectory="/workdir" />,
      { wrapper: createWrapper() }
    )

    // Wait for prompts to load
    await waitFor(() => {
      expect(screen.getByRole('combobox', { name: /prompt/i })).toBeInTheDocument()
    })

    // Open the prompt dropdown
    const promptSelect = screen.getByRole('combobox', { name: /prompt/i })
    await user.click(promptSelect)

    // Should show (project) suffix for override prompt
    expect(screen.getByText(/Build Feature \(project\)/)).toBeInTheDocument()
    // Should not show (project) suffix for non-override prompt
    expect(screen.getByText('Plan Task')).toBeInTheDocument()
    expect(screen.queryByText(/Plan Task \(project\)/)).not.toBeInTheDocument()
  })
})
