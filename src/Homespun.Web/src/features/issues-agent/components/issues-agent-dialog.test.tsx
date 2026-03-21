import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { IssuesAgentDialog } from './issues-agent-dialog'
import { Issues, IssuesAgent } from '@/api'

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
  }
})

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

describe('IssuesAgentDialog', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    localStorage.clear()
  })

  it('renders dialog when open', () => {
    render(<IssuesAgentDialog open={true} onOpenChange={() => {}} projectId="proj-1" />, {
      wrapper: createWrapper(),
    })

    expect(screen.getByText('Start Issues Agent')).toBeInTheDocument()
    expect(screen.getByText('Start Agent')).toBeInTheDocument()
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
})
