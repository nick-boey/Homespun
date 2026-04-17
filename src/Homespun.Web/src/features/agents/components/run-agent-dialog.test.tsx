import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { RunAgentDialog } from './run-agent-dialog'
import { Issues, IssuesAgent, Skills, SessionMode, SkillCategory, SkillArgKind } from '@/api'
import type { ReactNode } from 'react'
import type { DiscoveredSkills, RunAgentAcceptedResponse } from '@/api/generated/types.gen'

vi.mock('@/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api')>()
  return {
    ...actual,
    Skills: {
      getApiSkillsProjectByProjectId: vi.fn(),
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

const mockNavigate = vi.fn()
vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => mockNavigate,
}))

const mockGetSkills = vi.mocked(Skills.getApiSkillsProjectByProjectId)
const mockRunAgent = vi.mocked(Issues.postApiIssuesByIssueIdRun)
const mockCreateIssuesAgentSession = vi.mocked(IssuesAgent.postApiIssuesAgentSession)

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

const MOCK_SKILLS: DiscoveredSkills = {
  openSpec: [],
  general: [],
  homespun: [
    {
      name: 'fix-bug',
      description: 'Fix a bug',
      category: SkillCategory.HOMESPUN,
      mode: SessionMode.BUILD,
      args: [{ name: 'issue-id', kind: SkillArgKind.ISSUE, label: 'Issue ID' }],
    },
  ],
}

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

function getTaskTab() {
  return screen.getByTestId('task-tab-content')
}

function getIssuesTab() {
  return screen.getByTestId('issues-tab-content')
}

