import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useMergedProjectPrompts, mergedProjectPromptsQueryKey } from './use-merged-project-prompts'
import { AgentPrompts } from '@/api'

vi.mock('@/api', () => ({
  AgentPrompts: {
    getApiAgentPromptsAvailableForProjectByProjectId: vi.fn(),
  },
}))

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  })
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  }
}

describe('useMergedProjectPrompts', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('returns merged prompts for a project', async () => {
    const mockPrompts = [
      { id: '1', name: 'Project Prompt', initialMessage: 'Hello', mode: 1, projectId: 'proj-1' },
      { id: '2', name: 'Global Prompt', initialMessage: 'Hi', mode: 0, projectId: null },
    ]
    vi.mocked(AgentPrompts.getApiAgentPromptsAvailableForProjectByProjectId).mockResolvedValue({
      data: mockPrompts,
    } as never)

    const { result } = renderHook(() => useMergedProjectPrompts('proj-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))

    expect(result.current.data).toEqual(mockPrompts)
    expect(AgentPrompts.getApiAgentPromptsAvailableForProjectByProjectId).toHaveBeenCalledWith({
      path: { projectId: 'proj-1' },
    })
  })

  it('does not fetch when projectId is empty', () => {
    const { result } = renderHook(() => useMergedProjectPrompts(''), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(false)
    expect(result.current.isFetching).toBe(false)
    expect(AgentPrompts.getApiAgentPromptsAvailableForProjectByProjectId).not.toHaveBeenCalled()
  })

  it('handles API errors', async () => {
    vi.mocked(AgentPrompts.getApiAgentPromptsAvailableForProjectByProjectId).mockResolvedValue({
      error: { detail: 'Not found' },
    } as never)

    const { result } = renderHook(() => useMergedProjectPrompts('proj-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => expect(result.current.isError).toBe(true))
  })
})

describe('mergedProjectPromptsQueryKey', () => {
  it('returns correct query key structure', () => {
    expect(mergedProjectPromptsQueryKey('proj-123')).toEqual(['merged-project-prompts', 'proj-123'])
  })
})
