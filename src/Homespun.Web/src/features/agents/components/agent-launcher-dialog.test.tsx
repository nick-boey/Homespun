import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AgentLauncherDialog } from './agent-launcher-dialog'
import { AgentPrompts, Issues, SessionMode } from '@/api'
import type { ReactNode } from 'react'
import type { AgentPrompt, RunAgentAcceptedResponse } from '@/api/generated/types.gen'

vi.mock('@/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api')>()
  return {
    ...actual,
    AgentPrompts: {
      getApiAgentPromptsAvailableForProjectByProjectId: vi.fn(),
    },
    Issues: {
      postApiIssuesByIssueIdRun: vi.fn(),
    },
  }
})

// Mock useNavigate from tanstack router
const mockNavigate = vi.fn()
vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => mockNavigate,
}))

const mockGetAgentPrompts = vi.mocked(AgentPrompts.getApiAgentPromptsAvailableForProjectByProjectId)
const mockRunAgent = vi.mocked(Issues.postApiIssuesByIssueIdRun)

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

function createMockRunAgentResponse(
  overrides: Partial<RunAgentAcceptedResponse> = {}
): RunAgentAcceptedResponse {
  return {
    issueId: 'issue-456',
    branchName: 'feature/test-123',
    message: 'Agent is starting',
    ...overrides,
  }
}

