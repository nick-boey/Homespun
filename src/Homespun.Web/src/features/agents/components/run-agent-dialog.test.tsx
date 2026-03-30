import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RunAgentDialog } from './run-agent-dialog'
import { AgentPrompts, Issues, IssuesAgent, SessionMode } from '@/api'
import type { ReactNode } from 'react'
import type { AgentPrompt, RunAgentAcceptedResponse } from '@/api/generated/types.gen'

vi.mock('@/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api')>()
  return {
    ...actual,
    AgentPrompts: {
      getApiAgentPromptsAvailableForProjectByProjectId: vi.fn(),
      getApiAgentPromptsIssueAgentAvailableByProjectId: vi.fn(),
    },
    Issues: {
      postApiIssuesByIssueIdRun: vi.fn(),
      getApiIssuesByIssueId: vi.fn(),
    },
    IssuesAgent: {
      postApiIssuesAgentSession: vi.fn(),
    },
  }
})

// Mock useNavigate from tanstack router
const mockNavigate = vi.fn()
vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => mockNavigate,
}))

const mockGetAgentPrompts = vi.mocked(AgentPrompts.getApiAgentPromptsAvailableForProjectByProjectId)
const mockGetIssueAgentPrompts = vi.mocked(
  AgentPrompts.getApiAgentPromptsIssueAgentAvailableByProjectId
)
const mockGetIssue = vi.mocked(Issues.getApiIssuesByIssueId)
const mockRunAgent = vi.mocked(Issues.postApiIssuesByIssueIdRun)
const mockCreateIssuesAgentSession = vi.mocked(IssuesAgent.postApiIssuesAgentSession)

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

const mockTaskPrompts: AgentPrompt[] = [
  {
    name: 'Build Feature',
    initialMessage: 'Build the feature for {{title}}',
    mode: SessionMode.BUILD,
  },
  {
    name: 'Plan Task',
    initialMessage: 'Create a plan',
    mode: SessionMode.PLAN,
  },
]

