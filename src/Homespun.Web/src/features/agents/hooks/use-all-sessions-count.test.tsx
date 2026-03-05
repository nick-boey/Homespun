import { describe, it, expect, vi } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useAllSessionsCount } from './use-all-sessions-count'
import { Sessions } from '@/api'
import type { SessionSummary, ClaudeSessionStatus } from '@/api/generated/types.gen'

// Mock the API module
vi.mock('@/api', () => ({
  Sessions: {
    getApiSessions: vi.fn(),
  },
}))

describe('useAllSessionsCount', () => {
  const createWrapper = () => {
    const queryClient = new QueryClient({
      defaultOptions: {
        queries: {
          retry: false,
          refetchInterval: false,
        },
      },
    })

    return ({ children }: { children: React.ReactNode }) => (
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    )
  }

  const mockSession = (id: string, status: number): SessionSummary => ({
    id,
    status: status as ClaudeSessionStatus,
    entityId: null,
    projectId: 'test-project',
    model: null,
    mode: 0, // Using numeric value for SessionMode
    createdAt: new Date().toISOString(),
    lastActivityAt: undefined,
    messageCount: 0,
    totalCostUsd: 0,
    containerId: null,
    containerName: null,
  })

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('should return zero counts when no sessions exist', async () => {
    vi.mocked(Sessions.getApiSessions).mockResolvedValueOnce({
      data: [],
    } as any)

    const { result } = renderHook(() => useAllSessionsCount(), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.workingCount).toBe(0)
    expect(result.current.waitingCount).toBe(0)
    expect(result.current.errorCount).toBe(0)
    expect(result.current.totalActive).toBe(0)
    expect(result.current.hasActive).toBe(false)
    expect(result.current.isProcessing).toBe(false)
    expect(result.current.hasError).toBe(false)
  })

  it('should count working sessions correctly', async () => {
    const sessions: SessionSummary[] = [
      mockSession('1', 0), // Starting
      mockSession('2', 1), // RunningHooks
      mockSession('3', 2), // Running
      mockSession('4', 3), // WaitingForInput (not working)
    ]

    vi.mocked(Sessions.getApiSessions).mockResolvedValueOnce({
      data: sessions,
    } as any)

    const { result } = renderHook(() => useAllSessionsCount(), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.workingCount).toBe(3)
    expect(result.current.isProcessing).toBe(true)
    expect(result.current.hasActive).toBe(true)
  })

  it('should count waiting sessions correctly', async () => {
    const sessions: SessionSummary[] = [
      mockSession('1', 3), // WaitingForInput
      mockSession('2', 4), // WaitingForQuestionAnswer
      mockSession('3', 5), // WaitingForPlanExecution
      mockSession('4', 0), // Starting (not waiting)
    ]

    vi.mocked(Sessions.getApiSessions).mockResolvedValueOnce({
      data: sessions,
    } as any)

    const { result } = renderHook(() => useAllSessionsCount(), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.waitingCount).toBe(3)
    expect(result.current.hasActive).toBe(true)
    expect(result.current.isProcessing).toBe(true) // Because of Starting session
  })

  it('should count granular waiting status types correctly', async () => {
    const sessions: SessionSummary[] = [
      mockSession('1', 3), // WaitingForInput
      mockSession('2', 3), // WaitingForInput
      mockSession('3', 4), // WaitingForQuestionAnswer
      mockSession('4', 5), // WaitingForPlanExecution
      mockSession('5', 5), // WaitingForPlanExecution
      mockSession('6', 5), // WaitingForPlanExecution
      mockSession('7', 0), // Starting (not waiting)
    ]

    vi.mocked(Sessions.getApiSessions).mockResolvedValueOnce({
      data: sessions,
    } as any)

    const { result } = renderHook(() => useAllSessionsCount(), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.waitingForInputCount).toBe(2)
    expect(result.current.waitingForAnswerCount).toBe(1)
    expect(result.current.waitingForPlanCount).toBe(3)
    expect(result.current.waitingCount).toBe(6) // Total of all waiting types
  })

  it('should count error sessions correctly', async () => {
    const sessions: SessionSummary[] = [
      mockSession('1', 7), // Error
      mockSession('2', 7), // Error
      mockSession('3', 0), // Starting (not error)
    ]

    vi.mocked(Sessions.getApiSessions).mockResolvedValueOnce({
      data: sessions,
    } as any)

    const { result } = renderHook(() => useAllSessionsCount(), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.errorCount).toBe(2)
    expect(result.current.hasError).toBe(true)
    expect(result.current.hasActive).toBe(true) // Errors + working session
  })

  it('should calculate totalActive correctly (working + waiting)', async () => {
    const sessions: SessionSummary[] = [
      mockSession('1', 0), // Starting (working)
      mockSession('2', 1), // RunningHooks (working)
      mockSession('3', 3), // WaitingForInput (waiting)
      mockSession('4', 4), // WaitingForQuestionAnswer (waiting)
      mockSession('5', 7), // Error (not included in active)
    ]

    vi.mocked(Sessions.getApiSessions).mockResolvedValueOnce({
      data: sessions,
    } as any)

    const { result } = renderHook(() => useAllSessionsCount(), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.workingCount).toBe(2)
    expect(result.current.waitingCount).toBe(2)
    expect(result.current.errorCount).toBe(1)
    expect(result.current.totalActive).toBe(4) // 2 working + 2 waiting
  })

  it('should handle API errors gracefully', async () => {
    vi.mocked(Sessions.getApiSessions).mockRejectedValueOnce(
      new Error('Network error')
    )

    const { result } = renderHook(() => useAllSessionsCount(), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })

    expect(result.current.workingCount).toBe(0)
    expect(result.current.waitingCount).toBe(0)
    expect(result.current.waitingForInputCount).toBe(0)
    expect(result.current.waitingForAnswerCount).toBe(0)
    expect(result.current.waitingForPlanCount).toBe(0)
    expect(result.current.errorCount).toBe(0)
    expect(result.current.totalActive).toBe(0)
  })

  it('should handle loading state', () => {
    vi.mocked(Sessions.getApiSessions).mockImplementation(
      () => new Promise(() => {}) as any // Never resolves
    )

    const { result } = renderHook(() => useAllSessionsCount(), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(true)
    expect(result.current.workingCount).toBe(0)
    expect(result.current.waitingCount).toBe(0)
    expect(result.current.waitingForInputCount).toBe(0)
    expect(result.current.waitingForAnswerCount).toBe(0)
    expect(result.current.waitingForPlanCount).toBe(0)
    expect(result.current.errorCount).toBe(0)
  })

  it('should return zero granular counts when no sessions exist', async () => {
    vi.mocked(Sessions.getApiSessions).mockResolvedValueOnce({
      data: [],
    } as any)

    const { result } = renderHook(() => useAllSessionsCount(), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.waitingForInputCount).toBe(0)
    expect(result.current.waitingForAnswerCount).toBe(0)
    expect(result.current.waitingForPlanCount).toBe(0)
  })
})