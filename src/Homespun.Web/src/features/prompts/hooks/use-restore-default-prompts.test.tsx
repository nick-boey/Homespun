import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor, act } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useRestoreDefaultPrompts } from './use-restore-default-prompts'
import { AgentPrompts } from '@/api'

vi.mock('@/api', () => ({
  AgentPrompts: {
    postApiAgentPromptsRestoreDefaults: vi.fn(),
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

describe('useRestoreDefaultPrompts', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('restores default prompts successfully', async () => {
    vi.mocked(AgentPrompts.postApiAgentPromptsRestoreDefaults).mockResolvedValue({
      data: undefined,
    } as never)

    const onSuccess = vi.fn()
    const { result } = renderHook(() => useRestoreDefaultPrompts({ onSuccess }), {
      wrapper: createWrapper(),
    })

    await act(async () => {
      await result.current.mutateAsync()
    })

    expect(AgentPrompts.postApiAgentPromptsRestoreDefaults).toHaveBeenCalled()
    expect(onSuccess).toHaveBeenCalled()
  })

  it('handles API errors', async () => {
    vi.mocked(AgentPrompts.postApiAgentPromptsRestoreDefaults).mockResolvedValue({
      error: { detail: 'Server error' },
    } as never)

    const onError = vi.fn()
    const { result } = renderHook(() => useRestoreDefaultPrompts({ onError }), {
      wrapper: createWrapper(),
    })

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
