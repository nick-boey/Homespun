import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { IssuesAgentDialog } from './issues-agent-dialog'
import { Issues, IssuesAgent, AgentPrompts } from '@/api'

// Mock the router
const mockNavigate = vi.fn()
vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => mockNavigate,
}))

// Mock the API
vi.mock('@/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api')>()
  return {
    ...actual,
    Issues: {
      getApiIssuesByIssueId: vi.fn(),
    },
    IssuesAgent: {
      postApiIssuesAgentSession: vi.fn(),
    },
    AgentPrompts: {
      getApiAgentPromptsIssueAgentAvailableByProjectId: vi.fn(),
    },
  }
})

const mockPrompts = [
  {
    id: 'prompt-1',
    name: 'Default Prompt',
    mode: 'build',
    isOverride: false,
  },
  {
    id: 'prompt-2',
    name: 'Plan Prompt',
    mode: 'plan',
    isOverride: false,
  },
  {
    id: 'prompt-3',
    name: 'Project Override',
    mode: 'build',
    isOverride: true,
  },
]

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  })
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  }
}

function mockPromptsResponse(prompts = mockPrompts) {
  vi.mocked(AgentPrompts.getApiAgentPromptsIssueAgentAvailableByProjectId).mockResolvedValue({
    data: prompts,
  } as never)
}

