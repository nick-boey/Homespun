import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor, act } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useDeleteAllProjectPrompts } from './use-delete-all-project-prompts'
import { AgentPrompts } from '@/api'

vi.mock('@/api', () => ({
  AgentPrompts: {
    deleteApiAgentPromptsProjectByProjectIdAll: vi.fn(),
  },
}))

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

describe('useDeleteAllProjectPrompts', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('deletes all project prompts successfully', async () => {
    vi.mocked(AgentPrompts.deleteApiAgentPromptsProjectByProjectIdAll).mockResolvedValue({
      data: undefined,
    } as never)

    const onSuccess = vi.fn()
    const { result } = renderHook(
      () => useDeleteAllProjectPrompts({ projectId: 'proj-1', onSuccess }),
      { wrapper: createWrapper() }
    )

    await act(async () => {
      await result.current.mutateAsync()
    })

    expect(AgentPrompts.deleteApiAgentPromptsProjectByProjectIdAll).toHaveBeenCalledWith({
      path: { projectId: 'proj-1' },
    })
    expect(onSuccess).toHaveBeenCalled()
  })

  it('handles API errors', async () => {
    vi.mocked(AgentPrompts.deleteApiAgentPromptsProjectByProjectIdAll).mockResolvedValue({
      error: { detail: 'Server error' },
    } as never)

    const onError = vi.fn()
    const { result } = renderHook(
      () => useDeleteAllProjectPrompts({ projectId: 'proj-1', onError }),
      { wrapper: createWrapper() }
    )

    await act(async () => {
      try {
        await result.current.mutateAsync()
      } catch {
        // Expected to throw
      }
    })

    await waitFor(() => expect(onError).toHaveBeenCalled())
  })
})
