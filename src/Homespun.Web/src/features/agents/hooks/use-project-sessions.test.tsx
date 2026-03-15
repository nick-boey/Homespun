import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useProjectSessions, useActiveSessionCount } from './use-project-sessions'
import { Sessions, SessionMode, ClaudeSessionStatus } from '@/api'
import type { ReactNode } from 'react'
import type { SessionSummary } from '@/api/generated/types.gen'

vi.mock('@/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api')>()
  return {
    ...actual,
    Sessions: {
      getApiSessionsProjectByProjectId: vi.fn(),
    },
  }
})

const mockGetProjectSessions = vi.mocked(Sessions.getApiSessionsProjectByProjectId)

// Helper to create mock API response
function createMockResponse<T>(data: T) {
  return {
    data,
    request: new Request('http://localhost/api/test'),
    response: new Response(),
  }
}

// Helper to create a valid SessionSummary
function createSessionSummary(overrides: Partial<SessionSummary> = {}): SessionSummary {
  return {
    id: 'session-1',
    entityId: 'entity-1',
    projectId: 'project-123',
    model: 'sonnet',
    mode: SessionMode.BUILD,
    status: ClaudeSessionStatus.RUNNING,
    ...overrides,
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

describe('useProjectSessions', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('fetches sessions for a project', async () => {
    const mockSessions: SessionSummary[] = [
      createSessionSummary({
        id: 'session-1',
        entityId: 'issue-1',
        status: ClaudeSessionStatus.RUNNING,
      }),
      createSessionSummary({
        id: 'session-2',
        entityId: 'issue-2',
        model: 'opus',
        status: ClaudeSessionStatus.WAITING_FOR_INPUT,
      }),
    ]

    mockGetProjectSessions.mockResolvedValueOnce(createMockResponse(mockSessions))

    const { result } = renderHook(() => useProjectSessions('project-123'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(result.current.data).toEqual(mockSessions)
    expect(mockGetProjectSessions).toHaveBeenCalledWith({
      path: { projectId: 'project-123' },
    })
  })

  it('is disabled when projectId is not provided', () => {
    const { result } = renderHook(() => useProjectSessions(''), {
      wrapper: createWrapper(),
    })

    expect(result.current.isFetching).toBe(false)
    expect(mockGetProjectSessions).not.toHaveBeenCalled()
  })
})

describe('useActiveSessionCount', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('returns count of active sessions', async () => {
    const mockSessions: SessionSummary[] = [
      createSessionSummary({ id: 'session-1', status: ClaudeSessionStatus.RUNNING }), // active
      createSessionSummary({ id: 'session-2', status: ClaudeSessionStatus.WAITING_FOR_INPUT }), // active
      createSessionSummary({ id: 'session-3', status: ClaudeSessionStatus.STOPPED }), // not active
    ]

    mockGetProjectSessions.mockResolvedValueOnce(createMockResponse(mockSessions))

    const { result } = renderHook(() => useActiveSessionCount('project-123'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.count).toBe(2)
    })
  })

  it('returns hasActive as true when there are active sessions', async () => {
    const mockSessions: SessionSummary[] = [
      createSessionSummary({ id: 'session-1', status: ClaudeSessionStatus.RUNNING }),
    ]

    mockGetProjectSessions.mockResolvedValueOnce(createMockResponse(mockSessions))

    const { result } = renderHook(() => useActiveSessionCount('project-123'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.hasActive).toBe(true)
    })
  })

  it('returns isProcessing as true when sessions are running', async () => {
    const mockSessions: SessionSummary[] = [
      createSessionSummary({ id: 'session-1', status: ClaudeSessionStatus.RUNNING }),
    ]

    mockGetProjectSessions.mockResolvedValueOnce(createMockResponse(mockSessions))

    const { result } = renderHook(() => useActiveSessionCount('project-123'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isProcessing).toBe(true)
    })
  })

  it('returns isProcessing as false when sessions are waiting', async () => {
    const mockSessions: SessionSummary[] = [
      createSessionSummary({ id: 'session-1', status: ClaudeSessionStatus.WAITING_FOR_INPUT }),
    ]

    mockGetProjectSessions.mockResolvedValueOnce(createMockResponse(mockSessions))

    const { result } = renderHook(() => useActiveSessionCount('project-123'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isProcessing).toBe(false)
    })
  })
})
