import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor, act } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useDeletePrompt } from './use-delete-prompt'
import { AgentPrompts } from '@/api'
import { globalPromptsQueryKey } from './use-global-prompts'

vi.mock('@/api', () => ({
  AgentPrompts: {
    deleteApiAgentPromptsByNameByName: vi.fn(),
  },
}))

function createWrapper(queryClient?: QueryClient) {
  const qc =
    queryClient ??
    new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    })
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return <QueryClientProvider client={qc}>{children}</QueryClientProvider>
  }
}

describe('useDeletePrompt', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('deletes a prompt successfully', async () => {
    vi.mocked(AgentPrompts.deleteApiAgentPromptsByNameByName).mockResolvedValue({
      data: undefined,
    } as never)

    const onSuccess = vi.fn()
    const { result } = renderHook(() => useDeletePrompt({ projectId: 'proj-1', onSuccess }), {
      wrapper: createWrapper(),
    })

    await act(async () => {
      await result.current.mutateAsync({ name: 'prompt-1', projectId: 'proj-1' })
    })

    expect(AgentPrompts.deleteApiAgentPromptsByNameByName).toHaveBeenCalledWith({
      path: { name: 'prompt-1' },
      query: { projectId: 'proj-1' },
    })
    expect(onSuccess).toHaveBeenCalled()
  })

  it('handles API errors', async () => {
    vi.mocked(AgentPrompts.deleteApiAgentPromptsByNameByName).mockResolvedValue({
      error: { detail: 'Not found' },
    } as never)

    const onError = vi.fn()
    const { result } = renderHook(() => useDeletePrompt({ projectId: 'proj-1', onError }), {
      wrapper: createWrapper(),
    })

    await act(async () => {
      try {
        await result.current.mutateAsync({ name: 'invalid-id', projectId: 'proj-1' })
      } catch {
        // Expected to throw
      }
    })

    await waitFor(() => expect(onError).toHaveBeenCalled())
  })

  it('invalidates global prompts query key when no projectId is provided', async () => {
    vi.mocked(AgentPrompts.deleteApiAgentPromptsByNameByName).mockResolvedValue({
      data: undefined,
    } as never)

    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    })
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries')

    const { result } = renderHook(() => useDeletePrompt({ onSuccess: vi.fn() }), {
      wrapper: createWrapper(queryClient),
    })

    await act(async () => {
      await result.current.mutateAsync({ name: 'prompt-1' })
    })

    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: globalPromptsQueryKey(),
    })
  })
})
