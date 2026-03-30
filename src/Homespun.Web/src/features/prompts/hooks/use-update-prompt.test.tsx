import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor, act } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useUpdatePrompt } from './use-update-prompt'
import { AgentPrompts } from '@/api'
import { SessionMode } from '@/api/generated/types.gen'
import { globalPromptsQueryKey } from './use-global-prompts'

vi.mock('@/api', () => ({
  AgentPrompts: {
    putApiAgentPromptsByNameByName: vi.fn(),
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

describe('useUpdatePrompt', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('updates a prompt successfully', async () => {
    const updatedPrompt = {
      name: 'Updated Prompt',
      initialMessage: 'Updated message',
      mode: SessionMode.BUILD,
      projectId: 'proj-1',
    }
    vi.mocked(AgentPrompts.putApiAgentPromptsByNameByName).mockResolvedValue({
      data: updatedPrompt,
    } as never)

    const onSuccess = vi.fn()
    const { result } = renderHook(() => useUpdatePrompt({ projectId: 'proj-1', onSuccess }), {
      wrapper: createWrapper(),
    })

    await act(async () => {
      await result.current.mutateAsync({
        name: 'Updated Prompt',
        projectId: 'proj-1',
        initialMessage: 'Updated message',
        mode: SessionMode.BUILD,
      })
    })

    expect(AgentPrompts.putApiAgentPromptsByNameByName).toHaveBeenCalledWith({
      path: { name: 'Updated Prompt' },
      query: { projectId: 'proj-1' },
      body: {
        initialMessage: 'Updated message',
        mode: SessionMode.BUILD,
      },
    })
    expect(onSuccess).toHaveBeenCalledWith(updatedPrompt)
  })

  it('handles API errors', async () => {
    vi.mocked(AgentPrompts.putApiAgentPromptsByNameByName).mockResolvedValue({
      error: { detail: 'Not found' },
    } as never)

    const onError = vi.fn()
    const { result } = renderHook(() => useUpdatePrompt({ projectId: 'proj-1', onError }), {
      wrapper: createWrapper(),
    })

    await act(async () => {
      try {
        await result.current.mutateAsync({
          name: 'Test',
        })
      } catch {
        // Expected to throw
      }
    })

    await waitFor(() => expect(onError).toHaveBeenCalled())
  })

  it('invalidates global prompts query key when no projectId is provided', async () => {
    const updatedPrompt = {
      name: 'Updated Global Prompt',
      initialMessage: 'Updated message',
      mode: SessionMode.BUILD,
      projectId: null,
    }
    vi.mocked(AgentPrompts.putApiAgentPromptsByNameByName).mockResolvedValue({
      data: updatedPrompt,
    } as never)

    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    })
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries')

    const { result } = renderHook(() => useUpdatePrompt({ onSuccess: vi.fn() }), {
      wrapper: createWrapper(queryClient),
    })

    await act(async () => {
      await result.current.mutateAsync({
        name: 'Updated Global Prompt',
        initialMessage: 'Updated message',
        mode: SessionMode.BUILD,
      })
    })

    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: globalPromptsQueryKey(),
    })
  })
})