describe('RunAgentDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()

    mockGetSkills.mockResolvedValue(createMockResponse(MOCK_SKILLS))
    mockRunAgent.mockResolvedValue(createMockResponse(createMockRunAgentResponse()))
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

  it('renders dialog with both tabs when open', async () => {
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
      expect(screen.getByRole('tab', { name: /task agent/i })).toHaveAttribute(
        'data-state',
        'active'
      )
    })
  })

  it('defaults to issues tab when no issueId', async () => {
    render(<RunAgentDialog open={true} onOpenChange={() => {}} projectId="project-123" />, {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(screen.getByRole('tab', { name: /issues agent/i })).toHaveAttribute(
        'data-state',
        'active'
      )
    })
  })

  describe('Task Agent Tab', () => {
    it('shows skill picker, mode, model, and start button', async () => {
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
        expect(within(taskTab).getByRole('combobox', { name: /select skill/i })).toBeInTheDocument()
      })
      expect(within(taskTab).getByRole('combobox', { name: 'Select mode' })).toBeInTheDocument()
      expect(within(taskTab).getByRole('combobox', { name: 'Select model' })).toBeInTheDocument()
      expect(within(taskTab).getByRole('button', { name: /start agent/i })).toBeInTheDocument()
    })

    it('sends skillName and skillArgs when a skill is selected', async () => {
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

      // Wait for skills loaded
      const skillTrigger = await within(taskTab).findByRole('combobox', { name: /select skill/i })
      await user.click(skillTrigger)

      // Select fix-bug
      const listbox = await screen.findByRole('listbox')
      await user.click(within(listbox).getByText(/fix-bug/))

      // Fill in the arg
      const issueIdInput = await within(taskTab).findByLabelText('Issue ID')
      await user.type(issueIdInput, 'ABC123')

      await user.click(within(taskTab).getByRole('button', { name: /start agent/i }))

      await waitFor(() => {
        expect(mockRunAgent).toHaveBeenCalledWith({
          path: { issueId: 'issue-456' },
          body: expect.objectContaining({
            projectId: 'project-123',
            skillName: 'fix-bug',
            skillArgs: { 'issue-id': 'ABC123' },
          }),
        })
      })

      expect(onAgentStart).toHaveBeenCalledWith({
        issueId: 'issue-456',
        branchName: 'feature/test-123',
        message: 'Agent is starting',
      })
    })

    it('sends no skillName / skillArgs when no skill selected', async () => {
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
        expect(within(taskTab).getByRole('button', { name: /start agent/i })).toBeInTheDocument()
      })

      await user.click(within(taskTab).getByRole('button', { name: /start agent/i }))

      await waitFor(() => {
        expect(mockRunAgent).toHaveBeenCalled()
      })

      const body = mockRunAgent.mock.calls[0]?.[0]?.body
      expect(body?.skillName).toBeUndefined()
      expect(body?.skillArgs).toBeUndefined()
    })

    it('includes user instructions in the dispatch', async () => {
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

      const textarea = await within(taskTab).findByPlaceholderText(/additional instructions/i)
      await user.type(textarea, 'Do the thing')

      await user.click(within(taskTab).getByRole('button', { name: /start agent/i }))

      await waitFor(() => {
        expect(mockRunAgent).toHaveBeenCalledWith({
          path: { issueId: 'issue-456' },
          body: expect.objectContaining({
            userInstructions: 'Do the thing',
          }),
        })
      })
    })

    it('shows conflict state when agent already running', async () => {
      const user = userEvent.setup()

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
  })

  describe('Issues Agent Tab (skill-less)', () => {
    it('has no skill picker — only mode, model, textarea, start button', async () => {
      render(<RunAgentDialog open={true} onOpenChange={() => {}} projectId="project-123" />, {
        wrapper: createWrapper(),
      })

      const issuesTab = getIssuesTab()

      await waitFor(() => {
        expect(screen.getByRole('tab', { name: /issues agent/i })).toHaveAttribute(
          'data-state',
          'active'
        )
      })

      expect(
        within(issuesTab).queryByRole('combobox', { name: /select skill/i })
      ).not.toBeInTheDocument()
      expect(within(issuesTab).getByRole('combobox', { name: 'Select mode' })).toBeInTheDocument()
      expect(within(issuesTab).getByRole('combobox', { name: 'Select model' })).toBeInTheDocument()
      expect(within(issuesTab).getByPlaceholderText(/additional instructions/i)).toBeInTheDocument()
      expect(within(issuesTab).getByRole('button', { name: /start agent/i })).toBeInTheDocument()
    })

    it('stays on page when user instructions are provided', async () => {
      const user = userEvent.setup()
      const onOpenChange = vi.fn()

      mockCreateIssuesAgentSession.mockResolvedValue({
        data: {
          sessionId: 'session-123',
          branchName: 'issues-agent-123',
          clonePath: '/tmp/clone',
        },
      } as never)

      render(<RunAgentDialog open={true} onOpenChange={onOpenChange} projectId="project-123" />, {
        wrapper: createWrapper(),
      })

      const issuesTab = getIssuesTab()

      const textarea = within(issuesTab).getByPlaceholderText(/additional instructions/i)
      await user.type(textarea, 'Do something specific')

      await user.click(within(issuesTab).getByRole('button', { name: /start agent/i }))

      await waitFor(() => {
        expect(onOpenChange).toHaveBeenCalledWith(false)
      })
      expect(mockNavigate).not.toHaveBeenCalled()
    })

    it('navigates to session when no instructions are provided', async () => {
      const user = userEvent.setup()
      const onOpenChange = vi.fn()

      mockCreateIssuesAgentSession.mockResolvedValue({
        data: {
          sessionId: 'session-123',
          branchName: 'issues-agent-123',
          clonePath: '/tmp/clone',
        },
      } as never)

      render(<RunAgentDialog open={true} onOpenChange={onOpenChange} projectId="project-123" />, {
        wrapper: createWrapper(),
      })

      const issuesTab = getIssuesTab()

      await user.click(within(issuesTab).getByRole('button', { name: /start agent/i }))

      await waitFor(() => {
        expect(mockNavigate).toHaveBeenCalledWith({
          to: '/sessions/$sessionId',
          params: { sessionId: 'session-123' },
        })
      })
    })
  })
})