const mockIssuePrompts: AgentPrompt[] = [
  {
    name: 'Default Prompt',
    initialMessage: 'Work on issues',
    mode: SessionMode.BUILD,
    isOverride: false,
  },
  {
    name: 'Plan Prompt',
    initialMessage: 'Plan the issues',
    mode: SessionMode.PLAN,
    isOverride: false,
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

/** Get the task tab content container */
function getTaskTab() {
  return screen.getByTestId('task-tab-content')
}

/** Get the issues tab content container */
function getIssuesTab() {
  return screen.getByTestId('issues-tab-content')
}

describe('RunAgentDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()

    // Setup default mock responses
    mockGetAgentPrompts.mockResolvedValue(createMockResponse(mockTaskPrompts))
    mockGetIssueAgentPrompts.mockResolvedValue(createMockResponse(mockIssuePrompts))
    mockRunAgent.mockResolvedValue(createMockResponse(createMockRunAgentResponse()))
    mockGetIssue.mockResolvedValue(
      createMockResponse({
        id: 'issue-456',
        title: 'Test Issue',
        description: 'A test issue description',
        type: 'task',
      })
    )
  })

  it('renders nothing when closed', () => {
    const { container } = render(
      <RunAgentDialog
        open={false}
        onOpenChange={() => {}}
        projectId="project-123"
        issueId="issue-456"
      />,
      { wrapper: createWrapper() }
    )

    expect(container).toBeEmptyDOMElement()
  })

  it('renders dialog with tabs when open', async () => {
    render(
      <RunAgentDialog
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

    // Should have both tabs
    expect(screen.getByRole('tab', { name: /task agent/i })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: /issues agent/i })).toBeInTheDocument()
  })

  it('defaults to task tab when issueId is provided', async () => {
    render(
      <RunAgentDialog
        open={true}
        onOpenChange={() => {}}
        projectId="project-123"
        issueId="issue-456"
      />,
      { wrapper: createWrapper() }
    )

    await waitFor(() => {
      const taskTab = screen.getByRole('tab', { name: /task agent/i })
      expect(taskTab).toHaveAttribute('data-state', 'active')
    })
  })

  it('defaults to issues tab when no issueId', async () => {
    render(<RunAgentDialog open={true} onOpenChange={() => {}} projectId="project-123" />, {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      const issuesTab = screen.getByRole('tab', { name: /issues agent/i })
      expect(issuesTab).toHaveAttribute('data-state', 'active')
    })
  })

  it('respects defaultTab prop', async () => {
    render(
      <RunAgentDialog
        open={true}
        onOpenChange={() => {}}
        projectId="project-123"
        issueId="issue-456"
        defaultTab="issues"
      />,
      { wrapper: createWrapper() }
    )

    await waitFor(() => {
      const issuesTab = screen.getByRole('tab', { name: /issues agent/i })
      expect(issuesTab).toHaveAttribute('data-state', 'active')
    })
  })

  describe('Task Agent Tab', () => {
    it('shows prompt dropdown, model dropdown, and start button', async () => {
      render(
        <RunAgentDialog
          open={true}
          onOpenChange={() => {}}
          projectId="project-123"
          issueId="issue-456"
        />,
        { wrapper: createWrapper() }
      )

      const taskTab = getTaskTab()

      await waitFor(() => {
        expect(within(taskTab).getByRole('combobox', { name: /prompt/i })).toBeInTheDocument()
      })
      expect(within(taskTab).getByRole('combobox', { name: /model/i })).toBeInTheDocument()
      expect(within(taskTab).getByRole('button', { name: /start agent/i })).toBeInTheDocument()
    })

    it('calls run agent endpoint on start with userInstructions', async () => {
      const user = userEvent.setup()
      const onAgentStart = vi.fn()

      render(
        <RunAgentDialog
          open={true}
          onOpenChange={() => {}}
          projectId="project-123"
          issueId="issue-456"
          onAgentStart={onAgentStart}
        />,
        { wrapper: createWrapper() }
      )

      const taskTab = getTaskTab()

      // Wait for controls to be ready
      await waitFor(() => {
        expect(within(taskTab).getByRole('button', { name: /start agent/i })).toBeInTheDocument()
      })

      // Click start agent
      await user.click(within(taskTab).getByRole('button', { name: /start agent/i }))

      // Should have called the run agent endpoint
      await waitFor(() => {
        expect(mockRunAgent).toHaveBeenCalledWith({
          path: { issueId: 'issue-456' },
          body: expect.objectContaining({
            projectId: 'project-123',
            promptName: 'Build Feature',
          }),
        })
      })

      // Should have called onAgentStart
      await waitFor(() => {
        expect(onAgentStart).toHaveBeenCalledWith({
          issueId: 'issue-456',
          branchName: 'feature/test-123',
          message: 'Agent is starting',
        })
      })
    })

    it('shows conflict state when agent already running', async () => {
      const user = userEvent.setup()

      // Mock 409 conflict response
      mockRunAgent.mockResolvedValue({
        data: undefined,
        error: { sessionId: 'existing-session', status: 'Running', message: 'Already running' },
        request: new Request('http://localhost/api/test'),
        response: new Response(null, { status: 409 }),
      } as never)

      render(
        <RunAgentDialog
          open={true}
          onOpenChange={() => {}}
          projectId="project-123"
          issueId="issue-456"
        />,
        { wrapper: createWrapper() }
      )

      const taskTab = getTaskTab()

      await waitFor(() => {
        expect(within(taskTab).getByRole('button', { name: /start agent/i })).toBeInTheDocument()
      })

      await user.click(within(taskTab).getByRole('button', { name: /start agent/i }))

      await waitFor(() => {
        expect(within(taskTab).getByText(/agent already running/i)).toBeInTheDocument()
      })
    })

    it('shows advanced settings with base branch selector', async () => {
      const user = userEvent.setup()

      render(
        <RunAgentDialog
          open={true}
          onOpenChange={() => {}}
          projectId="project-123"
          issueId="issue-456"
        />,
        { wrapper: createWrapper() }
      )

      const taskTab = getTaskTab()

      await waitFor(() => {
        expect(within(taskTab).getByText(/more settings/i)).toBeInTheDocument()
      })

      await user.click(within(taskTab).getByText(/more settings/i))

      await waitFor(() => {
        expect(within(taskTab).getByText(/base branch/i)).toBeInTheDocument()
      })
    })

    it('prompt selection populates textarea with rendered template text', async () => {
      render(
        <RunAgentDialog
          open={true}
          onOpenChange={() => {}}
          projectId="project-123"
          issueId="issue-456"
        />,
        { wrapper: createWrapper() }
      )

      const taskTab = getTaskTab()

      // Wait for prompts to load
      await waitFor(() => {
        expect(within(taskTab).getByRole('combobox', { name: /prompt/i })).toBeInTheDocument()
      })

      // The textarea should have the rendered template from first prompt with issue context
      // 'Build the feature for {{title}}' -> 'Build the feature for Test Issue'
      await waitFor(() => {
        const textarea = within(taskTab).getByPlaceholderText(/additional instructions/i)
        expect(textarea).toHaveValue('Build the feature for Test Issue')
      })
    })
  })

  describe('Issues Agent Tab', () => {
    it('shows prompt dropdown, model selector, and instructions textarea', async () => {
      render(<RunAgentDialog open={true} onOpenChange={() => {}} projectId="project-123" />, {
        wrapper: createWrapper(),
      })

      const issuesTab = getIssuesTab()

      // Should default to issues tab when no issueId
      await waitFor(() => {
        const tab = screen.getByRole('tab', { name: /issues agent/i })
        expect(tab).toHaveAttribute('data-state', 'active')
      })

      await waitFor(() => {
        expect(within(issuesTab).getByRole('combobox', { name: /prompt/i })).toBeInTheDocument()
      })
      expect(within(issuesTab).getByRole('combobox', { name: /model/i })).toBeInTheDocument()
      expect(within(issuesTab).getByPlaceholderText(/additional instructions/i)).toBeInTheDocument()
    })

    it('stays on page when prompt is selected and instructions provided', async () => {
      const user = userEvent.setup()
      const onOpenChange = vi.fn()

      const mockResult = {
        sessionId: 'session-123',
        branchName: 'issues-agent-123',
        clonePath: '/tmp/clone',
      }

      mockCreateIssuesAgentSession.mockResolvedValue({
        data: mockResult,
      } as never)

      render(<RunAgentDialog open={true} onOpenChange={onOpenChange} projectId="project-123" />, {
        wrapper: createWrapper(),
      })

      const issuesTab = getIssuesTab()

      // Wait for prompts to load
      await waitFor(() => {
        expect(within(issuesTab).getByRole('combobox', { name: /prompt/i })).toBeInTheDocument()
      })

      // Type instructions
      const textarea = within(issuesTab).getByPlaceholderText(/additional instructions/i)
      await user.clear(textarea)
      await user.type(textarea, 'Do something specific')

      // Click start
      await user.click(within(issuesTab).getByRole('button', { name: /start agent/i }))

      // Should close dialog
      await waitFor(() => {
        expect(onOpenChange).toHaveBeenCalledWith(false)
      })

      // Should NOT navigate (has prompt + instructions)
      expect(mockNavigate).not.toHaveBeenCalled()
    })

    it('navigates to session when no prompt selected and no instructions', async () => {
      const user = userEvent.setup()
      const onOpenChange = vi.fn()

      // Pre-set "None" prompt in localStorage
      localStorage.setItem('issues-agent-prompt', '__none__')

      const mockResult = {
        sessionId: 'session-123',
        branchName: 'issues-agent-123',
        clonePath: '/tmp/clone',
      }

      mockCreateIssuesAgentSession.mockResolvedValue({
        data: mockResult,
      } as never)

      render(<RunAgentDialog open={true} onOpenChange={onOpenChange} projectId="project-123" />, {
        wrapper: createWrapper(),
      })

      const issuesTab = getIssuesTab()

      // Wait for prompts to load
      await waitFor(() => {
        expect(within(issuesTab).getByRole('combobox', { name: /prompt/i })).toBeInTheDocument()
      })

      // Click start (with None prompt, no instructions)
      await user.click(within(issuesTab).getByRole('button', { name: /start agent/i }))

      // Should navigate to session page
      await waitFor(() => {
        expect(mockNavigate).toHaveBeenCalledWith({
          to: '/sessions/$sessionId',
          params: { sessionId: 'session-123' },
        })
      })
    })
  })

  it('dialog sizing uses responsive width classes', async () => {
    render(
      <RunAgentDialog
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

    const dialogContent = screen.getByRole('dialog')
    expect(dialogContent.className).toMatch(/w-\[80vw\]/)
  })

  it('tab switching preserves state', async () => {
    const user = userEvent.setup()

    render(
      <RunAgentDialog
        open={true}
        onOpenChange={() => {}}
        projectId="project-123"
        issueId="issue-456"
      />,
      { wrapper: createWrapper() }
    )

    const taskTab = getTaskTab()

    // Wait for task tab to be ready
    await waitFor(() => {
      expect(within(taskTab).getByRole('combobox', { name: /prompt/i })).toBeInTheDocument()
    })

    // Clear then type in textarea on task tab
    const taskTextarea = within(taskTab).getByPlaceholderText(/additional instructions/i)
    await user.clear(taskTextarea)
    await user.type(taskTextarea, 'Task instructions')

    // Switch to issues tab
    await user.click(screen.getByRole('tab', { name: /issues agent/i }))

    // Switch back to task tab
    await user.click(screen.getByRole('tab', { name: /task agent/i }))

    // Task instructions should be preserved
    await waitFor(() => {
      const textarea = within(taskTab).getByPlaceholderText(/additional instructions/i)
      expect(textarea).toHaveValue('Task instructions')
    })
  })
})
