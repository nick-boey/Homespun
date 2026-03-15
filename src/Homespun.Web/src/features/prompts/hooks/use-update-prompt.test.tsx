import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor, act } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useUpdatePrompt } from './use-update-prompt'
import { AgentPrompts } from '@/api'
import { SessionMode } from '@/api/generated/types.gen'

vi.mock('@/api', () => ({
  AgentPrompts: {
    putApiAgentPromptsById: vi.fn(),
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

describe('useUpdatePrompt', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('updates a prompt successfully', async () => {
    const updatedPrompt = {
      id: 'prompt-1',
      name: 'Updated Prompt',
      initialMessage: 'Updated message',
      mode: SessionMode.BUILD,
      projectId: 'proj-1',
    }
    vi.mocked(AgentPrompts.putApiAgentPromptsById).mockResolvedValue({
      data: updatedPrompt,
    } as never)

    const onSuccess = vi.fn()
    const { result } = renderHook(() => useUpdatePrompt({ projectId: 'proj-1', onSuccess }), {
      wrapper: createWrapper(),
    })

    await act(async () => {
      await result.current.mutateAsync({
        id: 'prompt-1',
        name: 'Updated Prompt',
        initialMessage: 'Updated message',
        mode: SessionMode.BUILD,
      })
    })

    expect(AgentPrompts.putApiAgentPromptsById).toHaveBeenCalledWith({
      path: { id: 'prompt-1' },
      body: {
        name: 'Updated Prompt',
        initialMessage: 'Updated message',
        mode: SessionMode.BUILD,
      },
    })
    expect(onSuccess).toHaveBeenCalledWith(updatedPrompt)
  })

  it('handles API errors', async () => {
    vi.mocked(AgentPrompts.putApiAgentPromptsById).mockResolvedValue({
      error: { detail: 'Not found' },
    } as never)

    const onError = vi.fn()
    const { result } = renderHook(() => useUpdatePrompt({ projectId: 'proj-1', onError }), {
      wrapper: createWrapper(),
    })

    await act(async () => {
      try {
        await result.current.mutateAsync({
          id: 'invalid-id',
          name: 'Test',
        })
      } catch {
        // Expected to throw
      }
    })

    await waitFor(() => expect(onError).toHaveBeenCalled())
  })
})
