import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Issues, type IssueResponse, IssueStatus, IssueType, ExecutionMode } from '@/api'
import EditIssue from './projects.$projectId.issues.$issueId.edit'

// Mock the API
vi.mock('@/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api')>()
  return {
    ...actual,
    Issues: {
      getApiIssuesByIssueId: vi.fn(),
      putApiIssuesByIssueId: vi.fn(),
    },
  }
})

// Mock the breadcrumb hook
vi.mock('@/hooks/use-breadcrumbs', () => ({
  useBreadcrumbSetter: vi.fn(),
}))

// Mock TanStack Router
const mockNavigate = vi.fn()
const mockUseParams = vi.fn()
const mockUseBlocker = vi.fn()

vi.mock('@tanstack/react-router', async () => {
  const actual = await vi.importActual('@tanstack/react-router')
  return {
    ...actual,
    useNavigate: () => mockNavigate,
    useParams: () => mockUseParams(),
    useBlocker: (config: { condition: boolean }) => {
      mockUseBlocker(config)
      // Don't auto-trigger blocked status - return idle to prevent dialog from showing
      return {
        status: 'idle',
        proceed: vi.fn(),
        reset: vi.fn(),
      }
    },
    Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
      <a
        href={to}
        onClick={(e) => {
          e.preventDefault()
          mockNavigate({ to })
        }}
      >
        {children}
      </a>
    ),
    createFileRoute: () => () => ({ component: () => null }),
  }
})

const mockIssue: IssueResponse = {
  id: 'issue-123',
  title: 'Test Issue',
  description: 'Test description',
  status: IssueStatus[0], // Open
  type: IssueType[0], // Task
  priority: 3,
  executionMode: ExecutionMode[0], // Series
  workingBranchId: 'feature/test',
  parentIssues: [],
  tags: ['test', 'urgent'],
  linkedPR: null,
  linkedIssues: [],
  createdAt: '2024-01-01T00:00:00Z',
  lastUpdate: '2024-01-01T00:00:00Z',
}

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  })
  return ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  )
}

