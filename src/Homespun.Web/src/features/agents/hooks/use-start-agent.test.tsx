import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useStartAgent } from './use-start-agent'
import { Sessions } from '@/api'
import type { ReactNode } from 'react'
import type { ClaudeSession } from '@/api/generated/types.gen'

vi.mock('@/api', () => ({
  Sessions: {
    postApiSessions: vi.fn(),
  },
}))

const mockPostApiSessions = vi.mocked(Sessions.postApiSessions)

// Helper to create mock API response
function createMockResponse<T>(data: T) {
  return {
    data,
    request: new Request('http://localhost/api/test'),
    response: new Response(),
  }
}

// Helper to create a valid ClaudeSession
function createMockSession(overrides: Partial<ClaudeSession> = {}): ClaudeSession {
  return {
    id: 'session-123',
    entityId: 'issue-456',
    projectId: 'project-789',
    workingDirectory: '/workdir',
    model: 'sonnet',
    mode: 1 as const, // Build mode
    status: 0 as const,
    ...overrides,
  }
}

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  })
  return function Wrapper({ children }: { children: ReactNode }) {
    return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  }
}

describe('useStartAgent', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('calls postApiSessions with correct parameters when starting an agent', async () => {
    mockPostApiSessions.mockResolvedValueOnce(createMockResponse(createMockSession()))

    const { result } = renderHook(() => useStartAgent(), {
      wrapper: createWrapper(),
    })

    result.current.mutate({
      entityId: 'issue-456',
      projectId: 'project-789',
      mode: 1,
      model: 'sonnet',
      workingDirectory: '/workdir',
      systemPrompt: 'Test prompt',
    })

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(mockPostApiSessions).toHaveBeenCalledWith({
      body: {
        entityId: 'issue-456',
        projectId: 'project-789',
        mode: 1,
        model: 'sonnet',
        workingDirectory: '/workdir',
        systemPrompt: 'Test prompt',
        initialMessage: undefined,
      },
    })
  })

  it('returns session data on success', async () => {
    const mockSession = createMockSession()

    mockPostApiSessions.mockResolvedValueOnce(createMockResponse(mockSession))

    const { result } = renderHook(() => useStartAgent(), {
      wrapper: createWrapper(),
    })

    result.current.mutate({
      entityId: 'issue-456',
      projectId: 'project-789',
      mode: 1,
      model: 'sonnet',
      workingDirectory: '/workdir',
    })

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(result.current.data).toEqual(mockSession)
  })

  it('handles error when session creation fails', async () => {
    mockPostApiSessions.mockRejectedValueOnce(new Error('Session creation failed'))

    const { result } = renderHook(() => useStartAgent(), {
      wrapper: createWrapper(),
    })

    result.current.mutate({
      entityId: 'issue-456',
      projectId: 'project-789',
      mode: 1,
      model: 'sonnet',
      workingDirectory: '/workdir',
    })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })

    expect(result.current.error).toBeInstanceOf(Error)
  })

  it('passes initialMessage to start agent work immediately', async () => {
    mockPostApiSessions.mockResolvedValueOnce(createMockResponse(createMockSession()))

    const { result } = renderHook(() => useStartAgent(), {
      wrapper: createWrapper(),
    })

    result.current.mutate({
      entityId: 'issue-456',
      projectId: 'project-789',
      mode: 1,
      model: 'sonnet',
      workingDirectory: '/workdir',
      initialMessage: 'Build the feature',
    })

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(mockPostApiSessions).toHaveBeenCalledWith({
      body: {
        entityId: 'issue-456',
        projectId: 'project-789',
        mode: 1,
        model: 'sonnet',
        workingDirectory: '/workdir',
        systemPrompt: undefined,
        initialMessage: 'Build the feature',
      },
    })
  })

  it('uses default mode and model when not specified', async () => {
    mockPostApiSessions.mockResolvedValueOnce(createMockResponse(createMockSession()))

    const { result } = renderHook(() => useStartAgent(), {
      wrapper: createWrapper(),
    })

    result.current.mutate({
      entityId: 'issue-456',
      projectId: 'project-789',
      workingDirectory: '/workdir',
    })

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    // Should use default values
    expect(mockPostApiSessions).toHaveBeenCalledWith({
      body: {
        entityId: 'issue-456',
        projectId: 'project-789',
        workingDirectory: '/workdir',
        mode: undefined,
        model: undefined,
        systemPrompt: undefined,
        initialMessage: undefined,
      },
    })
  })
})
