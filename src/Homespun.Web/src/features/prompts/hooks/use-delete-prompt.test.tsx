import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor, act } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useDeletePrompt } from './use-delete-prompt'
import { AgentPrompts } from '@/api'

vi.mock('@/api', () => ({
  AgentPrompts: {
    deleteApiAgentPromptsById: vi.fn(),
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

describe('useDeletePrompt', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('deletes a prompt successfully', async () => {
    vi.mocked(AgentPrompts.deleteApiAgentPromptsById).mockResolvedValue({
      data: undefined,
    } as never)

    const onSuccess = vi.fn()
    const { result } = renderHook(() => useDeletePrompt({ projectId: 'proj-1', onSuccess }), {
      wrapper: createWrapper(),
    })

    await act(async () => {
      await result.current.mutateAsync('prompt-1')
    })

    expect(AgentPrompts.deleteApiAgentPromptsById).toHaveBeenCalledWith({
      path: { id: 'prompt-1' },
    })
    expect(onSuccess).toHaveBeenCalled()
  })

  it('handles API errors', async () => {
    vi.mocked(AgentPrompts.deleteApiAgentPromptsById).mockResolvedValue({
      error: { detail: 'Not found' },
    } as never)

    const onError = vi.fn()
    const { result } = renderHook(() => useDeletePrompt({ projectId: 'proj-1', onError }), {
      wrapper: createWrapper(),
    })

    await act(async () => {
      try {
        await result.current.mutateAsync('invalid-id')
      } catch {
        // Expected to throw
      }
    })

    await waitFor(() => expect(onError).toHaveBeenCalled())
  })
})
