import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement, type ReactNode } from 'react'
import { Sessions } from '@/api'
import type { SessionSummary } from '@/api/generated'
import { ClaudeSessionStatus, SessionMode } from '@/api/generated'
import { useSessions, useStopSession, sessionsQueryKey } from './use-sessions'

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
