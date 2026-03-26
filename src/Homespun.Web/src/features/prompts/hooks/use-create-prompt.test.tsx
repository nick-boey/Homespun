import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor, act } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useCreatePrompt } from './use-create-prompt'
import { AgentPrompts } from '@/api'
import { SessionMode } from '@/api/generated/types.gen'
import { globalPromptsQueryKey } from './use-global-prompts'

vi.mock('@/api', () => ({
  AgentPrompts: {
    postApiAgentPrompts: vi.fn(),
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

describe('useCreatePrompt', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('creates a prompt successfully', async () => {
    const createdPrompt = {
      id: 'new-prompt-1',
      name: 'New Prompt',
      initialMessage: 'System message',
      mode: SessionMode.BUILD,
      projectId: 'proj-1',
    }
    vi.mocked(AgentPrompts.postApiAgentPrompts).mockResolvedValue({
      data: createdPrompt,
    } as never)

    const onSuccess = vi.fn()
    const { result } = renderHook(() => useCreatePrompt({ projectId: 'proj-1', onSuccess }), {
      wrapper: createWrapper(),
    })

    await act(async () => {
      await result.current.mutateAsync({
        name: 'New Prompt',
        initialMessage: 'System message',
        mode: SessionMode.BUILD,
        projectId: 'proj-1',
      })
    })

    expect(AgentPrompts.postApiAgentPrompts).toHaveBeenCalledWith({
      body: {
        name: 'New Prompt',
        initialMessage: 'System message',
        mode: SessionMode.BUILD,
        projectId: 'proj-1',
      },
    })
    expect(onSuccess).toHaveBeenCalledWith(createdPrompt)
  })

  it('handles API errors', async () => {
    vi.mocked(AgentPrompts.postApiAgentPrompts).mockResolvedValue({
      error: { detail: 'Validation failed' },
    } as never)

    const onError = vi.fn()
    const { result } = renderHook(() => useCreatePrompt({ projectId: 'proj-1', onError }), {
      wrapper: createWrapper(),
    })

    await act(async () => {
      try {
        await result.current.mutateAsync({
          name: 'Invalid',
          projectId: 'proj-1',
        })
      } catch {
        // Expected to throw
      }
    })

    await waitFor(() => expect(onError).toHaveBeenCalled())
  })

  it('invalidates global prompts query key when no projectId is provided', async () => {
    const createdPrompt = {
      id: 'global-prompt-1',
      name: 'Global Prompt',
      initialMessage: 'Global message',
      mode: SessionMode.BUILD,
      projectId: null,
    }
    vi.mocked(AgentPrompts.postApiAgentPrompts).mockResolvedValue({
      data: createdPrompt,
    } as never)

    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    })
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries')

    const { result } = renderHook(() => useCreatePrompt({ onSuccess: vi.fn() }), {
      wrapper: createWrapper(queryClient),
    })

    await act(async () => {
      await result.current.mutateAsync({
        name: 'Global Prompt',
        initialMessage: 'Global message',
        mode: SessionMode.BUILD,
      })
    })

    expect(invalidateSpy).toHaveBeenCalledWith({
      queryKey: globalPromptsQueryKey(),
    })
  })
})
