import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RunAgentDialog } from './run-agent-dialog'
import { AgentPrompts, Issues, IssuesAgent, SessionMode } from '@/api'
import type { ReactNode } from 'react'
import type {
  AgentPrompt,
  RunAgentAcceptedResponse,
  WorkflowSummary,
} from '@/api/generated/types.gen'

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

// Mock workflow hooks
const mockUseWorkflows = vi.fn()
const mockExecuteWorkflowMutate = vi.fn()
const mockExecuteWorkflowMutateAsync = vi.fn()
const mockUseExecuteWorkflow = vi.fn(() => ({
  mutate: mockExecuteWorkflowMutate,
  mutateAsync: mockExecuteWorkflowMutateAsync,
  isPending: false,
  isSuccess: false,
}))

vi.mock('@/features/workflows', () => ({
  useWorkflows: (...args: unknown[]) => mockUseWorkflows(...args),
  useExecuteWorkflow: () => mockUseExecuteWorkflow(),
}))

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

const mockWorkflows: WorkflowSummary[] = [
  {
    id: 'wf-1',
    title: 'Build Pipeline',
    description: 'Runs CI build',
    enabled: true,
    triggerType: 'manual',
    stepCount: 3,
    version: 1,
    updatedAt: '2026-01-01T00:00:00Z',
  },
  {
    id: 'wf-2',
    title: 'Deploy Pipeline',
    description: 'Deploys to production',
    enabled: false,
    triggerType: 'event',
    stepCount: 5,
    version: 2,
    updatedAt: '2026-01-02T00:00:00Z',
  },
  {
    id: 'wf-3',
    title: 'Test Suite',
    description: 'Runs tests',
    enabled: true,
    triggerType: 'manual',
    stepCount: 2,
    version: 1,
    updatedAt: '2026-01-03T00:00:00Z',
  },
]

/** Get the task tab content container */
function getTaskTab() {
  return screen.getByTestId('task-tab-content')
}

/** Get the issues tab content container */
function getIssuesTab() {
  return screen.getByTestId('issues-tab-content')
}

/** Get the workflow tab content container */
function getWorkflowTab() {
  return screen.getByTestId('workflow-tab-content')
}

