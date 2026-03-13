import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement, type ReactNode } from 'react'
import { Sessions } from '@/api'
import type { SessionSummary, ClaudeSessionStatus, SessionMode } from '@/api/generated'
import { useSessionNavigation } from './use-session-navigation'

vi.mock('@/api', () => ({
  Sessions: {
    getApiSessions: vi.fn(),
  },
}))

function createMockSession(
  id: string,
  status: ClaudeSessionStatus,
  lastActivityAt: string | null
): SessionSummary {
  return {
    id,
    entityId: `entity-${id}`,
    projectId: 'project-1',
    model: 'sonnet',
    mode: 1 as SessionMode,
    status,
    createdAt: '2024-01-01T00:00:00Z',
    lastActivityAt: lastActivityAt ?? undefined,
    messageCount: 5,
    totalCostUsd: 0.1,
    containerId: null,
    containerName: null,
  }
}

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

describe('useSessionNavigation', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('returns correct next and previous sessions when available', async () => {
    // Sessions sorted by lastActivityAt descending:
    // session-1 (newest) -> session-2 (current) -> session-3 (oldest)
    const mockSessions: SessionSummary[] = [
      createMockSession('session-1', 6 as ClaudeSessionStatus, '2024-01-03T10:00:00Z'), // Stopped, newest
      createMockSession('session-2', 6 as ClaudeSessionStatus, '2024-01-02T10:00:00Z'), // Stopped, current
      createMockSession('session-3', 6 as ClaudeSessionStatus, '2024-01-01T10:00:00Z'), // Stopped, oldest
    ]

    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockResolvedValueOnce({ data: mockSessions })

    const { result } = renderHook(() => useSessionNavigation('session-2'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    // previous = newer session (session-1)
    expect(result.current.previousSessionId).toBe('session-1')
    expect(result.current.hasPrevious).toBe(true)
    // next = older session (session-3)
    expect(result.current.nextSessionId).toBe('session-3')
    expect(result.current.hasNext).toBe(true)
  })

  it('returns null when at the start of navigation (no newer session)', async () => {
    const mockSessions: SessionSummary[] = [
      createMockSession('session-1', 6 as ClaudeSessionStatus, '2024-01-03T10:00:00Z'), // current, newest
      createMockSession('session-2', 6 as ClaudeSessionStatus, '2024-01-02T10:00:00Z'),
      createMockSession('session-3', 6 as ClaudeSessionStatus, '2024-01-01T10:00:00Z'),
    ]

    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockResolvedValueOnce({ data: mockSessions })

    const { result } = renderHook(() => useSessionNavigation('session-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.previousSessionId).toBeNull()
    expect(result.current.hasPrevious).toBe(false)
    expect(result.current.nextSessionId).toBe('session-2')
    expect(result.current.hasNext).toBe(true)
  })

  it('returns null when at the end of navigation (no older session)', async () => {
    const mockSessions: SessionSummary[] = [
      createMockSession('session-1', 6 as ClaudeSessionStatus, '2024-01-03T10:00:00Z'),
      createMockSession('session-2', 6 as ClaudeSessionStatus, '2024-01-02T10:00:00Z'),
      createMockSession('session-3', 6 as ClaudeSessionStatus, '2024-01-01T10:00:00Z'), // current, oldest
    ]

    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockResolvedValueOnce({ data: mockSessions })

    const { result } = renderHook(() => useSessionNavigation('session-3'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.previousSessionId).toBe('session-2')
    expect(result.current.hasPrevious).toBe(true)
    expect(result.current.nextSessionId).toBeNull()
    expect(result.current.hasNext).toBe(false)
  })

  it('filters out Running sessions from navigation targets', async () => {
    const mockSessions: SessionSummary[] = [
      createMockSession('session-1', 2 as ClaudeSessionStatus, '2024-01-03T10:00:00Z'), // Running - should be excluded
      createMockSession('session-2', 6 as ClaudeSessionStatus, '2024-01-02T10:00:00Z'), // Stopped - current
      createMockSession('session-3', 6 as ClaudeSessionStatus, '2024-01-01T10:00:00Z'), // Stopped
    ]

    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockResolvedValueOnce({ data: mockSessions })

    const { result } = renderHook(() => useSessionNavigation('session-2'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    // session-1 is Running, so previous should be null
    expect(result.current.previousSessionId).toBeNull()
    expect(result.current.hasPrevious).toBe(false)
    expect(result.current.nextSessionId).toBe('session-3')
    expect(result.current.hasNext).toBe(true)
  })

  it('filters out RunningHooks sessions from navigation targets', async () => {
    const mockSessions: SessionSummary[] = [
      createMockSession('session-1', 1 as ClaudeSessionStatus, '2024-01-03T10:00:00Z'), // RunningHooks - should be excluded
      createMockSession('session-2', 6 as ClaudeSessionStatus, '2024-01-02T10:00:00Z'), // Stopped - current
      createMockSession('session-3', 6 as ClaudeSessionStatus, '2024-01-01T10:00:00Z'), // Stopped
    ]

    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockResolvedValueOnce({ data: mockSessions })

    const { result } = renderHook(() => useSessionNavigation('session-2'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.previousSessionId).toBeNull()
    expect(result.current.hasPrevious).toBe(false)
  })

  it('filters out Starting sessions from navigation targets', async () => {
    const mockSessions: SessionSummary[] = [
      createMockSession('session-1', 0 as ClaudeSessionStatus, '2024-01-03T10:00:00Z'), // Starting - should be excluded
      createMockSession('session-2', 6 as ClaudeSessionStatus, '2024-01-02T10:00:00Z'), // Stopped - current
      createMockSession('session-3', 6 as ClaudeSessionStatus, '2024-01-01T10:00:00Z'), // Stopped
    ]

    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockResolvedValueOnce({ data: mockSessions })

    const { result } = renderHook(() => useSessionNavigation('session-2'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.previousSessionId).toBeNull()
    expect(result.current.hasPrevious).toBe(false)
  })

  it('includes WaitingForInput sessions as navigation targets', async () => {
    const mockSessions: SessionSummary[] = [
      createMockSession('session-1', 3 as ClaudeSessionStatus, '2024-01-03T10:00:00Z'), // WaitingForInput - should be included
      createMockSession('session-2', 6 as ClaudeSessionStatus, '2024-01-02T10:00:00Z'), // Stopped - current
    ]

    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockResolvedValueOnce({ data: mockSessions })

    const { result } = renderHook(() => useSessionNavigation('session-2'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.previousSessionId).toBe('session-1')
    expect(result.current.hasPrevious).toBe(true)
  })

  it('includes WaitingForQuestionAnswer sessions as navigation targets', async () => {
    const mockSessions: SessionSummary[] = [
      createMockSession('session-1', 4 as ClaudeSessionStatus, '2024-01-03T10:00:00Z'), // WaitingForQuestionAnswer
      createMockSession('session-2', 6 as ClaudeSessionStatus, '2024-01-02T10:00:00Z'), // Stopped - current
    ]

    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockResolvedValueOnce({ data: mockSessions })

    const { result } = renderHook(() => useSessionNavigation('session-2'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.previousSessionId).toBe('session-1')
    expect(result.current.hasPrevious).toBe(true)
  })

  it('includes WaitingForPlanExecution sessions as navigation targets', async () => {
    const mockSessions: SessionSummary[] = [
      createMockSession('session-1', 5 as ClaudeSessionStatus, '2024-01-03T10:00:00Z'), // WaitingForPlanExecution
      createMockSession('session-2', 6 as ClaudeSessionStatus, '2024-01-02T10:00:00Z'), // Stopped - current
    ]

    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockResolvedValueOnce({ data: mockSessions })

    const { result } = renderHook(() => useSessionNavigation('session-2'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.previousSessionId).toBe('session-1')
    expect(result.current.hasPrevious).toBe(true)
  })

  it('includes Error sessions as navigation targets', async () => {
    const mockSessions: SessionSummary[] = [
      createMockSession('session-1', 7 as ClaudeSessionStatus, '2024-01-03T10:00:00Z'), // Error
      createMockSession('session-2', 6 as ClaudeSessionStatus, '2024-01-02T10:00:00Z'), // Stopped - current
    ]

    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockResolvedValueOnce({ data: mockSessions })

    const { result } = renderHook(() => useSessionNavigation('session-2'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.previousSessionId).toBe('session-1')
    expect(result.current.hasPrevious).toBe(true)
  })

  it('excludes current session from navigation targets', async () => {
    // Even if only one session exists (the current one), both buttons should be disabled
    const mockSessions: SessionSummary[] = [
      createMockSession('session-1', 6 as ClaudeSessionStatus, '2024-01-01T10:00:00Z'),
    ]

    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockResolvedValueOnce({ data: mockSessions })

    const { result } = renderHook(() => useSessionNavigation('session-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.previousSessionId).toBeNull()
    expect(result.current.hasPrevious).toBe(false)
    expect(result.current.nextSessionId).toBeNull()
    expect(result.current.hasNext).toBe(false)
  })

  it('handles empty sessions list', async () => {
    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockResolvedValueOnce({ data: [] })

    const { result } = renderHook(() => useSessionNavigation('session-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.previousSessionId).toBeNull()
    expect(result.current.hasPrevious).toBe(false)
    expect(result.current.nextSessionId).toBeNull()
    expect(result.current.hasNext).toBe(false)
  })

  it('handles loading state', () => {
    const mockGetApiSessions = Sessions.getApiSessions as Mock
    // Don't resolve the promise to keep it in loading state
    mockGetApiSessions.mockReturnValue(new Promise(() => {}))

    const { result } = renderHook(() => useSessionNavigation('session-1'), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(true)
    expect(result.current.previousSessionId).toBeNull()
    expect(result.current.nextSessionId).toBeNull()
    expect(result.current.hasPrevious).toBe(false)
    expect(result.current.hasNext).toBe(false)
  })

  it('handles sessions with null lastActivityAt (treated as oldest)', async () => {
    const mockSessions: SessionSummary[] = [
      createMockSession('session-1', 6 as ClaudeSessionStatus, '2024-01-02T10:00:00Z'), // Stopped
      createMockSession('session-2', 6 as ClaudeSessionStatus, null), // Stopped, null timestamp - oldest
      createMockSession('session-3', 6 as ClaudeSessionStatus, '2024-01-01T10:00:00Z'), // Stopped - current
    ]

    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockResolvedValueOnce({ data: mockSessions })

    const { result } = renderHook(() => useSessionNavigation('session-3'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    // session-3 is current, sorted order should be:
    // session-1 (newest) -> session-3 (current) -> session-2 (null timestamp, oldest)
    expect(result.current.previousSessionId).toBe('session-1')
    expect(result.current.hasPrevious).toBe(true)
    expect(result.current.nextSessionId).toBe('session-2')
    expect(result.current.hasNext).toBe(true)
  })

  it('navigates correctly when all other sessions are running', async () => {
    const mockSessions: SessionSummary[] = [
      createMockSession('session-1', 2 as ClaudeSessionStatus, '2024-01-03T10:00:00Z'), // Running
      createMockSession('session-2', 6 as ClaudeSessionStatus, '2024-01-02T10:00:00Z'), // Stopped - current
      createMockSession('session-3', 0 as ClaudeSessionStatus, '2024-01-01T10:00:00Z'), // Starting
    ]

    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockResolvedValueOnce({ data: mockSessions })

    const { result } = renderHook(() => useSessionNavigation('session-2'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    // Both running sessions should be filtered out
    expect(result.current.previousSessionId).toBeNull()
    expect(result.current.hasPrevious).toBe(false)
    expect(result.current.nextSessionId).toBeNull()
    expect(result.current.hasNext).toBe(false)
  })

  it('skips multiple running sessions to find valid targets', async () => {
    const mockSessions: SessionSummary[] = [
      createMockSession('session-1', 6 as ClaudeSessionStatus, '2024-01-05T10:00:00Z'), // Stopped - valid previous
      createMockSession('session-2', 2 as ClaudeSessionStatus, '2024-01-04T10:00:00Z'), // Running - skip
      createMockSession('session-3', 1 as ClaudeSessionStatus, '2024-01-03T10:00:00Z'), // RunningHooks - skip
      createMockSession('session-4', 6 as ClaudeSessionStatus, '2024-01-02T10:00:00Z'), // Stopped - current
      createMockSession('session-5', 0 as ClaudeSessionStatus, '2024-01-01T09:00:00Z'), // Starting - skip
      createMockSession('session-6', 6 as ClaudeSessionStatus, '2024-01-01T08:00:00Z'), // Stopped - valid next
    ]

    const mockGetApiSessions = Sessions.getApiSessions as Mock
    mockGetApiSessions.mockResolvedValueOnce({ data: mockSessions })

    const { result } = renderHook(() => useSessionNavigation('session-4'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    // Should skip running sessions and find session-1 as previous
    expect(result.current.previousSessionId).toBe('session-1')
    expect(result.current.hasPrevious).toBe(true)
    // Should skip running sessions and find session-6 as next
    expect(result.current.nextSessionId).toBe('session-6')
    expect(result.current.hasNext).toBe(true)
  })
})
