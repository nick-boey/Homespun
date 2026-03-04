import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { AgentLauncherDialog } from './agent-launcher-dialog'
import { AgentPrompts, Clones, Issues, Sessions } from '@/api'
import type { ReactNode } from 'react'
import type {
  AgentPrompt,
  ClaudeSession,
  CloneExistsResponse,
  CreateCloneResponse,
  ResolvedBranchResponse,
} from '@/api/generated/types.gen'

vi.mock('@/api', () => ({
  AgentPrompts: {
    getApiAgentPromptsAvailableForProjectByProjectId: vi.fn(),
  },
  Clones: {
    getApiClonesExists: vi.fn(),
    postApiClones: vi.fn(),
  },
  Issues: {
    getApiIssuesByIssueIdResolvedBranch: vi.fn(),
  },
  Sessions: {
    postApiSessions: vi.fn(),
  },
}))

const mockGetAgentPrompts = vi.mocked(AgentPrompts.getApiAgentPromptsAvailableForProjectByProjectId)
const mockGetApiClonesExists = vi.mocked(Clones.getApiClonesExists)
const mockPostApiClones = vi.mocked(Clones.postApiClones)
const mockGetResolvedBranch = vi.mocked(Issues.getApiIssuesByIssueIdResolvedBranch)
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
    mode: 1 as const,
  },
  {
    id: 'prompt-2',
    name: 'Plan Task',
    initialMessage: 'Create a plan',
    mode: 0 as const,
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

describe('AgentLauncherDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks()

    // Setup default mock responses
    const resolvedBranch: ResolvedBranchResponse = { branchName: 'feature/test-123' }
    const cloneExists: CloneExistsResponse = { exists: false }
    const createCloneResponse: CreateCloneResponse = {
      path: '/clones/project-123/feature-test-123',
      branchName: 'feature/test-123',
    }

    mockGetResolvedBranch.mockResolvedValue(createMockResponse(resolvedBranch))
    mockGetApiClonesExists.mockResolvedValue(createMockResponse(cloneExists))
    mockPostApiClones.mockResolvedValue(createMockResponse(createCloneResponse))
    mockGetAgentPrompts.mockResolvedValue(createMockResponse(mockPrompts))
    mockPostApiSessions.mockResolvedValue(createMockResponse(createMockSession()))
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

  it('shows loading state while resolving branch', async () => {
    // Delay the branch resolution
    let resolveBranch: (value: ReturnType<typeof createMockResponse>) => void
    mockGetResolvedBranch.mockReturnValue(
      new Promise((resolve) => {
        resolveBranch = resolve
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
      expect(screen.getByText(/preparing/i)).toBeInTheDocument()
    })

    // Resolve and clean up
    const resolvedBranch: ResolvedBranchResponse = { branchName: 'feature/test' }
    resolveBranch!(createMockResponse(resolvedBranch))
  })

  it('displays branch name once resolved', async () => {
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
      expect(screen.getByText(/feature\/test-123/)).toBeInTheDocument()
    })
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

  it('creates clone and starts agent when start is clicked', async () => {
    const user = userEvent.setup()
    const onSessionStart = vi.fn()

    render(
      <AgentLauncherDialog
        open={true}
        onOpenChange={() => {}}
        projectId="project-123"
        issueId="issue-456"
        onSessionStart={onSessionStart}
      />,
      { wrapper: createWrapper() }
    )

    // Wait for controls to be ready
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /start agent/i })).toBeInTheDocument()
    })

    // Click start agent
    await user.click(screen.getByRole('button', { name: /start agent/i }))

    // Should have created the clone
    await waitFor(() => {
      expect(mockPostApiClones).toHaveBeenCalledWith({
        body: {
          projectId: 'project-123',
          branchName: 'feature/test-123',
          createBranch: true,
        },
      })
    })

    // Should have started the session with the clone path
    await waitFor(() => {
      expect(mockPostApiSessions).toHaveBeenCalledWith({
        body: expect.objectContaining({
          entityId: 'issue-456',
          projectId: 'project-123',
          workingDirectory: '/clones/project-123/feature-test-123',
        }),
      })
    })

    // Should have called onSessionStart
    await waitFor(() => {
      expect(onSessionStart).toHaveBeenCalled()
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

  it('shows error when branch resolution fails', async () => {
    mockGetResolvedBranch.mockResolvedValue({
      data: undefined,
      error: { detail: 'Failed to resolve branch' },
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
      expect(screen.getByText(/failed to prepare workspace/i)).toBeInTheDocument()
    })
    expect(screen.getByText(/failed to resolve branch/i)).toBeInTheDocument()
  })

  it('disables start button while clone is being created', async () => {
    const user = userEvent.setup()

    // Delay clone creation
    let resolveClone: (value: ReturnType<typeof createMockResponse>) => void
    mockPostApiClones.mockReturnValue(
      new Promise((resolve) => {
        resolveClone = resolve
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

    // Button should be disabled during creation
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /start agent/i })).toBeDisabled()
    })

    // Clean up
    const createCloneResponse: CreateCloneResponse = {
      path: '/clones/project-123/feature-test-123',
      branchName: 'feature/test-123',
    }
    resolveClone!(createMockResponse(createCloneResponse))
  })

  it('closes dialog after successful session start', async () => {
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
})