describe('EditIssue Page', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    mockUseParams.mockReturnValue({
      projectId: 'project-1',
      issueId: 'issue-123',
    })
  })

  it('renders loading skeleton while fetching issue', () => {
    vi.mocked(Issues.getApiIssuesByIssueId).mockReturnValue(
      new Promise(() => {}) as never // Never resolves
    )

    render(<EditIssue />, { wrapper: createWrapper() })

    expect(screen.getByText('Edit Issue')).toBeInTheDocument()
    expect(screen.getByTestId('edit-form-skeleton')).toBeInTheDocument()
  })

  it('renders form with issue data when loaded', async () => {
    vi.mocked(Issues.getApiIssuesByIssueId).mockResolvedValue({
      data: mockIssue,
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })

    render(<EditIssue />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByLabelText(/title/i)).toHaveValue('Test Issue')
    })

    expect(screen.getByLabelText(/description/i)).toHaveValue('Test description')
  })

  it('shows error message when issue fetch fails', async () => {
    vi.mocked(Issues.getApiIssuesByIssueId).mockResolvedValue({
      data: undefined,
      error: { detail: 'Issue not found' },
      request: new Request('http://test'),
      response: new Response(null, { status: 404 }),
    })

    render(<EditIssue />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByRole('alert')).toBeInTheDocument()
    })

    expect(screen.getByText(/issue not found/i)).toBeInTheDocument()
  })

  it('validates required title field', async () => {
    vi.mocked(Issues.getApiIssuesByIssueId).mockResolvedValue({
      data: mockIssue,
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })

    const user = userEvent.setup()
    render(<EditIssue />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByLabelText(/title/i)).toBeInTheDocument()
    })

    const titleInput = screen.getByLabelText(/title/i)
    await user.clear(titleInput)

    const saveButton = screen.getByRole('button', { name: /^save changes$/i })
    await user.click(saveButton)

    await waitFor(() => {
      expect(screen.getByText(/title is required/i)).toBeInTheDocument()
    })
  })

  it('saves issue successfully and navigates back', async () => {
    vi.mocked(Issues.getApiIssuesByIssueId).mockResolvedValue({
      data: mockIssue,
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })

    vi.mocked(Issues.putApiIssuesByIssueId).mockResolvedValue({
      data: { ...mockIssue, title: 'Updated Title' },
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })

    const user = userEvent.setup()
    render(<EditIssue />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByLabelText(/title/i)).toBeInTheDocument()
    })

    const titleInput = screen.getByLabelText(/title/i)
    await user.clear(titleInput)
    await user.type(titleInput, 'Updated Title')

    const saveButton = screen.getByRole('button', { name: /^save changes$/i })
    await user.click(saveButton)

    await waitFor(() => {
      expect(Issues.putApiIssuesByIssueId).toHaveBeenCalledWith(
        expect.objectContaining({
          path: { issueId: 'issue-123' },
          body: expect.objectContaining({
            projectId: 'project-1',
            title: 'Updated Title',
          }),
        })
      )
    })

    await waitFor(() => {
      expect(mockNavigate).toHaveBeenCalledWith({
        to: '/projects/$projectId/issues',
        params: { projectId: 'project-1' },
      })
    })
  })

  it('shows error message when save fails', async () => {
    vi.mocked(Issues.getApiIssuesByIssueId).mockResolvedValue({
      data: mockIssue,
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })

    vi.mocked(Issues.putApiIssuesByIssueId).mockResolvedValue({
      data: undefined,
      error: { detail: 'Server error' },
      request: new Request('http://test'),
      response: new Response(null, { status: 500 }),
    })

    const user = userEvent.setup()
    render(<EditIssue />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByLabelText(/title/i)).toBeInTheDocument()
    })

    const titleInput = screen.getByLabelText(/title/i)
    await user.clear(titleInput)
    await user.type(titleInput, 'Updated Title')

    const saveButton = screen.getByRole('button', { name: /^save changes$/i })
    await user.click(saveButton)

    await waitFor(() => {
      expect(screen.getByRole('alert')).toBeInTheDocument()
    })
  })

  it('cancels and navigates back without saving', async () => {
    vi.mocked(Issues.getApiIssuesByIssueId).mockResolvedValue({
      data: mockIssue,
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })

    const user = userEvent.setup()
    render(<EditIssue />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByLabelText(/title/i)).toBeInTheDocument()
    })

    const cancelButton = screen.getByRole('button', { name: /cancel/i })
    await user.click(cancelButton)

    expect(mockNavigate).toHaveBeenCalled()
    expect(Issues.putApiIssuesByIssueId).not.toHaveBeenCalled()
  })

  it('tracks dirty state for unsaved changes warning', async () => {
    vi.mocked(Issues.getApiIssuesByIssueId).mockResolvedValue({
      data: mockIssue,
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })

    const user = userEvent.setup()
    render(<EditIssue />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByLabelText(/title/i)).toBeInTheDocument()
    })

    // Initially should not be blocking navigation
    expect(mockUseBlocker).toHaveBeenLastCalledWith(expect.objectContaining({ condition: false }))

    // Make a change
    const titleInput = screen.getByLabelText(/title/i)
    await user.type(titleInput, ' modified')

    // Should now be blocking navigation
    await waitFor(() => {
      expect(mockUseBlocker).toHaveBeenLastCalledWith(expect.objectContaining({ condition: true }))
    })
  })

  it('handles keyboard shortcut Cmd/Ctrl+S to save', async () => {
    vi.mocked(Issues.getApiIssuesByIssueId).mockResolvedValue({
      data: mockIssue,
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })

    vi.mocked(Issues.putApiIssuesByIssueId).mockResolvedValue({
      data: mockIssue,
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })

    const user = userEvent.setup()
    render(<EditIssue />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByLabelText(/title/i)).toBeInTheDocument()
    })

    // Make a change first
    const titleInput = screen.getByLabelText(/title/i)
    await user.type(titleInput, ' modified')

    // Press Ctrl+S (or Cmd+S on Mac)
    await user.keyboard('{Control>}s{/Control}')

    await waitFor(() => {
      expect(Issues.putApiIssuesByIssueId).toHaveBeenCalled()
    })
  })

  it('disables save button while submitting', async () => {
    vi.mocked(Issues.getApiIssuesByIssueId).mockResolvedValue({
      data: mockIssue,
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })

    let resolvePromise: (value: unknown) => void
    const pendingPromise = new Promise((resolve) => {
      resolvePromise = resolve
    })

    vi.mocked(Issues.putApiIssuesByIssueId).mockReturnValue(pendingPromise as never)

    const user = userEvent.setup()
    render(<EditIssue />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByLabelText(/title/i)).toBeInTheDocument()
    })

    const titleInput = screen.getByLabelText(/title/i)
    await user.type(titleInput, ' modified')

    const saveButton = screen.getByRole('button', { name: /^save changes$/i })
    await user.click(saveButton)

    await waitFor(() => {
      expect(saveButton).toBeDisabled()
    })

    // Clean up
    resolvePromise!({
      data: mockIssue,
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })
  })

  it('renders all form fields', async () => {
    vi.mocked(Issues.getApiIssuesByIssueId).mockResolvedValue({
      data: mockIssue,
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })

    render(<EditIssue />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByLabelText(/title/i)).toBeInTheDocument()
    })

    // Check all expected form fields are present
    expect(screen.getByLabelText(/title/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/description/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/status/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/type/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/priority/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/execution mode/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/working branch/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/tags/i)).toBeInTheDocument()
  })

  it('renders markdown preview for description', async () => {
    vi.mocked(Issues.getApiIssuesByIssueId).mockResolvedValue({
      data: { ...mockIssue, description: '# Heading\n\nParagraph text' },
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })

    render(<EditIssue />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByLabelText(/description/i)).toBeInTheDocument()
    })

    // Check for preview tab
    expect(screen.getByRole('tab', { name: /preview/i })).toBeInTheDocument()
  })

  it('initializes form with correct status and type from API response', async () => {
    // Create an issue with specific type (Chore = 2) and status (Progress = 1)
    // C# IssueStatus: Open=0, Progress=1, Review=2, Complete=3, Archived=4, Closed=5, Deleted=6
    // C# IssueType: Task=0, Bug=1, Chore=2, Feature=3, Idea=4
    const issueWithChoreType: IssueResponse = {
      ...mockIssue,
      status: 1, // Progress (In Progress)
      type: 2, // Chore
      priority: 2,
    }

    vi.mocked(Issues.getApiIssuesByIssueId).mockResolvedValue({
      data: issueWithChoreType,
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })

    vi.mocked(Issues.putApiIssuesByIssueId).mockResolvedValue({
      data: issueWithChoreType,
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })

    const user = userEvent.setup()
    render(<EditIssue />, { wrapper: createWrapper() })

    // Wait for title to be populated (confirms data is loaded)
    await waitFor(() => {
      expect(screen.getByLabelText(/title/i)).toHaveValue('Test Issue')
    })

    // Verify the Select components display the correct values
    const statusTrigger = screen.getByRole('combobox', { name: /status/i })
    const typeTrigger = screen.getByRole('combobox', { name: /type/i })
    expect(statusTrigger).toHaveTextContent('In Progress')
    expect(typeTrigger).toHaveTextContent('Chore')

    // Save without changing anything and verify correct values are sent
    const saveButton = screen.getByRole('button', { name: /^save changes$/i })
    await user.click(saveButton)

    await waitFor(() => {
      expect(Issues.putApiIssuesByIssueId).toHaveBeenCalledWith(
        expect.objectContaining({
          body: expect.objectContaining({
            status: 1, // Progress (In Progress)
            type: 2, // Chore
            priority: 2,
          }),
        })
      )
    })
  })

  it('sends correct status and type values when saving', async () => {
    // Start with a Chore type issue
    // C# IssueStatus: Open=0, Progress=1, Review=2, Complete=3, Archived=4, Closed=5, Deleted=6
    // C# IssueType: Task=0, Bug=1, Chore=2, Feature=3, Idea=4
    const issueWithChoreType: IssueResponse = {
      ...mockIssue,
      status: 1, // Progress (In Progress)
      type: 2, // Chore
    }

    vi.mocked(Issues.getApiIssuesByIssueId).mockResolvedValue({
      data: issueWithChoreType,
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })

    vi.mocked(Issues.putApiIssuesByIssueId).mockResolvedValue({
      data: issueWithChoreType,
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })

    const user = userEvent.setup()
    render(<EditIssue />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByLabelText(/title/i)).toHaveValue('Test Issue')
    })

    // Wait for form to be fully loaded with correct values
    await waitFor(() => {
      expect(screen.getByRole('combobox', { name: /type/i })).toHaveTextContent('Chore')
    })

    // Just save without changing anything
    const saveButton = screen.getByRole('button', { name: /^save changes$/i })
    await user.click(saveButton)

    // Verify the API was called with the correct type and status
    await waitFor(() => {
      expect(Issues.putApiIssuesByIssueId).toHaveBeenCalledWith(
        expect.objectContaining({
          body: expect.objectContaining({
            status: 1, // Progress (In Progress)
            type: 2, // Chore
          }),
        })
      )
    })
  })
})
