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
      getApiAgentPromptsAvailableForProjectByProjectId: vi.fn(),
      postApiAgentPrompts: vi.fn(),
      putApiAgentPromptsById: vi.fn(),
      deleteApiAgentPromptsById: vi.fn(),
      postApiAgentPromptsCreateOverride: vi.fn(),
      postApiAgentPromptsRestoreDefaults: vi.fn(),
      deleteApiAgentPromptsProjectByProjectIdAll: vi.fn(),
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
        {
          id: '1',
          name: 'First Prompt',
          initialMessage: 'Content 1',
          mode: SessionMode.BUILD,
          projectId: 'proj-1',
        },
        {
          id: '2',
          name: 'Second Prompt',
          initialMessage: 'Content 2',
          mode: SessionMode.PLAN,
          projectId: 'proj-1',
        },
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
        { id: '1', name: 'First', mode: SessionMode.BUILD, projectId: 'proj-1' },
        { id: '2', name: 'Second', mode: SessionMode.PLAN, projectId: 'proj-1' },
        { id: '3', name: 'Third', mode: SessionMode.BUILD, projectId: 'proj-1' },
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
      data: [{ id: '1', name: 'Test Prompt', mode: SessionMode.BUILD, projectId: 'proj-1' }],
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
      data: [
        {
          id: '1',
          name: 'Test Prompt',
          initialMessage: 'Hello',
          mode: SessionMode.BUILD,
          projectId: 'proj-1',
        },
      ],
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

  it('shows section headers for project and inherited global prompts', async () => {
    vi.mocked(AgentPrompts.getApiAgentPromptsProjectByProjectId).mockResolvedValue({
      data: [
        {
          id: 'proj-1-prompt',
          name: 'Project Prompt',
          initialMessage: 'Project message',
          mode: SessionMode.BUILD,
          projectId: 'proj-1',
          isOverride: false,
        },
        {
          id: 'override-prompt',
          name: 'Override Prompt',
          initialMessage: 'Override message',
          mode: SessionMode.BUILD,
          projectId: 'proj-1',
          isOverride: true,
        },
        {
          id: 'global-prompt',
          name: 'Global Prompt',
          initialMessage: 'Global message',
          mode: SessionMode.PLAN,
          projectId: null,
          isOverride: false,
        },
      ],
    } as never)

    render(<PromptsList projectId="proj-1" />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('Project Prompts')).toBeInTheDocument()
      expect(screen.getByText('Inherited Global Prompts')).toBeInTheDocument()
    })

    // All prompts should be visible
    expect(screen.getByText('Project Prompt')).toBeInTheDocument()
    expect(screen.getByText('Override Prompt')).toBeInTheDocument()
    expect(screen.getByText('Global Prompt')).toBeInTheDocument()
  })

  it('hides Project Prompts header when only global prompts exist', async () => {
    vi.mocked(AgentPrompts.getApiAgentPromptsProjectByProjectId).mockResolvedValue({
      data: [
        {
          id: 'global-prompt',
          name: 'Global Prompt',
          initialMessage: 'Global message',
          mode: SessionMode.PLAN,
          projectId: null,
          isOverride: false,
        },
      ],
    } as never)

    render(<PromptsList projectId="proj-1" />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('Inherited Global Prompts')).toBeInTheDocument()
    })

    expect(screen.queryByText('Project Prompts')).not.toBeInTheDocument()
  })

  it('hides Inherited Global Prompts header when only project prompts exist', async () => {
    vi.mocked(AgentPrompts.getApiAgentPromptsProjectByProjectId).mockResolvedValue({
      data: [
        {
          id: 'proj-prompt',
          name: 'Project Prompt',
          initialMessage: 'Project message',
          mode: SessionMode.BUILD,
          projectId: 'proj-1',
          isOverride: false,
        },
      ],
    } as never)

    render(<PromptsList projectId="proj-1" />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('Project Prompts')).toBeInTheDocument()
    })

    expect(screen.queryByText('Inherited Global Prompts')).not.toBeInTheDocument()
  })

  it('does not show section headers on global page', async () => {
    vi.mocked(AgentPrompts.getApiAgentPrompts).mockResolvedValue({
      data: [
        {
          id: 'global-1',
          name: 'Global Prompt',
          initialMessage: 'Message',
          mode: SessionMode.BUILD,
          projectId: null,
          isOverride: false,
        },
      ],
    } as never)

    render(<PromptsList isGlobal />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('Global Prompt')).toBeInTheDocument()
    })

    expect(screen.queryByText('Project Prompts')).not.toBeInTheDocument()
    expect(screen.queryByText('Inherited Global Prompts')).not.toBeInTheDocument()
  })

  it('shows Restore Defaults button on global page', async () => {
    vi.mocked(AgentPrompts.getApiAgentPrompts).mockResolvedValue({
      data: [{ id: '1', name: 'Test', mode: SessionMode.BUILD, projectId: null }],
    } as never)

    render(<PromptsList isGlobal />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('Restore Defaults')).toBeInTheDocument()
    })
  })

  it('shows Clear Project Prompts button on project page', async () => {
    vi.mocked(AgentPrompts.getApiAgentPromptsProjectByProjectId).mockResolvedValue({
      data: [{ id: '1', name: 'Test', mode: SessionMode.BUILD, projectId: 'proj-1' }],
    } as never)

    render(<PromptsList projectId="proj-1" />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('Clear Project Prompts')).toBeInTheDocument()
    })
  })

  it('shows confirmation dialog when Restore Defaults is clicked', async () => {
    const user = userEvent.setup()
    vi.mocked(AgentPrompts.getApiAgentPrompts).mockResolvedValue({
      data: [{ id: '1', name: 'Test', mode: SessionMode.BUILD, projectId: null }],
    } as never)

    render(<PromptsList isGlobal />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('Restore Defaults')).toBeEnabled()
    })

    await user.click(screen.getByText('Restore Defaults'))

    await waitFor(() => {
      expect(
        screen.getByText(
          'This will delete all global prompts and restore defaults. Project prompts are not affected.'
        )
      ).toBeInTheDocument()
    })
  })

  it('shows confirmation dialog when Clear Project Prompts is clicked', async () => {
    const user = userEvent.setup()
    vi.mocked(AgentPrompts.getApiAgentPromptsProjectByProjectId).mockResolvedValue({
      data: [{ id: '1', name: 'Test', mode: SessionMode.BUILD, projectId: 'proj-1' }],
    } as never)

    render(<PromptsList projectId="proj-1" />, { wrapper: createWrapper() })

    await waitFor(() => {
      expect(screen.getByText('Clear Project Prompts')).toBeEnabled()
    })

    await user.click(screen.getByText('Clear Project Prompts'))

    await waitFor(() => {
      expect(
        screen.getByText(
          'This will delete all project prompts. The project will revert to inherited global prompts.'
        )
      ).toBeInTheDocument()
    })
  })

  it('shows disabled restore button in loading state on global page', () => {
    vi.mocked(AgentPrompts.getApiAgentPrompts).mockReturnValue(new Promise(() => {}) as never)

    render(<PromptsList isGlobal />, { wrapper: createWrapper() })

    const restoreButton = screen.getByText('Restore Defaults')
    expect(restoreButton.closest('button')).toBeDisabled()
  })
})
