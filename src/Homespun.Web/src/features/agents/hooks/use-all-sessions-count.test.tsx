import { describe, it, expect, vi } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useAllSessionsCount } from './use-all-sessions-count'
import { Sessions, SessionMode, ClaudeSessionStatus } from '@/api'
import type { SessionSummary } from '@/api/generated/types.gen'

// Mock the API module
vi.mock('@/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api')>()
  return {
    ...actual,
    Sessions: {
      getApiSessions: vi.fn(),
    },
  }
})

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

  const mockSession = (
    id: string,
    status: (typeof ClaudeSessionStatus)[keyof typeof ClaudeSessionStatus]
  ): SessionSummary => ({
    id,
    status: status,
    entityId: null,
    projectId: 'test-project',
    model: null,
    mode: SessionMode.PLAN,
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
      request: {} as Request,
      response: {} as Response,
    })

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
      mockSession('1', ClaudeSessionStatus.STARTING),
      mockSession('2', ClaudeSessionStatus.RUNNING_HOOKS),
      mockSession('3', ClaudeSessionStatus.RUNNING),
      mockSession('4', ClaudeSessionStatus.WAITING_FOR_INPUT), // not working
    ]

    vi.mocked(Sessions.getApiSessions).mockResolvedValueOnce({
      data: sessions,
      request: {} as Request,
      response: {} as Response,
    })

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
      mockSession('1', ClaudeSessionStatus.WAITING_FOR_INPUT),
      mockSession('2', ClaudeSessionStatus.WAITING_FOR_QUESTION_ANSWER),
      mockSession('3', ClaudeSessionStatus.WAITING_FOR_PLAN_EXECUTION),
      mockSession('4', ClaudeSessionStatus.STARTING), // not waiting
    ]

    vi.mocked(Sessions.getApiSessions).mockResolvedValueOnce({
      data: sessions,
      request: {} as Request,
      response: {} as Response,
    })

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
      mockSession('1', ClaudeSessionStatus.WAITING_FOR_INPUT),
      mockSession('2', ClaudeSessionStatus.WAITING_FOR_INPUT),
      mockSession('3', ClaudeSessionStatus.WAITING_FOR_QUESTION_ANSWER),
      mockSession('4', ClaudeSessionStatus.WAITING_FOR_PLAN_EXECUTION),
      mockSession('5', ClaudeSessionStatus.WAITING_FOR_PLAN_EXECUTION),
      mockSession('6', ClaudeSessionStatus.WAITING_FOR_PLAN_EXECUTION),
      mockSession('7', ClaudeSessionStatus.STARTING), // not waiting
    ]

    vi.mocked(Sessions.getApiSessions).mockResolvedValueOnce({
      data: sessions,
      request: {} as Request,
      response: {} as Response,
    })

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
      mockSession('1', ClaudeSessionStatus.ERROR),
      mockSession('2', ClaudeSessionStatus.ERROR),
      mockSession('3', ClaudeSessionStatus.STARTING), // not error
    ]

    vi.mocked(Sessions.getApiSessions).mockResolvedValueOnce({
      data: sessions,
      request: {} as Request,
      response: {} as Response,
    })

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
      mockSession('1', ClaudeSessionStatus.STARTING), // working
      mockSession('2', ClaudeSessionStatus.RUNNING_HOOKS), // working
      mockSession('3', ClaudeSessionStatus.WAITING_FOR_INPUT), // waiting
      mockSession('4', ClaudeSessionStatus.WAITING_FOR_QUESTION_ANSWER), // waiting
      mockSession('5', ClaudeSessionStatus.ERROR), // not included in active
    ]

    vi.mocked(Sessions.getApiSessions).mockResolvedValueOnce({
      data: sessions,
      request: {} as Request,
      response: {} as Response,
    })

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
    vi.mocked(Sessions.getApiSessions).mockRejectedValueOnce(new Error('Network error'))

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
      () =>
        new Promise(() => {}) as Promise<{
          data: Array<SessionSummary>
          request: Request
          response: Response
        }> // Never resolves
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
      request: {} as Request,
      response: {} as Response,
    })

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
