import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor, act } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useCreateOverride } from './use-create-override'
import { AgentPrompts } from '@/api'
import { SessionMode } from '@/api/generated/types.gen'

vi.mock('@/api', () => ({
  AgentPrompts: {
    postApiAgentPromptsCreateOverride: vi.fn(),
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

describe('useCreateOverride', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('creates an override prompt successfully', async () => {
    const overridePrompt = {
      id: 'override-1',
      name: 'Build',
      initialMessage: 'Custom message',
      mode: SessionMode.BUILD,
      projectId: 'proj-1',
      isOverride: true,
    }
    vi.mocked(AgentPrompts.postApiAgentPromptsCreateOverride).mockResolvedValue({
      data: overridePrompt,
    } as never)

    const onSuccess = vi.fn()
    const { result } = renderHook(() => useCreateOverride({ projectId: 'proj-1', onSuccess }), {
      wrapper: createWrapper(),
    })

    await act(async () => {
      await result.current.mutateAsync({
        globalPromptId: 'global-build',
        projectId: 'proj-1',
        initialMessage: 'Custom message',
      })
    })

    expect(AgentPrompts.postApiAgentPromptsCreateOverride).toHaveBeenCalledWith({
      body: {
        globalPromptId: 'global-build',
        projectId: 'proj-1',
        initialMessage: 'Custom message',
      },
    })
    expect(onSuccess).toHaveBeenCalledWith(overridePrompt)
  })

  it('creates override without custom message (copies from global)', async () => {
    const overridePrompt = {
      id: 'override-1',
      name: 'Plan',
      initialMessage: 'Global plan message',
      mode: SessionMode.PLAN,
      projectId: 'proj-1',
    }
    vi.mocked(AgentPrompts.postApiAgentPromptsCreateOverride).mockResolvedValue({
      data: overridePrompt,
    } as never)

    const { result } = renderHook(() => useCreateOverride({ projectId: 'proj-1' }), {
      wrapper: createWrapper(),
    })

    await act(async () => {
      await result.current.mutateAsync({
        globalPromptId: 'global-plan',
        projectId: 'proj-1',
      })
    })

    expect(AgentPrompts.postApiAgentPromptsCreateOverride).toHaveBeenCalledWith({
      body: {
        globalPromptId: 'global-plan',
        projectId: 'proj-1',
      },
    })
  })

  it('handles API errors', async () => {
    vi.mocked(AgentPrompts.postApiAgentPromptsCreateOverride).mockResolvedValue({
      error: { detail: 'Global prompt not found' },
    } as never)

    const onError = vi.fn()
    const { result } = renderHook(() => useCreateOverride({ projectId: 'proj-1', onError }), {
      wrapper: createWrapper(),
    })

    await act(async () => {
      try {
        await result.current.mutateAsync({
          globalPromptId: 'non-existent',
          projectId: 'proj-1',
        })
      } catch {
        // Expected to throw
      }
    })

    await waitFor(() => expect(onError).toHaveBeenCalled())
  })
})
