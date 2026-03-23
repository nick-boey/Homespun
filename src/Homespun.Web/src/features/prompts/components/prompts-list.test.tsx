import { describe, it, expect, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { PromptsList } from './prompts-list'
import { AgentPrompts } from '@/api'
import { SessionMode } from '@/api/generated/types.gen'

vi.mock('@/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api')>()
  return {
    ...actual,
    AgentPrompts: {
      getApiAgentPrompts: vi.fn(),
      getApiAgentPromptsProjectByProjectId: vi.fn(),
      postApiAgentPrompts: vi.fn(),
      putApiAgentPromptsById: vi.fn(),
      deleteApiAgentPromptsById: vi.fn(),
      getApiAgentPromptsAvailableForProjectByProjectId: vi.fn(),
      postApiAgentPromptsCreateOverride: vi.fn(),
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

describe('PromptsList', () => {
  it('renders loading state', () => {
    vi.mocked(AgentPrompts.getApiAgentPromptsProjectByProjectId).mockReturnValue(
      new Promise(() => {}) as never
    )

    render(<PromptsList projectId="proj-1" />, { wrapper: createWrapper() })

    expect(screen.getByText('Agent Prompts')).toBeInTheDocument()
    // Loading state shows disabled buttons
    expect(screen.getByText('Refresh')).toBeDisabled()
    expect(screen.getByText('New Prompt')).toBeDisabled()
  })

  it('renders empty state when no prompts', async () => {
    vi.mocked(AgentPrompts.getApiAgentPromptsProjectByProjectId).mockResolvedValue({
      data: [],
    } as never)

    render(<PromptsList projectId="proj-1" />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('No prompts yet')).toBeInTheDocument()
    })
  })

  it('renders list of prompts', async () => {
    vi.mocked(AgentPrompts.getApiAgentPromptsProjectByProjectId).mockResolvedValue({
      data: [
        { id: '1', name: 'First Prompt', initialMessage: 'Content 1', mode: SessionMode.BUILD },
        { id: '2', name: 'Second Prompt', initialMessage: 'Content 2', mode: SessionMode.PLAN },
      ],
    } as never)

    render(<PromptsList projectId="proj-1" />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('First Prompt')).toBeInTheDocument()
      expect(screen.getByText('Second Prompt')).toBeInTheDocument()
    })
  })

  it('shows create form when New Prompt is clicked', async () => {
    const user = userEvent.setup()
    vi.mocked(AgentPrompts.getApiAgentPromptsProjectByProjectId).mockResolvedValue({
      data: [],
    } as never)

    render(<PromptsList projectId="proj-1" />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('New Prompt')).toBeEnabled()
    })

    await user.click(screen.getByText('New Prompt'))

    expect(screen.getByText('Create New Prompt')).toBeInTheDocument()
    expect(screen.getByLabelText('Name')).toBeInTheDocument()
  })

  it('shows count when prompts exist', async () => {
    vi.mocked(AgentPrompts.getApiAgentPromptsProjectByProjectId).mockResolvedValue({
      data: [
        { id: '1', name: 'First', mode: SessionMode.BUILD },
        { id: '2', name: 'Second', mode: SessionMode.PLAN },
        { id: '3', name: 'Third', mode: SessionMode.BUILD },
      ],
    } as never)

    render(<PromptsList projectId="proj-1" />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('(3)')).toBeInTheDocument()
    })
  })

  it('renders Cards and Code tabs', async () => {
    vi.mocked(AgentPrompts.getApiAgentPromptsProjectByProjectId).mockResolvedValue({
      data: [],
    } as never)

    render(<PromptsList projectId="proj-1" />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByRole('tab', { name: /cards/i })).toBeInTheDocument()
      expect(screen.getByRole('tab', { name: /code/i })).toBeInTheDocument()
    })
  })

  it('defaults to Cards view', async () => {
    vi.mocked(AgentPrompts.getApiAgentPromptsProjectByProjectId).mockResolvedValue({
      data: [{ id: '1', name: 'Test Prompt', mode: SessionMode.BUILD }],
    } as never)

    render(<PromptsList projectId="proj-1" />, { wrapper: createWrapper() })

    await waitFor(() => {
      // Cards tab should be selected
      const cardsTab = screen.getByRole('tab', { name: /cards/i })
      expect(cardsTab).toHaveAttribute('aria-selected', 'true')

      // Should show prompt cards, not code editor
      expect(screen.getByText('Test Prompt')).toBeInTheDocument()
    })
  })

  it('switches to Code view on tab click', async () => {
    const user = userEvent.setup()
    vi.mocked(AgentPrompts.getApiAgentPromptsProjectByProjectId).mockResolvedValue({
      data: [{ id: '1', name: 'Test Prompt', initialMessage: 'Hello', mode: SessionMode.BUILD }],
    } as never)

    render(<PromptsList projectId="proj-1" />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('Test Prompt')).toBeInTheDocument()
    })

    // Click the Code tab
    await user.click(screen.getByRole('tab', { name: /code/i }))

    // Should show code editor with JSON
    await waitFor(() => {
      const textarea = screen.getByRole('textbox')
      expect(textarea).toBeInTheDocument()
      expect((textarea as HTMLTextAreaElement).value).toContain('Test Prompt')
    })
  })

  it('Code view shows Apply and Revert buttons', async () => {
    const user = userEvent.setup()
    vi.mocked(AgentPrompts.getApiAgentPromptsProjectByProjectId).mockResolvedValue({
      data: [],
    } as never)

    render(<PromptsList projectId="proj-1" />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByRole('tab', { name: /code/i })).toBeInTheDocument()
    })

    // Click the Code tab
    await user.click(screen.getByRole('tab', { name: /code/i }))

    // Should show Apply and Revert buttons
    await waitFor(() => {
      expect(screen.getByRole('button', { name: /apply/i })).toBeInTheDocument()
      expect(screen.getByRole('button', { name: /revert/i })).toBeInTheDocument()
    })
  })

  it('shows Create Project Override title when editing global prompt from project page', async () => {
    const user = userEvent.setup()

    // A global prompt has no projectId and isOverride is false
    vi.mocked(AgentPrompts.getApiAgentPromptsProjectByProjectId).mockResolvedValue({
      data: [
        {
          id: 'global-build',
          name: 'Global Build Prompt',
          initialMessage: 'Global build message',
          mode: SessionMode.BUILD,
          projectId: null,
          isOverride: false,
        },
      ],
    } as never)

    render(<PromptsList projectId="proj-1" />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('Global Build Prompt')).toBeInTheDocument()
    })

    // Click the actions dropdown first
    const actionsButton = screen.getByRole('button', { name: /actions/i })
    await user.click(actionsButton)

    // Click the Edit menu item
    const editMenuItem = await screen.findByRole('menuitem', { name: /edit/i })
    await user.click(editMenuItem)

    // Should show the create override title
    await waitFor(() => {
      expect(screen.getByText('Create Project Override')).toBeInTheDocument()
    })
  })

  it('hides delete option for global prompts on project page', async () => {
    const user = userEvent.setup()

    vi.mocked(AgentPrompts.getApiAgentPromptsProjectByProjectId).mockResolvedValue({
      data: [
        {
          id: 'global-build',
          name: 'Global Build Prompt',
          initialMessage: 'Global build message',
          mode: SessionMode.BUILD,
          projectId: null,
          isOverride: false,
        },
      ],
    } as never)

    render(<PromptsList projectId="proj-1" />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('Global Build Prompt')).toBeInTheDocument()
    })

    // Open dropdown menu
    await user.click(screen.getByRole('button', { name: /actions/i }))

    // Delete option should NOT be visible for global prompts on project page
    expect(screen.queryByText('Delete')).not.toBeInTheDocument()
    // Edit should still be visible
    expect(screen.getByText('Edit')).toBeInTheDocument()
  })

  it('shows delete option for project prompts on project page', async () => {
    const user = userEvent.setup()

    vi.mocked(AgentPrompts.getApiAgentPromptsProjectByProjectId).mockResolvedValue({
      data: [
        {
          id: 'proj-build',
          name: 'Project Build Prompt',
          initialMessage: 'Project build message',
          mode: SessionMode.BUILD,
          projectId: 'proj-1',
          isOverride: false,
        },
      ],
    } as never)

    render(<PromptsList projectId="proj-1" />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('Project Build Prompt')).toBeInTheDocument()
    })

    // Open dropdown menu
    await user.click(screen.getByRole('button', { name: /actions/i }))

    // Delete option should be visible for project prompts
    expect(screen.getByText('Delete')).toBeInTheDocument()
  })

  it('shows delete option for all prompts on global page', async () => {
    const user = userEvent.setup()

    vi.mocked(AgentPrompts.getApiAgentPrompts).mockResolvedValue({
      data: [
        {
          id: 'global-build',
          name: 'Global Build Prompt',
          initialMessage: 'Global build message',
          mode: SessionMode.BUILD,
          projectId: null,
          isOverride: false,
        },
      ],
    } as never)

    render(<PromptsList isGlobal />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('Global Build Prompt')).toBeInTheDocument()
    })

    // Open dropdown menu
    await user.click(screen.getByRole('button', { name: /actions/i }))

    // Delete option should be visible on global page
    expect(screen.getByText('Delete')).toBeInTheDocument()
  })

  it('shows Edit Prompt title when editing project-specific prompt', async () => {
    const user = userEvent.setup()

    // A project prompt has projectId set
    vi.mocked(AgentPrompts.getApiAgentPromptsProjectByProjectId).mockResolvedValue({
      data: [
        {
          id: 'proj-build',
          name: 'Project Build Prompt',
          initialMessage: 'Project build message',
          mode: SessionMode.BUILD,
          projectId: 'proj-1',
          isOverride: true,
        },
      ],
    } as never)

    render(<PromptsList projectId="proj-1" />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('Project Build Prompt')).toBeInTheDocument()
    })

    // Click the actions dropdown first
    const actionsButton = screen.getByRole('button', { name: /actions/i })
    await user.click(actionsButton)

    // Click the Edit menu item
    const editMenuItem = await screen.findByRole('menuitem', { name: /edit/i })
    await user.click(editMenuItem)

    // Should show regular edit title
    await waitFor(() => {
      expect(screen.getByText('Edit Prompt')).toBeInTheDocument()
    })
  })
})