describe('RunAgentDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()

    // Setup default workflow mock responses
    mockUseWorkflows.mockReturnValue({
      workflows: mockWorkflows,
      isLoading: false,
      isError: false,
      error: null,
    })
    mockExecuteWorkflowMutate.mockReset()
    mockExecuteWorkflowMutateAsync.mockReset()

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

    // Should have all three tabs
    expect(screen.getByRole('tab', { name: /task agent/i })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: /issues agent/i })).toBeInTheDocument()
    expect(screen.getByRole('tab', { name: /workflow/i })).toBeInTheDocument()
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
    it('shows prompt dropdown, mode dropdown, model dropdown, and start button', async () => {
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
      expect(within(taskTab).getByRole('combobox', { name: 'Select mode' })).toBeInTheDocument()
      expect(within(taskTab).getByRole('combobox', { name: /model/i })).toBeInTheDocument()
      expect(within(taskTab).getByRole('button', { name: /start agent/i })).toBeInTheDocument()
    })

    it('sends mode instead of promptName on start', async () => {
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

      // Should have called the run agent endpoint with mode, not promptName
      await waitFor(() => {
        expect(mockRunAgent).toHaveBeenCalledWith({
          path: { issueId: 'issue-456' },
          body: expect.objectContaining({
            projectId: 'project-123',
            mode: SessionMode.BUILD,
          }),
        })
      })

      // Verify promptName is NOT in the request
      const callBody = mockRunAgent.mock.calls[0]?.[0]?.body
      expect(callBody).not.toHaveProperty('promptName')

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
    it('shows prompt dropdown, mode dropdown, model selector, and instructions textarea', async () => {
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
      expect(within(issuesTab).getByRole('combobox', { name: 'Select mode' })).toBeInTheDocument()
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

    it('sends mode in request instead of promptName', async () => {
      const user = userEvent.setup()

      const mockResult = {
        sessionId: 'session-123',
        branchName: 'issues-agent-123',
        clonePath: '/tmp/clone',
      }

      mockCreateIssuesAgentSession.mockResolvedValue({
        data: mockResult,
      } as never)

      render(<RunAgentDialog open={true} onOpenChange={() => {}} projectId="project-123" />, {
        wrapper: createWrapper(),
      })

      const issuesTab = getIssuesTab()

      await waitFor(() => {
        expect(within(issuesTab).getByRole('combobox', { name: /prompt/i })).toBeInTheDocument()
      })

      // Type instructions
      const textarea = within(issuesTab).getByPlaceholderText(/additional instructions/i)
      await user.clear(textarea)
      await user.type(textarea, 'Test instructions')

      await user.click(within(issuesTab).getByRole('button', { name: /start agent/i }))

      await waitFor(() => {
        expect(mockCreateIssuesAgentSession).toHaveBeenCalledWith({
          body: expect.objectContaining({
            projectId: 'project-123',
            mode: SessionMode.BUILD,
          }),
        })
      })

      // Verify promptName is NOT in the request
      const callBody = mockCreateIssuesAgentSession.mock.calls[0]?.[0]?.body
      expect(callBody).not.toHaveProperty('promptName')
    })
  })

  describe('Workflow Tab', () => {
    it('shows loading state while workflows load', async () => {
      mockUseWorkflows.mockReturnValue({
        workflows: [],
        isLoading: true,
        isError: false,
        error: null,
      })

      render(
        <RunAgentDialog
          open={true}
          onOpenChange={() => {}}
          projectId="project-123"
          defaultTab="workflow"
        />,
        { wrapper: createWrapper() }
      )

      const workflowTab = getWorkflowTab()
      expect(within(workflowTab).getByText(/loading workflows/i)).toBeInTheDocument()
    })

    it('shows workflow dropdown and start button when loaded', async () => {
      render(
        <RunAgentDialog
          open={true}
          onOpenChange={() => {}}
          projectId="project-123"
          defaultTab="workflow"
        />,
        { wrapper: createWrapper() }
      )

      const workflowTab = getWorkflowTab()

      // Should show a select for workflows and a start button
      expect(
        within(workflowTab).getByRole('combobox', { name: /select workflow/i })
      ).toBeInTheDocument()
      expect(
        within(workflowTab).getByRole('button', { name: /start workflow/i })
      ).toBeInTheDocument()
    })

    it('shows only enabled workflows in dropdown', async () => {
      const user = userEvent.setup()

      render(
        <RunAgentDialog
          open={true}
          onOpenChange={() => {}}
          projectId="project-123"
          defaultTab="workflow"
        />,
        { wrapper: createWrapper() }
      )

      const workflowTab = getWorkflowTab()

      // Open the dropdown
      await user.click(within(workflowTab).getByRole('combobox', { name: /select workflow/i }))

      // Should show enabled workflows
      expect(screen.getByRole('option', { name: /build pipeline/i })).toBeInTheDocument()
      expect(screen.getByRole('option', { name: /test suite/i })).toBeInTheDocument()

      // Should NOT show disabled workflows
      expect(screen.queryByRole('option', { name: /deploy pipeline/i })).not.toBeInTheDocument()
    })

    it('shows empty state when no enabled workflows', async () => {
      mockUseWorkflows.mockReturnValue({
        workflows: [{ id: 'wf-2', title: 'Deploy', enabled: false }],
        isLoading: false,
        isError: false,
        error: null,
      })

      render(
        <RunAgentDialog
          open={true}
          onOpenChange={() => {}}
          projectId="project-123"
          defaultTab="workflow"
        />,
        { wrapper: createWrapper() }
      )

      const workflowTab = getWorkflowTab()
      expect(within(workflowTab).getByText(/no enabled workflows/i)).toBeInTheDocument()
    })

    it('executes workflow with issue context on start', async () => {
      const user = userEvent.setup()
      const onOpenChange = vi.fn()

      mockExecuteWorkflowMutateAsync.mockResolvedValue({
        executionId: 'exec-1',
        workflowId: 'wf-1',
        status: 'queued',
      })

      render(
        <RunAgentDialog
          open={true}
          onOpenChange={onOpenChange}
          projectId="project-123"
          issueId="issue-456"
          defaultTab="workflow"
        />,
        { wrapper: createWrapper() }
      )

      const workflowTab = getWorkflowTab()

      // Click start workflow
      await user.click(within(workflowTab).getByRole('button', { name: /start workflow/i }))

      await waitFor(() => {
        expect(mockExecuteWorkflowMutateAsync).toHaveBeenCalledWith({
          workflowId: 'wf-1',
          projectId: 'project-123',
          input: { issueId: 'issue-456' },
        })
      })

      // Should close dialog on success
      await waitFor(() => {
        expect(onOpenChange).toHaveBeenCalledWith(false)
      })
    })

    it('executes workflow without issue context when no issueId', async () => {
      const user = userEvent.setup()

      mockExecuteWorkflowMutateAsync.mockResolvedValue({
        executionId: 'exec-1',
        workflowId: 'wf-1',
        status: 'queued',
      })

      render(
        <RunAgentDialog
          open={true}
          onOpenChange={() => {}}
          projectId="project-123"
          defaultTab="workflow"
        />,
        { wrapper: createWrapper() }
      )

      const workflowTab = getWorkflowTab()
      await user.click(within(workflowTab).getByRole('button', { name: /start workflow/i }))

      await waitFor(() => {
        expect(mockExecuteWorkflowMutateAsync).toHaveBeenCalledWith({
          workflowId: 'wf-1',
          projectId: 'project-123',
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

  it('applies responsive classes to start button for mobile layout', async () => {
    mockGetAgentPrompts.mockResolvedValue(createMockResponse(mockTaskPrompts))
    mockUseWorkflows.mockReturnValue({
      workflows: mockWorkflows,
      isLoading: false,
    })

    render(<RunAgentDialog projectId="proj-1" open={true} onOpenChange={vi.fn()} />, {
      wrapper: createWrapper(),
    })

    const taskTab = getTaskTab()

    await waitFor(() => {
      expect(within(taskTab).getByRole('button', { name: /start agent/i })).toBeInTheDocument()
    })

    const startButton = within(taskTab).getByRole('button', { name: /start agent/i })
    expect(startButton.className).toMatch(/w-full/)
    expect(startButton.className).toMatch(/sm:w-auto/)
  })
})
