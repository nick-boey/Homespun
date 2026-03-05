import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useIssuePrStatus } from './use-issue-pr-status'
import * as api from '@/api'
import type { ReactNode } from 'react'
import type { IssuePullRequestStatus } from '@/api'

// Mock the API
vi.mock('@/api', () => ({
  IssuePrStatus: {
    getApiIssuePrStatusByProjectIdByIssueId: vi.fn(),
  },
}))

describe('useIssuePrStatus', () => {
  const createWrapper = () => {
    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
      },
    })
    return ({ children }: { children: ReactNode }) => (
      <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
    )
  }

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('fetches PR status successfully', async () => {
    const mockPrStatus: IssuePullRequestStatus = {
      prNumber: 123,
      prUrl: 'https://github.com/example/repo/pull/123',
      status: 0, // Open
      branchName: null,
      checksPassing: null,
    }

    vi.mocked(api.IssuePrStatus.getApiIssuePrStatusByProjectIdByIssueId).mockResolvedValue({
      data: mockPrStatus,
      error: undefined,
      request: {} as Request,
      response: {} as Response,
    })

    const { result } = renderHook(() => useIssuePrStatus('project-1', 'issue-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.prStatus).toEqual(mockPrStatus)
      expect(result.current.isLoading).toBe(false)
      expect(result.current.error).toBeNull()
    })

    expect(api.IssuePrStatus.getApiIssuePrStatusByProjectIdByIssueId).toHaveBeenCalledWith({
      path: {
        projectId: 'project-1',
        issueId: 'issue-1',
      },
    })
  })

  it('handles no PR status', async () => {
    const mockPrStatus: IssuePullRequestStatus = {
      prNumber: undefined,
      prUrl: null,
      status: undefined,
      branchName: null,
      checksPassing: null,
    }

    vi.mocked(api.IssuePrStatus.getApiIssuePrStatusByProjectIdByIssueId).mockResolvedValue({
      data: mockPrStatus,
      error: undefined,
      request: {} as Request,
      response: {} as Response,
    })

    const { result } = renderHook(() => useIssuePrStatus('project-1', 'issue-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.prStatus).toEqual(mockPrStatus)
      expect(result.current.prStatus?.prNumber).toBeUndefined()
    })
  })

  it('handles API errors', async () => {
    const mockError = new Error('API Error')

    vi.mocked(api.IssuePrStatus.getApiIssuePrStatusByProjectIdByIssueId).mockRejectedValue(
      mockError
    )

    const { result } = renderHook(() => useIssuePrStatus('project-1', 'issue-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.error).toEqual(mockError)
      expect(result.current.prStatus).toBeUndefined()
      expect(result.current.isLoading).toBe(false)
    })
  })

  it('shows loading state initially', () => {
    vi.mocked(api.IssuePrStatus.getApiIssuePrStatusByProjectIdByIssueId).mockImplementation(
      () =>
        new Promise<{
          data: IssuePullRequestStatus
          error: undefined
          request: Request
          response: Response
        }>(() => {}) // Never resolves
    )

    const { result } = renderHook(() => useIssuePrStatus('project-1', 'issue-1'), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(true)
    expect(result.current.prStatus).toBeUndefined()
    expect(result.current.error).toBeNull()
  })

  it('does not fetch when projectId is empty', () => {
    const { result } = renderHook(() => useIssuePrStatus('', 'issue-1'), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(false)
    expect(result.current.prStatus).toBeUndefined()
    expect(result.current.error).toBeNull()
    expect(api.IssuePrStatus.getApiIssuePrStatusByProjectIdByIssueId).not.toHaveBeenCalled()
  })

  it('does not fetch when issueId is empty', () => {
    const { result } = renderHook(() => useIssuePrStatus('project-1', ''), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(false)
    expect(result.current.prStatus).toBeUndefined()
    expect(result.current.error).toBeNull()
    expect(api.IssuePrStatus.getApiIssuePrStatusByProjectIdByIssueId).not.toHaveBeenCalled()
  })

  it('uses correct cache key', async () => {
    const mockPrStatus: Partial<IssuePullRequestStatus> = { prNumber: 456 }

    vi.mocked(api.IssuePrStatus.getApiIssuePrStatusByProjectIdByIssueId).mockResolvedValue({
      data: mockPrStatus as IssuePullRequestStatus,
      request: {} as Request,
      response: {} as Response,
    })

    // Create a single wrapper instance to share the query client
    const wrapper = createWrapper()

    const { result: result1 } = renderHook(() => useIssuePrStatus('project-1', 'issue-1'), {
      wrapper,
    })

    await waitFor(() => {
      expect(result1.current.prStatus).toEqual(mockPrStatus)
    })

    // Call API only once even with multiple hooks using same params
    const { result: result2 } = renderHook(() => useIssuePrStatus('project-1', 'issue-1'), {
      wrapper,
    })

    expect(result2.current.prStatus).toEqual(mockPrStatus)
    expect(api.IssuePrStatus.getApiIssuePrStatusByProjectIdByIssueId).toHaveBeenCalledTimes(1)
  })

  it('refetches when parameters change', async () => {
    const mockPrStatus1: Partial<IssuePullRequestStatus> = { prNumber: 123 }
    const mockPrStatus2: Partial<IssuePullRequestStatus> = { prNumber: undefined }

    vi.mocked(api.IssuePrStatus.getApiIssuePrStatusByProjectIdByIssueId)
      .mockResolvedValueOnce({
        data: mockPrStatus1 as IssuePullRequestStatus,
        error: undefined,
        request: {} as Request,
        response: {} as Response,
      })
      .mockResolvedValueOnce({
        data: mockPrStatus2 as IssuePullRequestStatus,
        error: undefined,
        request: {} as Request,
        response: {} as Response,
      })

    const { result, rerender } = renderHook(
      ({ projectId, issueId }) => useIssuePrStatus(projectId, issueId),
      {
        wrapper: createWrapper(),
        initialProps: { projectId: 'project-1', issueId: 'issue-1' },
      }
    )

    await waitFor(() => {
      expect(result.current.prStatus).toEqual(mockPrStatus1)
    })

    // Change parameters
    rerender({ projectId: 'project-1', issueId: 'issue-2' })

    await waitFor(() => {
      expect(result.current.prStatus).toEqual(mockPrStatus2)
    })

    expect(api.IssuePrStatus.getApiIssuePrStatusByProjectIdByIssueId).toHaveBeenCalledTimes(2)
    expect(api.IssuePrStatus.getApiIssuePrStatusByProjectIdByIssueId).toHaveBeenNthCalledWith(1, {
      projectId: 'project-1',
      issueId: 'issue-1',
    })
    expect(api.IssuePrStatus.getApiIssuePrStatusByProjectIdByIssueId).toHaveBeenNthCalledWith(2, {
      projectId: 'project-1',
      issueId: 'issue-2',
    })
  })
})