describe('IssuesAgentDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
    mockPromptsResponse()
  })

  it('renders dialog when open', () => {
    render(<IssuesAgentDialog open={true} onOpenChange={() => {}} projectId="proj-1" />, {
      wrapper: createWrapper(),
    })

    expect(screen.getByText('Start Issues Agent')).toBeInTheDocument()
    expect(screen.getByText('Start Agent')).toBeInTheDocument()
  })

  it('renders dialog at 80% viewport width and height', () => {
    render(<IssuesAgentDialog open={true} onOpenChange={() => {}} projectId="proj-1" />, {
      wrapper: createWrapper(),
    })

    const dialogContent = document.querySelector('[data-slot="dialog-content"]')
    expect(dialogContent).toHaveClass('max-h-[80vh]')
    expect(dialogContent).toHaveClass('sm:max-w-[80vw]')
  })

  it('does not render when closed', () => {
    render(<IssuesAgentDialog open={false} onOpenChange={() => {}} projectId="proj-1" />, {
      wrapper: createWrapper(),
    })

    expect(screen.queryByText('Start Issues Agent')).not.toBeInTheDocument()
  })

  it('shows instructions textarea', () => {
    render(<IssuesAgentDialog open={true} onOpenChange={() => {}} projectId="proj-1" />, {
      wrapper: createWrapper(),
    })

    expect(screen.getByLabelText('Instructions')).toBeInTheDocument()
    expect(
      screen.getByPlaceholderText('What would you like the agent to do? (optional)')
    ).toBeInTheDocument()
  })

  it('shows selected issue when provided', async () => {
    const mockIssue = {
      id: 'abc123',
      title: 'Test Issue Title',
    }

    vi.mocked(Issues.getApiIssuesByIssueId).mockResolvedValue({
      data: mockIssue,
    } as never)

    render(
      <IssuesAgentDialog
        open={true}
        onOpenChange={() => {}}
        projectId="proj-1"
        selectedIssueId="abc123"
      />,
      { wrapper: createWrapper() }
    )

    expect(screen.getByText('Selected Issue')).toBeInTheDocument()

    await waitFor(() => {
      expect(screen.getByText('abc123')).toBeInTheDocument()
      expect(screen.getByText('Test Issue Title')).toBeInTheDocument()
    })
  })

  it('does not show selected issue section when no issue provided', () => {
    render(<IssuesAgentDialog open={true} onOpenChange={() => {}} projectId="proj-1" />, {
      wrapper: createWrapper(),
    })

    expect(screen.queryByText('Selected Issue')).not.toBeInTheDocument()
  })

  it('renders prompt dropdown with available prompts', async () => {
    render(<IssuesAgentDialog open={true} onOpenChange={() => {}} projectId="proj-1" />, {
      wrapper: createWrapper(),
    })

    // Wait for prompts to load
    await waitFor(() => {
      expect(screen.getByRole('combobox', { name: 'Select prompt' })).toBeInTheDocument()
    })

    // Open the dropdown
    const user = userEvent.setup()
    await user.click(screen.getByRole('combobox', { name: 'Select prompt' }))

    // Verify "None" option is present
    expect(
      screen.getByRole('option', { name: 'None - Start without prompt (Build mode)' })
    ).toBeInTheDocument()

    // Verify prompts are listed
    expect(screen.getByRole('option', { name: 'Default Prompt (build)' })).toBeInTheDocument()
    expect(screen.getByRole('option', { name: 'Plan Prompt (plan)' })).toBeInTheDocument()
    expect(
      screen.getByRole('option', { name: 'Project Override (build) (project)' })
    ).toBeInTheDocument()
  })

  it('shows "None" option and defaults to first prompt when no selection saved', async () => {
    render(<IssuesAgentDialog open={true} onOpenChange={() => {}} projectId="proj-1" />, {
      wrapper: createWrapper(),
    })

    // Wait for prompts to load - should default to first prompt
    await waitFor(() => {
      const promptSelect = screen.getByRole('combobox', { name: 'Select prompt' })
      expect(promptSelect).toBeInTheDocument()
    })
  })

  it('persists selected prompt to localStorage', async () => {
    const user = userEvent.setup()

    render(<IssuesAgentDialog open={true} onOpenChange={() => {}} projectId="proj-1" />, {
      wrapper: createWrapper(),
    })

    // Wait for prompts to load
    await waitFor(() => {
      expect(screen.getByRole('combobox', { name: 'Select prompt' })).toBeInTheDocument()
    })

    // Open dropdown and select "None"
    await user.click(screen.getByRole('combobox', { name: 'Select prompt' }))
    await user.click(
      screen.getByRole('option', { name: 'None - Start without prompt (Build mode)' })
    )

    // Verify persisted
    expect(localStorage.getItem('issues-agent-prompt')).toBe('__none__')
  })

  it('passes prompt ID to the create session mutation', async () => {
    const user = userEvent.setup()
    const mockResult = {
      sessionId: 'session-123',
      branchName: 'issues-agent-123',
      clonePath: '/tmp/clone',
    }

    vi.mocked(IssuesAgent.postApiIssuesAgentSession).mockResolvedValue({
      data: mockResult,
    } as never)

    render(
      <IssuesAgentDialog
        open={true}
        onOpenChange={() => {}}
        projectId="proj-1"
        selectedIssueId="abc123"
      />,
      { wrapper: createWrapper() }
    )

    // Wait for prompts to load (defaults to first prompt)
    await waitFor(() => {
      expect(screen.getByRole('combobox', { name: 'Select prompt' })).toBeInTheDocument()
    })

    // Click start (with default prompt selected)
    await user.click(screen.getByText('Start Agent'))

    await waitFor(() => {
      expect(IssuesAgent.postApiIssuesAgentSession).toHaveBeenCalledWith({
        body: {
          projectId: 'proj-1',
          model: 'sonnet',
          selectedIssueId: 'abc123',
          promptId: 'prompt-1',
        },
      })
    })
  })

  it('passes null prompt ID when "None" is selected', async () => {
    const user = userEvent.setup()
    const mockResult = {
      sessionId: 'session-123',
      branchName: 'issues-agent-123',
      clonePath: '/tmp/clone',
    }

    // Pre-set "None" in localStorage
    localStorage.setItem('issues-agent-prompt', '__none__')

    vi.mocked(IssuesAgent.postApiIssuesAgentSession).mockResolvedValue({
      data: mockResult,
    } as never)

    render(<IssuesAgentDialog open={true} onOpenChange={() => {}} projectId="proj-1" />, {
      wrapper: createWrapper(),
    })

    // Wait for prompts to load
    await waitFor(() => {
      expect(screen.getByRole('combobox', { name: 'Select prompt' })).toBeInTheDocument()
    })

    // Click start (with "None" selected)
    await user.click(screen.getByText('Start Agent'))

    await waitFor(() => {
      expect(IssuesAgent.postApiIssuesAgentSession).toHaveBeenCalledWith({
        body: {
          projectId: 'proj-1',
          model: 'sonnet',
          promptId: null,
        },
      })
    })
  })

  it('calls API with correct params when starting agent', async () => {
    const user = userEvent.setup()
    const mockResult = {
      sessionId: 'session-123',
      branchName: 'issues-agent-123',
      clonePath: '/tmp/clone',
    }

    vi.mocked(IssuesAgent.postApiIssuesAgentSession).mockResolvedValue({
      data: mockResult,
    } as never)

    const onOpenChange = vi.fn()

    render(
      <IssuesAgentDialog
        open={true}
        onOpenChange={onOpenChange}
        projectId="proj-1"
        selectedIssueId="abc123"
      />,
      { wrapper: createWrapper() }
    )

    // Wait for prompts to load
    await waitFor(() => {
      expect(screen.getByRole('combobox', { name: 'Select prompt' })).toBeInTheDocument()
    })

    // Enter instructions
    const instructionsInput = screen.getByLabelText('Instructions')
    await user.type(instructionsInput, 'Update the issue status')

    // Click start
    await user.click(screen.getByText('Start Agent'))

    await waitFor(() => {
      expect(IssuesAgent.postApiIssuesAgentSession).toHaveBeenCalledWith({
        body: {
          projectId: 'proj-1',
          model: 'sonnet',
          selectedIssueId: 'abc123',
          userInstructions: 'Update the issue status',
          promptId: 'prompt-1',
        },
      })
    })

    expect(mockNavigate).toHaveBeenCalledWith({
      to: '/sessions/$sessionId',
      params: { sessionId: 'session-123' },
    })
    expect(onOpenChange).toHaveBeenCalledWith(false)
  })

  it('shows helper text based on instructions state', async () => {
    const user = userEvent.setup()

    render(<IssuesAgentDialog open={true} onOpenChange={() => {}} projectId="proj-1" />, {
      wrapper: createWrapper(),
    })

    // Initially shows empty state helper
    expect(screen.getByText('Leave empty to start an interactive session.')).toBeInTheDocument()

    // Type some instructions
    const instructionsInput = screen.getByLabelText('Instructions')
    await user.type(instructionsInput, 'Do something')

    // Shows different helper text
    expect(screen.getByText('The agent will start with these instructions.')).toBeInTheDocument()
  })

  it('allows model selection', async () => {
    const user = userEvent.setup()

    render(<IssuesAgentDialog open={true} onOpenChange={() => {}} projectId="proj-1" />, {
      wrapper: createWrapper(),
    })

    // Click the model selector
    const modelSelector = screen.getByRole('combobox', { name: 'Select model' })
    await user.click(modelSelector)

    // Select Opus
    await user.click(screen.getByText('Opus'))

    // Verify model is persisted in localStorage
    expect(localStorage.getItem('issues-agent-model')).toBe('opus')
  })

  it('clears instructions when dialog closes via escape key', async () => {
    const user = userEvent.setup()
    let currentOpen = true
    const onOpenChange = vi.fn((newOpen: boolean) => {
      currentOpen = newOpen
    })

    const { rerender } = render(
      <IssuesAgentDialog open={currentOpen} onOpenChange={onOpenChange} projectId="proj-1" />,
      { wrapper: createWrapper() }
    )

    // Type some instructions
    const instructionsInput = screen.getByLabelText('Instructions')
    await user.type(instructionsInput, 'Some instructions')

    // Close the dialog via escape key
    await user.keyboard('{Escape}')

    // onOpenChange should be called with false
    expect(onOpenChange).toHaveBeenCalledWith(false)

    // Simulate the parent updating the prop
    rerender(
      <QueryClientProvider
        client={
          new QueryClient({
            defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
          })
        }
      >
        <IssuesAgentDialog open={false} onOpenChange={onOpenChange} projectId="proj-1" />
      </QueryClientProvider>
    )

    // Reopen the dialog
    rerender(
      <QueryClientProvider
        client={
          new QueryClient({
            defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
          })
        }
      >
        <IssuesAgentDialog open={true} onOpenChange={onOpenChange} projectId="proj-1" />
      </QueryClientProvider>
    )

    // Instructions should be cleared
    expect(screen.getByLabelText('Instructions')).toHaveValue('')
  })

  it('defaults to "None" when no prompts are available', async () => {
    mockPromptsResponse([])

    render(<IssuesAgentDialog open={true} onOpenChange={() => {}} projectId="proj-1" />, {
      wrapper: createWrapper(),
    })

    // Wait for prompts to load
    await waitFor(() => {
      expect(screen.getByRole('combobox', { name: 'Select prompt' })).toBeInTheDocument()
    })
  })
})
