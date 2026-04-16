import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement, type ReactNode } from 'react'
import { Sessions } from '@/api'
import type { ResumableSession } from '@/api/generated'
import { SessionMode } from '@/api/generated'
import { useSessionHistory, sessionHistoryQueryKey } from './use-session-history'

vi.mock('@/api', () => ({
  Sessions: {
    getApiSessionsEntityByEntityIdResumable: vi.fn(),
  },
}))

const mockSessionHistory: ResumableSession[] = [
  {
    sessionId: 'session-1',
    mode: SessionMode.BUILD,
    model: 'sonnet',
    lastActivityAt: '2024-01-01T10:30:00Z',
    messageCount: 15,
  },
  {
    sessionId: 'session-2',
    mode: SessionMode.PLAN,
    model: 'opus',
    lastActivityAt: '2024-01-01T09:15:00Z',
    messageCount: 5,
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

describe('useSessionHistory', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('fetches session history successfully', async () => {
    const mockGetHistory = Sessions.getApiSessionsEntityByEntityIdResumable as Mock
    mockGetHistory.mockResolvedValueOnce({ data: mockSessionHistory })

    const { result } = renderHook(() => useSessionHistory('issue-1'), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(true)

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.data).toEqual(mockSessionHistory)
    expect(mockGetHistory).toHaveBeenCalledWith({
      path: { entityId: 'issue-1' },
    })
  })

  it('handles error when fetching history fails', async () => {
    const mockGetHistory = Sessions.getApiSessionsEntityByEntityIdResumable as Mock
    mockGetHistory.mockRejectedValueOnce(new Error('Network error'))

    const { result } = renderHook(() => useSessionHistory('issue-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.error).toBeDefined()
  })

  it('returns empty array when no history exists', async () => {
    const mockGetHistory = Sessions.getApiSessionsEntityByEntityIdResumable as Mock
    mockGetHistory.mockResolvedValueOnce({ data: [] })

    const { result } = renderHook(() => useSessionHistory('issue-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.data).toEqual([])
  })

  it('does not fetch when entityId is undefined', () => {
    const mockGetHistory = Sessions.getApiSessionsEntityByEntityIdResumable as Mock

    const { result } = renderHook(() => useSessionHistory(undefined), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(false)
    expect(mockGetHistory).not.toHaveBeenCalled()
  })

  it('does not fetch when entityId is null', () => {
    const mockGetHistory = Sessions.getApiSessionsEntityByEntityIdResumable as Mock

    const { result } = renderHook(() => useSessionHistory(null), { wrapper: createWrapper() })

    expect(result.current.isLoading).toBe(false)
    expect(mockGetHistory).not.toHaveBeenCalled()
  })

  it('exports correct query key function', () => {
    expect(sessionHistoryQueryKey('issue-1')).toEqual(['session-history', 'issue-1'])
    expect(sessionHistoryQueryKey(undefined)).toEqual(['session-history', undefined])
  })
})
