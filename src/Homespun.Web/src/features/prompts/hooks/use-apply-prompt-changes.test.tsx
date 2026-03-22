import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor, act } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useApplyPromptChanges } from './use-apply-prompt-changes'
import { AgentPrompts } from '@/api'
import { SessionMode } from '@/api/generated/types.gen'
import type { PromptChanges } from '../utils/prompt-diff'

vi.mock('@/api', () => ({
  AgentPrompts: {
    postApiAgentPrompts: vi.fn(),
    putApiAgentPromptsById: vi.fn(),
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

describe('useApplyPromptChanges', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('creates prompts for items in creates array', async () => {
    vi.mocked(AgentPrompts.postApiAgentPrompts).mockResolvedValue({
      data: { id: 'new-1', name: 'New Prompt', mode: SessionMode.BUILD },
    } as never)

    const onSuccess = vi.fn()
    const { result } = renderHook(() => useApplyPromptChanges({ projectId: 'proj-1', onSuccess }), {
      wrapper: createWrapper(),
    })

    const changes: PromptChanges = {
      creates: [{ name: 'New Prompt', mode: SessionMode.BUILD }],
      updates: [],
      deletes: [],
    }

    await act(async () => {
      await result.current.mutateAsync(changes)
    })

    expect(AgentPrompts.postApiAgentPrompts).toHaveBeenCalledWith({
      body: {
        name: 'New Prompt',
        mode: SessionMode.BUILD,
        projectId: 'proj-1',
      },
    })
    expect(onSuccess).toHaveBeenCalled()
  })

  it('updates prompts for items in updates array', async () => {
    vi.mocked(AgentPrompts.putApiAgentPromptsById).mockResolvedValue({
      data: { id: 'prompt-1', name: 'Updated Prompt', mode: SessionMode.PLAN },
    } as never)

    const onSuccess = vi.fn()
    const { result } = renderHook(() => useApplyPromptChanges({ projectId: 'proj-1', onSuccess }), {
      wrapper: createWrapper(),
    })

    const changes: PromptChanges = {
      creates: [],
      updates: [{ id: 'prompt-1', name: 'Updated Prompt', mode: SessionMode.PLAN }],
      deletes: [],
    }

    await act(async () => {
      await result.current.mutateAsync(changes)
    })

    expect(AgentPrompts.putApiAgentPromptsById).toHaveBeenCalledWith({
      path: { id: 'prompt-1' },
      body: {
        name: 'Updated Prompt',
        mode: SessionMode.PLAN,
      },
    })
    expect(onSuccess).toHaveBeenCalled()
  })

  it('deletes prompts for items in deletes array', async () => {
    vi.mocked(AgentPrompts.deleteApiAgentPromptsById).mockResolvedValue({
      data: {},
    } as never)

    const onSuccess = vi.fn()
    const { result } = renderHook(() => useApplyPromptChanges({ projectId: 'proj-1', onSuccess }), {
      wrapper: createWrapper(),
    })

    const changes: PromptChanges = {
      creates: [],
      updates: [],
      deletes: ['prompt-1', 'prompt-2'],
    }

    await act(async () => {
      await result.current.mutateAsync(changes)
    })

    expect(AgentPrompts.deleteApiAgentPromptsById).toHaveBeenCalledTimes(2)
    expect(AgentPrompts.deleteApiAgentPromptsById).toHaveBeenCalledWith({
      path: { id: 'prompt-1' },
    })
    expect(AgentPrompts.deleteApiAgentPromptsById).toHaveBeenCalledWith({
      path: { id: 'prompt-2' },
    })
    expect(onSuccess).toHaveBeenCalled()
  })

  it('executes creates, updates, and deletes in order', async () => {
    const callOrder: string[] = []

    vi.mocked(AgentPrompts.postApiAgentPrompts).mockImplementation(async () => {
      callOrder.push('create')
      return { data: { id: 'new-1', name: 'New', mode: SessionMode.BUILD } } as never
    })

    vi.mocked(AgentPrompts.putApiAgentPromptsById).mockImplementation(async () => {
      callOrder.push('update')
      return { data: { id: 'existing-1', name: 'Updated', mode: SessionMode.PLAN } } as never
    })

    vi.mocked(AgentPrompts.deleteApiAgentPromptsById).mockImplementation(async () => {
      callOrder.push('delete')
      return { data: {} } as never
    })

    const { result } = renderHook(() => useApplyPromptChanges({ projectId: 'proj-1' }), {
      wrapper: createWrapper(),
    })

    const changes: PromptChanges = {
      creates: [{ name: 'New', mode: SessionMode.BUILD }],
      updates: [{ id: 'existing-1', name: 'Updated', mode: SessionMode.PLAN }],
      deletes: ['delete-1'],
    }

    await act(async () => {
      await result.current.mutateAsync(changes)
    })

    // Creates run first, then updates, then deletes
    expect(callOrder).toEqual(['create', 'update', 'delete'])
  })

  it('stops on first error during creates', async () => {
    vi.mocked(AgentPrompts.postApiAgentPrompts).mockResolvedValue({
      error: { detail: 'Create failed' },
    } as never)

    vi.mocked(AgentPrompts.putApiAgentPromptsById).mockResolvedValue({
      data: {},
    } as never)

    const onError = vi.fn()
    const { result } = renderHook(() => useApplyPromptChanges({ projectId: 'proj-1', onError }), {
      wrapper: createWrapper(),
    })

    const changes: PromptChanges = {
      creates: [{ name: 'New', mode: SessionMode.BUILD }],
      updates: [{ id: 'existing-1', name: 'Updated', mode: SessionMode.PLAN }],
      deletes: [],
    }

    await act(async () => {
      try {
        await result.current.mutateAsync(changes)
      } catch {
        // Expected to throw
      }
    })

    await waitFor(() => expect(onError).toHaveBeenCalled())
    // Updates should not have been called because create failed
    expect(AgentPrompts.putApiAgentPromptsById).not.toHaveBeenCalled()
  })

  it('stops on first error during updates', async () => {
    vi.mocked(AgentPrompts.postApiAgentPrompts).mockResolvedValue({
      data: { id: 'new-1', name: 'New', mode: SessionMode.BUILD },
    } as never)

    vi.mocked(AgentPrompts.putApiAgentPromptsById).mockResolvedValue({
      error: { detail: 'Update failed' },
    } as never)

    vi.mocked(AgentPrompts.deleteApiAgentPromptsById).mockResolvedValue({
      data: {},
    } as never)

    const onError = vi.fn()
    const { result } = renderHook(() => useApplyPromptChanges({ projectId: 'proj-1', onError }), {
      wrapper: createWrapper(),
    })

    const changes: PromptChanges = {
      creates: [{ name: 'New', mode: SessionMode.BUILD }],
      updates: [{ id: 'existing-1', name: 'Updated', mode: SessionMode.PLAN }],
      deletes: ['delete-1'],
    }

    await act(async () => {
      try {
        await result.current.mutateAsync(changes)
      } catch {
        // Expected to throw
      }
    })

    await waitFor(() => expect(onError).toHaveBeenCalled())
    // Create should have been called, but not delete
    expect(AgentPrompts.postApiAgentPrompts).toHaveBeenCalled()
    expect(AgentPrompts.deleteApiAgentPromptsById).not.toHaveBeenCalled()
  })

  it('uses null projectId for global prompts', async () => {
    vi.mocked(AgentPrompts.postApiAgentPrompts).mockResolvedValue({
      data: { id: 'new-1', name: 'Global Prompt', mode: SessionMode.BUILD },
    } as never)

    const { result } = renderHook(() => useApplyPromptChanges({ isGlobal: true }), {
      wrapper: createWrapper(),
    })

    const changes: PromptChanges = {
      creates: [{ name: 'Global Prompt', mode: SessionMode.BUILD }],
      updates: [],
      deletes: [],
    }

    await act(async () => {
      await result.current.mutateAsync(changes)
    })

    expect(AgentPrompts.postApiAgentPrompts).toHaveBeenCalledWith({
      body: {
        name: 'Global Prompt',
        mode: SessionMode.BUILD,
        projectId: null,
      },
    })
  })

  it('handles empty changes gracefully', async () => {
    const onSuccess = vi.fn()
    const { result } = renderHook(() => useApplyPromptChanges({ projectId: 'proj-1', onSuccess }), {
      wrapper: createWrapper(),
    })

    const changes: PromptChanges = {
      creates: [],
      updates: [],
      deletes: [],
    }

    await act(async () => {
      await result.current.mutateAsync(changes)
    })

    expect(AgentPrompts.postApiAgentPrompts).not.toHaveBeenCalled()
    expect(AgentPrompts.putApiAgentPromptsById).not.toHaveBeenCalled()
    expect(AgentPrompts.deleteApiAgentPromptsById).not.toHaveBeenCalled()
    expect(onSuccess).toHaveBeenCalled()
  })

  it('includes initialMessage in create request', async () => {
    vi.mocked(AgentPrompts.postApiAgentPrompts).mockResolvedValue({
      data: { id: 'new-1', name: 'Test', initialMessage: 'Hello', mode: SessionMode.BUILD },
    } as never)

    const { result } = renderHook(() => useApplyPromptChanges({ projectId: 'proj-1' }), {
      wrapper: createWrapper(),
    })

    const changes: PromptChanges = {
      creates: [{ name: 'Test', initialMessage: 'Hello', mode: SessionMode.BUILD }],
      updates: [],
      deletes: [],
    }

    await act(async () => {
      await result.current.mutateAsync(changes)
    })

    expect(AgentPrompts.postApiAgentPrompts).toHaveBeenCalledWith({
      body: {
        name: 'Test',
        initialMessage: 'Hello',
        mode: SessionMode.BUILD,
        projectId: 'proj-1',
      },
    })
  })

  it('includes initialMessage in update request', async () => {
    vi.mocked(AgentPrompts.putApiAgentPromptsById).mockResolvedValue({
      data: {
        id: 'prompt-1',
        name: 'Test',
        initialMessage: 'Updated Hello',
        mode: SessionMode.BUILD,
      },
    } as never)

    const { result } = renderHook(() => useApplyPromptChanges({ projectId: 'proj-1' }), {
      wrapper: createWrapper(),
    })

    const changes: PromptChanges = {
      creates: [],
      updates: [
        { id: 'prompt-1', name: 'Test', initialMessage: 'Updated Hello', mode: SessionMode.BUILD },
      ],
      deletes: [],
    }

    await act(async () => {
      await result.current.mutateAsync(changes)
    })

    expect(AgentPrompts.putApiAgentPromptsById).toHaveBeenCalledWith({
      path: { id: 'prompt-1' },
      body: {
        name: 'Test',
        initialMessage: 'Updated Hello',
        mode: SessionMode.BUILD,
      },
    })
  })
})
