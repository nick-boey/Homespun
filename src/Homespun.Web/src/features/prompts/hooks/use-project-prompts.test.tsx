import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useProjectPrompts, useAvailableGlobalPrompts } from './use-project-prompts'
import { AgentPrompts } from '@/api'
import type { ReactNode } from 'react'
import type { AgentPrompt } from '@/api/generated/types.gen'

vi.mock('@/api', () => ({
  AgentPrompts: {
    getApiAgentPromptsProjectByProjectId: vi.fn(),
    getApiAgentPromptsAvailableForProjectByProjectId: vi.fn(),
    postApiAgentPrompts: vi.fn(),
    putApiAgentPromptsById: vi.fn(),
    deleteApiAgentPromptsById: vi.fn(),
  },
}))

const mockGetProjectPrompts = vi.mocked(AgentPrompts.getApiAgentPromptsProjectByProjectId)
const mockGetAvailablePrompts = vi.mocked(
  AgentPrompts.getApiAgentPromptsAvailableForProjectByProjectId
)

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

describe('useProjectPrompts', () => {
  const mockPrompts: AgentPrompt[] = [
    {
      id: 'prompt-1',
      name: 'Test Prompt',
      initialMessage: 'Test message',
      mode: 1,
      projectId: 'project-1',
    },
  ]

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('should fetch project prompts', async () => {
    mockGetProjectPrompts.mockResolvedValueOnce(createMockResponse(mockPrompts))

    const { result } = renderHook(() => useProjectPrompts('project-1'), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(true)

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.prompts).toEqual(mockPrompts)
    expect(mockGetProjectPrompts).toHaveBeenCalledWith({
      path: { projectId: 'project-1' },
    })
  })

  it('should not fetch if projectId is empty', async () => {
    const { result } = renderHook(() => useProjectPrompts(''), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(false)
    expect(mockGetProjectPrompts).not.toHaveBeenCalled()
  })

  it('should handle errors', async () => {
    mockGetProjectPrompts.mockRejectedValueOnce(new Error('API Error'))

    const { result } = renderHook(() => useProjectPrompts('project-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })
  })
})

describe('useAvailableGlobalPrompts', () => {
  const mockPrompts: AgentPrompt[] = [
    {
      id: 'global-1',
      name: 'Global Prompt',
      initialMessage: 'Global message',
      mode: 1,
      projectId: null,
    },
    {
      id: 'project-1',
      name: 'Project Prompt',
      initialMessage: 'Project message',
      mode: 1,
      projectId: 'project-1',
    },
  ]

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('should filter to only global prompts', async () => {
    mockGetAvailablePrompts.mockResolvedValueOnce(createMockResponse(mockPrompts))

    const { result } = renderHook(() => useAvailableGlobalPrompts('project-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.globalPrompts).toHaveLength(1)
    expect(result.current.globalPrompts[0].name).toBe('Global Prompt')
  })
})
