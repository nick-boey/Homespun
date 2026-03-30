import { describe, it, expect, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { IssueAgentPromptsSection } from './issue-agent-prompts-section'
import { AgentPrompts } from '@/api'
import { PromptCategory, SessionMode, SessionType } from '@/api/generated/types.gen'

vi.mock('@/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api')>()
  return {
    ...actual,
    AgentPrompts: {
      getApiAgentPromptsIssueAgentPrompts: vi.fn(),
      getApiAgentPromptsIssueAgentAvailableByProjectId: vi.fn(),
      postApiAgentPrompts: vi.fn(),
      putApiAgentPromptsByNameByName: vi.fn(),
      deleteApiAgentPromptsByNameByName: vi.fn(),
      postApiAgentPromptsCreateOverride: vi.fn(),
      deleteApiAgentPromptsByNameByNameOverride: vi.fn(),
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

const makeSystemPrompt = (overrides = {}) => ({
  name: 'IssueAgentSystem',
  initialMessage: 'System instructions',
  mode: SessionMode.BUILD,
  projectId: null,
  category: PromptCategory.ISSUE_AGENT,
  sessionType: SessionType.ISSUE_AGENT_SYSTEM,
  ...overrides,
})

const makeUserSelectablePrompt = (overrides = {}) => ({
  name: 'Custom Issue Prompt',
  initialMessage: 'Custom instructions',
  mode: SessionMode.BUILD,
  projectId: null,
  category: PromptCategory.ISSUE_AGENT,
  ...overrides,
})

describe('IssueAgentPromptsSection', () => {
  it('renders loading state', () => {
    vi.mocked(AgentPrompts.getApiAgentPromptsIssueAgentPrompts).mockReturnValue(
      new Promise(() => {}) as never
    )

    render(<IssueAgentPromptsSection />, { wrapper: createWrapper() })

    expect(screen.getByText('Issue Agent Prompts')).toBeInTheDocument()
    expect(screen.getByText('Refresh')).toBeDisabled()
    expect(screen.getByText('New Issue Agent Prompt')).toBeDisabled()
  })

  it('shows issue agent prompts on global page with CRUD', async () => {
    vi.mocked(AgentPrompts.getApiAgentPromptsIssueAgentPrompts).mockResolvedValue({
      data: [makeUserSelectablePrompt(), makeSystemPrompt()],
    } as never)

    render(<IssueAgentPromptsSection />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('Custom Issue Prompt')).toBeInTheDocument()
    })
    // System prompt section should also appear
    expect(screen.getByText('System Prompts')).toBeInTheDocument()
    expect(screen.getByText('IssueAgentSystem')).toBeInTheDocument()
    // Create button should be visible
    expect(screen.getByText('New Issue Agent Prompt')).toBeEnabled()
  })

  it('separates system prompts into read-only section', async () => {
    const user = userEvent.setup()
    vi.mocked(AgentPrompts.getApiAgentPromptsIssueAgentPrompts).mockResolvedValue({
      data: [makeSystemPrompt()],
    } as never)

    render(<IssueAgentPromptsSection />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('IssueAgentSystem')).toBeInTheDocument()
    })

    // System prompt should be in System Prompts section
    expect(screen.getByText('System Prompts')).toBeInTheDocument()

    // Open dropdown on system prompt
    const actionsButton = screen.getByRole('button', { name: /actions/i })
    await user.click(actionsButton)

    // Should have Edit but not Delete
    expect(screen.getByText('Edit')).toBeInTheDocument()
    expect(screen.queryByText('Delete')).not.toBeInTheDocument()
  })

  it('shows create form when New Issue Agent Prompt is clicked', async () => {
    const user = userEvent.setup()
    vi.mocked(AgentPrompts.getApiAgentPromptsIssueAgentPrompts).mockResolvedValue({
      data: [],
    } as never)

    render(<IssueAgentPromptsSection />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('New Issue Agent Prompt')).toBeEnabled()
    })

    await user.click(screen.getByText('New Issue Agent Prompt'))

    expect(screen.getByText('Create New Issue Agent Prompt')).toBeInTheDocument()
    expect(screen.getByLabelText('Name')).toBeInTheDocument()
  })

  it('creates issue agent prompt with correct category', async () => {
    const user = userEvent.setup()
    vi.mocked(AgentPrompts.getApiAgentPromptsIssueAgentPrompts).mockResolvedValue({
      data: [],
    } as never)
    vi.mocked(AgentPrompts.postApiAgentPrompts).mockResolvedValue({
      data: makeUserSelectablePrompt(),
    } as never)

    render(<IssueAgentPromptsSection />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('New Issue Agent Prompt')).toBeEnabled()
    })

    await user.click(screen.getByText('New Issue Agent Prompt'))

    await user.type(screen.getByLabelText('Name'), 'My New Prompt')
    await user.click(screen.getByText('Create Prompt'))

    await waitFor(() => {
      expect(AgentPrompts.postApiAgentPrompts).toHaveBeenCalledWith({
        body: expect.objectContaining({
          name: 'My New Prompt',
          category: PromptCategory.ISSUE_AGENT,
        }),
      })
    })
  })

  it('shows delete option for user-selectable prompts on global page', async () => {
    const user = userEvent.setup()
    vi.mocked(AgentPrompts.getApiAgentPromptsIssueAgentPrompts).mockResolvedValue({
      data: [makeUserSelectablePrompt()],
    } as never)

    render(<IssueAgentPromptsSection />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('Custom Issue Prompt')).toBeInTheDocument()
    })

    await user.click(screen.getByRole('button', { name: /actions/i }))
    expect(screen.getByText('Delete')).toBeInTheDocument()
  })

  it('shows inherited issue agent prompts on project page', async () => {
    vi.mocked(AgentPrompts.getApiAgentPromptsIssueAgentAvailableByProjectId).mockResolvedValue({
      data: [
        makeUserSelectablePrompt({ projectId: null, isOverride: false }),
        makeUserSelectablePrompt({
          name: 'Project Issue Prompt',
          projectId: 'proj-1',
          isOverride: false,
        }),
      ],
    } as never)

    render(<IssueAgentPromptsSection projectId="proj-1" />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('Custom Issue Prompt')).toBeInTheDocument()
      expect(screen.getByText('Project Issue Prompt')).toBeInTheDocument()
    })

    // Should show section headers
    expect(screen.getByText('Project Issue Agent Prompts')).toBeInTheDocument()
    expect(screen.getByText('Inherited Global Issue Agent Prompts')).toBeInTheDocument()
  })

  it('hides delete for inherited global prompts on project page', async () => {
    const user = userEvent.setup()
    vi.mocked(AgentPrompts.getApiAgentPromptsIssueAgentAvailableByProjectId).mockResolvedValue({
      data: [makeUserSelectablePrompt({ projectId: null, isOverride: false })],
    } as never)

    render(<IssueAgentPromptsSection projectId="proj-1" />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('Custom Issue Prompt')).toBeInTheDocument()
    })

    await user.click(screen.getByRole('button', { name: /actions/i }))
    expect(screen.queryByText('Delete')).not.toBeInTheDocument()
    expect(screen.getByText('Edit')).toBeInTheDocument()
  })

  it('shows override flow for inherited global issue agent prompt on project page', async () => {
    const user = userEvent.setup()
    vi.mocked(AgentPrompts.getApiAgentPromptsIssueAgentAvailableByProjectId).mockResolvedValue({
      data: [makeUserSelectablePrompt({ projectId: null, isOverride: false })],
    } as never)

    render(<IssueAgentPromptsSection projectId="proj-1" />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('Custom Issue Prompt')).toBeInTheDocument()
    })

    await user.click(screen.getByRole('button', { name: /actions/i }))
    await user.click(screen.getByText('Edit'))

    await waitFor(() => {
      expect(screen.getByText('Create Project Override')).toBeInTheDocument()
    })
  })

  it('shows prompt count', async () => {
    vi.mocked(AgentPrompts.getApiAgentPromptsIssueAgentPrompts).mockResolvedValue({
      data: [makeUserSelectablePrompt(), makeUserSelectablePrompt({ name: 'Another' })],
    } as never)

    render(<IssueAgentPromptsSection />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('(2)')).toBeInTheDocument()
    })
  })

  it('shows empty state when no user-selectable prompts exist', async () => {
    vi.mocked(AgentPrompts.getApiAgentPromptsIssueAgentPrompts).mockResolvedValue({
      data: [makeSystemPrompt()],
    } as never)

    render(<IssueAgentPromptsSection />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('No issue agent prompts yet')).toBeInTheDocument()
    })
    // System prompts section should still show
    expect(screen.getByText('System Prompts')).toBeInTheDocument()
  })
})
