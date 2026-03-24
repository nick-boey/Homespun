import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useIssueAgentProjectPrompts } from './use-issue-agent-project-prompts'
import { AgentPrompts } from '@/api'
import { SessionMode } from '@/api/generated/types.gen'

vi.mock('@/api', () => ({
  AgentPrompts: {
    getApiAgentPromptsIssueAgentAvailableByProjectId: vi.fn(),
  },
}))

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
    },
  })
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  }
}

describe('useIssueAgentProjectPrompts', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('fetches issue agent prompts for a project', async () => {
    const prompts = [
      { id: '1', name: 'Issue Prompt', mode: SessionMode.BUILD, projectId: 'proj-1' },
    ]
    vi.mocked(AgentPrompts.getApiAgentPromptsIssueAgentAvailableByProjectId).mockResolvedValue({
      data: prompts,
    } as never)

    const { result } = renderHook(() => useIssueAgentProjectPrompts('proj-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))

    expect(result.current.data).toEqual(prompts)
    expect(AgentPrompts.getApiAgentPromptsIssueAgentAvailableByProjectId).toHaveBeenCalledWith({
      path: { projectId: 'proj-1' },
    })
  })

  it('does not fetch when projectId is empty', () => {
    const { result } = renderHook(() => useIssueAgentProjectPrompts(''), {
      wrapper: createWrapper(),
    })

    expect(result.current.isFetching).toBe(false)
    expect(AgentPrompts.getApiAgentPromptsIssueAgentAvailableByProjectId).not.toHaveBeenCalled()
  })

  it('handles API errors', async () => {
    vi.mocked(AgentPrompts.getApiAgentPromptsIssueAgentAvailableByProjectId).mockResolvedValue({
      error: { detail: 'Not found' },
    } as never)

    const { result } = renderHook(() => useIssueAgentProjectPrompts('proj-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => expect(result.current.isError).toBe(true))
    expect(result.current.error?.message).toBe('Not found')
  })
})
