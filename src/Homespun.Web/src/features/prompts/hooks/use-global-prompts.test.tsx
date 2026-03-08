import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useGlobalPrompts } from './use-global-prompts'
import { AgentPrompts } from '@/api'
import type { AgentPrompt } from '@/api/generated/types.gen'

// Mock the API module
vi.mock('@/api', () => ({
  AgentPrompts: {
    getApiAgentPrompts: vi.fn(),
  },
}))

describe('useGlobalPrompts', () => {
  let queryClient: QueryClient

  beforeEach(() => {
    queryClient = new QueryClient({
      defaultOptions: {
        queries: {
          retry: false,
        },
      },
    })
    vi.clearAllMocks()
  })

  const wrapper = ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  )

  it('fetches all global prompts', async () => {
    const mockPrompts: AgentPrompt[] = [
      {
        id: '1',
        name: 'Global Prompt 1',
        initialMessage: 'Test message',
        mode: 0 as const,
        projectId: null,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      },
      {
        id: '2',
        name: 'Project Prompt',
        initialMessage: 'Project message',
        mode: 1 as const,
        projectId: 'project-123',
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      },
      {
        id: '3',
        name: 'Global Prompt 2',
        initialMessage: 'Another message',
        mode: 0 as const,
        projectId: null,
        createdAt: new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      },
    ]

    vi.mocked(AgentPrompts.getApiAgentPrompts).mockResolvedValue({
      data: mockPrompts,
      error: undefined,
      request: new Request('http://localhost/api/test'),
      response: new Response(),
    })

    const { result } = renderHook(() => useGlobalPrompts(), { wrapper })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))

    // Should only return prompts where projectId is null
    expect(result.current.data).toHaveLength(2)
    expect(result.current.data).toEqual([mockPrompts[0], mockPrompts[2]])
    expect(result.current.data?.every((p) => p.projectId === null)).toBe(true)
  })

  it('handles loading state correctly', () => {
    vi.mocked(AgentPrompts.getApiAgentPrompts).mockImplementation(
      () => new Promise<never>(() => {}) // Never resolves
    )

    const { result } = renderHook(() => useGlobalPrompts(), { wrapper })

    expect(result.current.isLoading).toBe(true)
    expect(result.current.data).toBeUndefined()
    expect(result.current.error).toBeNull()
  })

  it('handles error state correctly', async () => {
    const errorMessage = 'Failed to fetch prompts'
    vi.mocked(AgentPrompts.getApiAgentPrompts).mockRejectedValue(new Error(errorMessage))

    const { result } = renderHook(() => useGlobalPrompts(), { wrapper })

    await waitFor(() => expect(result.current.isError).toBe(true))

    expect(result.current.error).toBeInstanceOf(Error)
    expect(result.current.error?.message).toBe(errorMessage)
    expect(result.current.data).toBeUndefined()
  })

  it('handles API error response correctly', async () => {
    vi.mocked(AgentPrompts.getApiAgentPrompts).mockResolvedValue({
      data: undefined,
      error: { detail: 'API Error: Unauthorized' },
      request: new Request('http://localhost/api/test'),
      response: new Response(),
    })

    const { result } = renderHook(() => useGlobalPrompts(), { wrapper })

    await waitFor(() => expect(result.current.isError).toBe(true))

    expect(result.current.error).toBeInstanceOf(Error)
    expect(result.current.error?.message).toBe('API Error: Unauthorized')
  })
})
