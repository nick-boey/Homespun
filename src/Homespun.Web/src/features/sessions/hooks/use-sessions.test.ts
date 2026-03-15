import { describe, it, expect, vi, beforeEach, afterEach, type Mock } from 'vitest'
import { renderHook, waitFor, act } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement, type ReactNode } from 'react'
import { Sessions } from '@/api'
import type { SessionSummary } from '@/api/generated'
import { ClaudeSessionStatus, SessionMode } from '@/api/generated'
import {
  useSessions,
  useStopSession,
  sessionsQueryKey,
  invalidateAllSessionsQueries,
  allSessionsCountQueryKey,
} from './use-sessions'

vi.mock('@/api', () => ({
  Sessions: {
    getApiSessions: vi.fn(),
    deleteApiSessionsById: vi.fn(),
  },
}))

const mockSessions: SessionSummary[] = [
  {
    id: 'session-1',
    entityId: 'issue-1',
    projectId: 'project-1',
    model: 'sonnet',
    mode: SessionMode.BUILD,
    status: ClaudeSessionStatus.RUNNING,
    createdAt: '2024-01-01T10:00:00Z',
    lastActivityAt: '2024-01-01T10:30:00Z',
    messageCount: 15,
    totalCostUsd: 0.25,
    containerId: 'container-1',
    containerName: 'session-container-1',
  },
  {
    id: 'session-2',
    entityId: 'issue-2',
    projectId: 'project-1',
    model: 'opus',
    mode: SessionMode.PLAN,
    status: ClaudeSessionStatus.STOPPED,
    createdAt: '2024-01-01T09:00:00Z',
    lastActivityAt: '2024-01-01T09:15:00Z',
    messageCount: 5,
    totalCostUsd: 0.1,
    containerId: null,
    containerName: null,
  },
]

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  })
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: queryClient }, children)
}

describe('useSessions', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('fetches sessions successfully', async () => {
    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockResolvedValueOnce({ data: mockSessions })

    const { result } = renderHook(() => useSessions(), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(true)

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(result.current.data).toEqual(mockSessions)
    expect(mockGetApiSessions).toHaveBeenCalledTimes(1)
  })

  it('handles error when fetching sessions fails', async () => {
    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockRejectedValueOnce(new Error('Network error'))

    const { result } = renderHook(() => useSessions(), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })

    expect(result.current.error).toBeDefined()
  })

  it('returns empty array when no sessions exist', async () => {
    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockResolvedValueOnce({ data: [] })

    const { result } = renderHook(() => useSessions(), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(result.current.data).toEqual([])
  })

  it('exports correct query key', () => {
    expect(sessionsQueryKey).toEqual(['sessions'])
  })
})

describe('useStopSession', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('stops session successfully', async () => {
    const mockDeleteApiSessionsById = Sessions.deleteApiSessionsById as Mock
    mockDeleteApiSessionsById.mockResolvedValueOnce({})

    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
      },
    })
    const wrapper = ({ children }: { children: ReactNode }) =>
      createElement(QueryClientProvider, { client: queryClient }, children)

    const { result } = renderHook(() => useStopSession(), { wrapper })

    result.current.mutate('session-1')

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(mockDeleteApiSessionsById).toHaveBeenCalledWith({
      path: { id: 'session-1' },
    })
  })

  it('handles error when stopping session fails', async () => {
    const mockDeleteApiSessionsById = Sessions.deleteApiSessionsById as Mock
    mockDeleteApiSessionsById.mockRejectedValueOnce(new Error('Stop failed'))

    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
      },
    })
    const wrapper = ({ children }: { children: ReactNode }) =>
      createElement(QueryClientProvider, { client: queryClient }, children)

    const { result } = renderHook(() => useStopSession(), { wrapper })

    result.current.mutate('session-1')

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })

    expect(result.current.error).toBeDefined()
  })
})

describe('invalidateAllSessionsQueries', () => {
  it('invalidates sessions query key', async () => {
    const queryClient = new QueryClient()
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries')

    await invalidateAllSessionsQueries(queryClient)

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: sessionsQueryKey })
  })

  it('invalidates all-sessions-count query key', async () => {
    const queryClient = new QueryClient()
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries')

    await invalidateAllSessionsQueries(queryClient)

    expect(invalidateSpy).toHaveBeenCalledWith({ queryKey: allSessionsCountQueryKey })
  })

  it('invalidates project-sessions queries via predicate', async () => {
    const queryClient = new QueryClient()
    const invalidateSpy = vi.spyOn(queryClient, 'invalidateQueries')

    await invalidateAllSessionsQueries(queryClient)

    // Check that predicate-based invalidation was called
    const predicateCall = invalidateSpy.mock.calls.find(
      (call) => typeof call[0]?.predicate === 'function'
    )
    expect(predicateCall).toBeDefined()

    // Test the predicate function
    const predicate = predicateCall![0]!.predicate!
    expect(predicate({ queryKey: ['project-sessions', 'project-1'] } as never)).toBe(true)
    expect(predicate({ queryKey: ['project-sessions', 'project-2'] } as never)).toBe(true)
    expect(predicate({ queryKey: ['other-query'] } as never)).toBe(false)
  })
})

describe('useSessions polling', () => {
  beforeEach(() => {
    vi.useFakeTimers()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('refetches every 5 seconds', async () => {
    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockResolvedValue({ data: mockSessions })

    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
      },
    })
    const wrapper = ({ children }: { children: ReactNode }) =>
      createElement(QueryClientProvider, { client: queryClient }, children)

    renderHook(() => useSessions(), { wrapper })

    // Wait for initial fetch
    await act(async () => {
      await vi.advanceTimersByTimeAsync(0)
    })

    expect(mockGetApiSessions).toHaveBeenCalledTimes(1)

    // Advance 5 seconds
    await act(async () => {
      await vi.advanceTimersByTimeAsync(5000)
    })

    expect(mockGetApiSessions).toHaveBeenCalledTimes(2)

    // Advance another 5 seconds
    await act(async () => {
      await vi.advanceTimersByTimeAsync(5000)
    })

    expect(mockGetApiSessions).toHaveBeenCalledTimes(3)
  })
})
