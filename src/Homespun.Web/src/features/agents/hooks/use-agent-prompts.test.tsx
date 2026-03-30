import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useAgentPrompts } from './use-agent-prompts'
import { AgentPrompts, SessionMode } from '@/api'
import type { ReactNode } from 'react'
import type { AgentPrompt } from '@/api/generated/types.gen'

vi.mock('@/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api')>()
  return {
    ...actual,
    AgentPrompts: {
      getApiAgentPromptsAvailableForProjectByProjectId: vi.fn(),
    },
  }
})

const mockGetAgentPrompts = vi.mocked(AgentPrompts.getApiAgentPromptsAvailableForProjectByProjectId)

// Helper to create mock API response
function createMockResponse<T>(data: T) {
  return {
    data,
    request: new Request('http://localhost/api/test'),
    response: new Response(),
  }
}

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
    },
  })
  return function Wrapper({ children }: { children: ReactNode }) {
    return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  }
}

describe('useAgentPrompts', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('fetches agent prompts for a project', async () => {
    const mockPrompts: AgentPrompt[] = [
      {
        name: 'Build Prompt',
        initialMessage: 'Start building...',
        mode: SessionMode.BUILD,
        projectId: 'project-123',
      },
      {
        name: 'Plan Prompt',
        initialMessage: 'Create a plan...',
        mode: SessionMode.PLAN,
        projectId: null, // Global prompt
      },
    ]

    mockGetAgentPrompts.mockResolvedValueOnce(createMockResponse(mockPrompts))

    const { result } = renderHook(() => useAgentPrompts('project-123'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(result.current.data).toEqual(mockPrompts)
    expect(mockGetAgentPrompts).toHaveBeenCalledWith({
      path: { projectId: 'project-123' },
    })
  })

  it('returns empty array when no prompts exist', async () => {
    mockGetAgentPrompts.mockResolvedValueOnce(createMockResponse<AgentPrompt[]>([]))

    const { result } = renderHook(() => useAgentPrompts('project-123'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(result.current.data).toEqual([])
  })

  it('is disabled when projectId is not provided', () => {
    const { result } = renderHook(() => useAgentPrompts(''), {
      wrapper: createWrapper(),
    })

    expect(result.current.isFetching).toBe(false)
    expect(mockGetAgentPrompts).not.toHaveBeenCalled()
  })
})
