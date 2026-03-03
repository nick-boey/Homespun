import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useProjectPrompts, projectPromptsQueryKey } from './use-project-prompts'
import { AgentPrompts } from '@/api'

vi.mock('@/api', () => ({
  AgentPrompts: {
    getApiAgentPromptsProjectByProjectId: vi.fn(),
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

describe('useProjectPrompts', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('returns prompts for a project', async () => {
    const mockPrompts = [
      { id: '1', name: 'Test Prompt', initialMessage: 'Hello', mode: 1, projectId: 'proj-1' },
      { id: '2', name: 'Another Prompt', initialMessage: 'Hi', mode: 0, projectId: 'proj-1' },
    ]
    vi.mocked(AgentPrompts.getApiAgentPromptsProjectByProjectId).mockResolvedValue({
      data: mockPrompts,
    } as never)

    const { result } = renderHook(() => useProjectPrompts('proj-1'), { wrapper: createWrapper() })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))

    expect(result.current.data).toEqual(mockPrompts)
    expect(AgentPrompts.getApiAgentPromptsProjectByProjectId).toHaveBeenCalledWith({
      path: { projectId: 'proj-1' },
    })
  })

  it('does not fetch when projectId is empty', () => {
    const { result } = renderHook(() => useProjectPrompts(''), { wrapper: createWrapper() })

    expect(result.current.isLoading).toBe(false)
    expect(result.current.isFetching).toBe(false)
    expect(AgentPrompts.getApiAgentPromptsProjectByProjectId).not.toHaveBeenCalled()
  })

  it('handles API errors', async () => {
    vi.mocked(AgentPrompts.getApiAgentPromptsProjectByProjectId).mockResolvedValue({
      error: { detail: 'Not found' },
    } as never)

    const { result } = renderHook(() => useProjectPrompts('proj-1'), { wrapper: createWrapper() })

    await waitFor(() => expect(result.current.isError).toBe(true))
  })
})

describe('projectPromptsQueryKey', () => {
  it('returns correct query key structure', () => {
    expect(projectPromptsQueryKey('proj-123')).toEqual(['project-prompts', 'proj-123'])
  })
})
