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
      getApiAgentPromptsProjectByProjectId: vi.fn(),
      postApiAgentPrompts: vi.fn(),
      putApiAgentPromptsById: vi.fn(),
      deleteApiAgentPromptsById: vi.fn(),
      getApiAgentPromptsAvailableForProjectByProjectId: vi.fn(),
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
})