describe('AgentLauncherDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks()

    // Setup default mock responses
    mockGetAgentPrompts.mockResolvedValue(createMockResponse(mockPrompts))
    mockRunAgent.mockResolvedValue(createMockResponse(createMockRunAgentResponse()))
  })

  it('renders nothing when closed', () => {
    const { container } = render(
      <AgentLauncherDialog
        open={false}
        onOpenChange={() => {}}
        projectId="project-123"
        issueId="issue-456"
      />,
      { wrapper: createWrapper() }
    )

    expect(container).toBeEmptyDOMElement()
  })

  it('renders dialog when open', async () => {
    render(
      <AgentLauncherDialog
        open={true}
        onOpenChange={() => {}}
        projectId="project-123"
        issueId="issue-456"
      />,
      { wrapper: createWrapper() }
    )

    await waitFor(() => {
      expect(screen.getByRole('dialog')).toBeInTheDocument()
    })
  })

  it('renders dialog at 80% viewport width and height', async () => {
    render(
      <AgentLauncherDialog
        open={true}
        onOpenChange={() => {}}
        projectId="project-123"
        issueId="issue-456"
      />,
      { wrapper: createWrapper() }
    )

    await waitFor(() => {
      const dialogContent = document.querySelector('[data-slot="dialog-content"]')
      expect(dialogContent).toHaveClass('max-h-[80vh]')
      expect(dialogContent).toHaveClass('sm:max-w-[80vw]')
    })
  })

  it('shows loading state while loading prompts', async () => {
    // Delay the prompt loading
    let resolvePrompts: (value: ReturnType<typeof createMockResponse>) => void
    mockGetAgentPrompts.mockReturnValue(
      new Promise((resolve) => {
        resolvePrompts = resolve
      }) as never
    )

    render(
      <AgentLauncherDialog
        open={true}
        onOpenChange={() => {}}
        projectId="project-123"
        issueId="issue-456"
      />,
      { wrapper: createWrapper() }
    )

    // Should show loading indicator
    await waitFor(() => {
      expect(screen.getByText(/loading prompts/i)).toBeInTheDocument()
    })

    // Resolve and clean up
    resolvePrompts!(createMockResponse(mockPrompts))
  })

  it('renders launcher controls once ready', async () => {
    render(
      <AgentLauncherDialog
        open={true}
        onOpenChange={() => {}}
        projectId="project-123"
        issueId="issue-456"
      />,
      { wrapper: createWrapper() }
    )

    // Wait for controls to appear
    await waitFor(() => {
      expect(screen.getByRole('combobox', { name: /prompt/i })).toBeInTheDocument()
    })
    expect(screen.getByRole('combobox', { name: /model/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /start agent/i })).toBeInTheDocument()
  })

  it('calls run agent endpoint when start is clicked', async () => {
    const user = userEvent.setup()
    const onAgentStart = vi.fn()

    render(
      <AgentLauncherDialog
        open={true}
        onOpenChange={() => {}}
        projectId="project-123"
        issueId="issue-456"
        onAgentStart={onAgentStart}
      />,
      { wrapper: createWrapper() }
    )

    // Wait for controls to be ready
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /start agent/i })).toBeInTheDocument()
    })

    // Click start agent
    await user.click(screen.getByRole('button', { name: /start agent/i }))

    // Should have called the run agent endpoint
    await waitFor(() => {
      expect(mockRunAgent).toHaveBeenCalledWith({
        path: { issueId: 'issue-456' },
        body: expect.objectContaining({
          projectId: 'project-123',
          promptId: 'prompt-1', // First prompt is selected by default
        }),
      })
    })

    // Should have called onAgentStart with the result (now 202 Accepted response)
    await waitFor(() => {
      expect(onAgentStart).toHaveBeenCalledWith({
        issueId: 'issue-456',
        branchName: 'feature/test-123',
        message: 'Agent is starting',
      })
    })
  })

  it('closes dialog when close button is clicked', async () => {
    const user = userEvent.setup()
    const onOpenChange = vi.fn()

    render(
      <AgentLauncherDialog
        open={true}
        onOpenChange={onOpenChange}
        projectId="project-123"
        issueId="issue-456"
      />,
      { wrapper: createWrapper() }
    )

    await waitFor(() => {
      expect(screen.getByRole('dialog')).toBeInTheDocument()
    })

    // Click the close button (X)
    const closeButton = screen.getByRole('button', { name: /close/i })
    await user.click(closeButton)

    expect(onOpenChange).toHaveBeenCalledWith(false)
  })

  it('shows dialog title', async () => {
    render(
      <AgentLauncherDialog
        open={true}
        onOpenChange={() => {}}
        projectId="project-123"
        issueId="issue-456"
      />,
      { wrapper: createWrapper() }
    )

    await waitFor(() => {
      expect(screen.getByText(/run agent/i)).toBeInTheDocument()
    })
  })

  it('shows error when prompts loading fails', async () => {
    mockGetAgentPrompts.mockResolvedValue({
      data: undefined,
      error: { detail: 'Failed to load prompts' },
      request: new Request('http://localhost/api/test'),
      response: new Response(null, { status: 500 }),
    })

    render(
      <AgentLauncherDialog
        open={true}
        onOpenChange={() => {}}
        projectId="project-123"
        issueId="issue-456"
      />,
      { wrapper: createWrapper() }
    )

    await waitFor(() => {
      expect(screen.getByText(/failed to load prompts/i)).toBeInTheDocument()
    })
  })

  it('disables start button while agent is starting', async () => {
    const user = userEvent.setup()

    // Delay run agent call
    let resolveRunAgent: (value: ReturnType<typeof createMockResponse>) => void
    mockRunAgent.mockReturnValue(
      new Promise((resolve) => {
        resolveRunAgent = resolve
      }) as never
    )

    render(
      <AgentLauncherDialog
        open={true}
        onOpenChange={() => {}}
        projectId="project-123"
        issueId="issue-456"
      />,
      { wrapper: createWrapper() }
    )

    // Wait for controls to be ready
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /start agent/i })).toBeEnabled()
    })

    // Click start
    await user.click(screen.getByRole('button', { name: /start agent/i }))

    // Button should be disabled during request
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /start agent/i })).toBeDisabled()
    })

    // Clean up
    resolveRunAgent!(createMockResponse(createMockRunAgentResponse()))
  })

  it('closes dialog after successful agent start', async () => {
    const user = userEvent.setup()
    const onOpenChange = vi.fn()

    render(
      <AgentLauncherDialog
        open={true}
        onOpenChange={onOpenChange}
        projectId="project-123"
        issueId="issue-456"
      />,
      { wrapper: createWrapper() }
    )

    // Wait for controls to be ready
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /start agent/i })).toBeInTheDocument()
    })

    // Click start agent
    await user.click(screen.getByRole('button', { name: /start agent/i }))

    // Should close dialog after success
    await waitFor(() => {
      expect(onOpenChange).toHaveBeenCalledWith(false)
    })
  })

  it('calls onError when run agent fails', async () => {
    const user = userEvent.setup()
    const onError = vi.fn()

    mockRunAgent.mockResolvedValue({
      data: undefined,
      error: { detail: 'Failed to run agent' },
      request: new Request('http://localhost/api/test'),
      response: new Response(null, { status: 500 }),
    })

    render(
      <AgentLauncherDialog
        open={true}
        onOpenChange={() => {}}
        projectId="project-123"
        issueId="issue-456"
        onError={onError}
      />,
      { wrapper: createWrapper() }
    )

    // Wait for controls to be ready
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /start agent/i })).toBeInTheDocument()
    })

    // Click start agent
    await user.click(screen.getByRole('button', { name: /start agent/i }))

    // Should have called onError
    await waitFor(() => {
      expect(onError).toHaveBeenCalled()
    })
  })

  it('shows None option as first item in dropdown', async () => {
    const user = userEvent.setup()
    render(
      <AgentLauncherDialog
        open={true}
        onOpenChange={() => {}}
        projectId="project-123"
        issueId="issue-456"
      />,
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

  it('sends null promptId when None is selected', async () => {
    const user = userEvent.setup()
    const onOpenChange = vi.fn()
    render(
      <AgentLauncherDialog
        open={true}
        onOpenChange={onOpenChange}
        projectId="project-123"
        issueId="issue-456"
      />,
      { wrapper: createWrapper() }
    )

    // Wait for controls to be ready
    await waitFor(() => {
      expect(screen.getByRole('combobox', { name: /prompt/i })).toBeInTheDocument()
    })

    // Select None option
    const promptSelect = screen.getByRole('combobox', { name: /prompt/i })
    await user.click(promptSelect)
    await user.click(screen.getByText('None - Start without prompt (Plan mode)'))

    // Click start
    await user.click(screen.getByRole('button', { name: /start agent/i }))

    // Should call run agent with null promptId
    await waitFor(() => {
      expect(mockRunAgent).toHaveBeenCalledWith({
        path: { issueId: 'issue-456' },
        body: expect.objectContaining({
          projectId: 'project-123',
          promptId: null,
        }),
      })
    })

    // Dialog should close immediately (async startup)
    await waitFor(() => {
      expect(onOpenChange).toHaveBeenCalledWith(false)
    })
  })

  it('always shows None option regardless of prompt types', async () => {
    // Update mock prompts to only have Build prompts
    const buildOnlyPrompts: AgentPrompt[] = [
      {
        id: 'prompt-1',
        name: 'Build Feature',
        initialMessage: 'Build the feature',
        mode: SessionMode.BUILD,
      },
      {
        id: 'prompt-2',
        name: 'Build Another',
        initialMessage: 'Build another feature',
        mode: SessionMode.BUILD,
      },
    ]
    mockGetAgentPrompts.mockResolvedValue(createMockResponse(buildOnlyPrompts))

    const user = userEvent.setup()
    render(
      <AgentLauncherDialog
        open={true}
        onOpenChange={() => {}}
        projectId="project-123"
        issueId="issue-456"
      />,
      { wrapper: createWrapper() }
    )

    // Wait for prompts to load
    await waitFor(() => {
      expect(screen.getByRole('combobox', { name: /prompt/i })).toBeInTheDocument()
    })

    // Open the prompt dropdown
    const promptSelect = screen.getByRole('combobox', { name: /prompt/i })
    await user.click(promptSelect)

    // Should ALWAYS have None option as first option, regardless of prompt types
    const options = screen.getAllByRole('option')
    expect(options[0]).toHaveTextContent('None - Start without prompt (Plan mode)')
  })

  it('renders dialog at 80% viewport width and height', async () => {
    render(
      <AgentLauncherDialog
        open={true}
        onOpenChange={() => {}}
        projectId="project-123"
        issueId="issue-456"
      />,
      { wrapper: createWrapper() }
    )

    await waitFor(() => {
      expect(screen.getByRole('dialog')).toBeInTheDocument()
    })

    const dialogContent = document.querySelector('[data-slot="dialog-content"]')
    expect(dialogContent).toHaveClass('max-h-[80vh]')
    expect(dialogContent).toHaveClass('sm:max-w-[80vw]')
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
      <AgentLauncherDialog
        open={true}
        onOpenChange={() => {}}
        projectId="project-123"
        issueId="issue-456"
      />,
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
