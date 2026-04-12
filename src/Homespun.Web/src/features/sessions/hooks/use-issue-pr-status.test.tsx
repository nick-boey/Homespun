import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClientProvider, QueryClient } from '@tanstack/react-query'
import { useIssuePrStatus } from './use-issue-pr-status'
import { IssuePrStatus } from '@/api'
import type { IssuePullRequestStatus } from '@/api/generated'
import { PullRequestStatus } from '@/api/generated'
import { createMockSession } from '@/test/test-utils'

vi.mock('@/api', () => ({
  IssuePrStatus: {
    getApiProjectsByProjectIdIssuesByIssueIdPrStatus: vi.fn(),
  },
}))

describe('useIssuePrStatus', () => {
  let queryClient: QueryClient

  beforeEach(() => {
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    })
    vi.clearAllMocks()
  })

  const wrapper = ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  )

  it('returns null when session is undefined', async () => {
    const { result } = renderHook(() => useIssuePrStatus(undefined), { wrapper })

    expect(result.current.data).toBe(undefined)
    expect(result.current.isLoading).toBe(false)
  })

  it('returns null when entityId does not start with clone:', async () => {
    const session = createMockSession({
      entityId: 'issue-123',
    })

    const { result } = renderHook(() => useIssuePrStatus(session), { wrapper })

    expect(result.current.data).toBe(undefined)
    expect(result.current.isLoading).toBe(false)
  })

  it('fetches PR status for a clone entity', async () => {
    const mockPrStatus: IssuePullRequestStatus = {
      prNumber: 42,
      prUrl: 'https://github.com/test/repo/pull/42',
      branchName: 'feature/test',
      status: PullRequestStatus.READY_FOR_REVIEW,
      checksPassing: true,
      isApproved: true,
      approvalCount: 2,
      changesRequestedCount: 0,
      isMergeableByGitHub: true,
      mergeableState: 'clean',
      isMergeable: true,
      checksRunning: false,
      checksFailing: false,
      hasConflicts: false,
    }

    vi.mocked(IssuePrStatus.getApiProjectsByProjectIdIssuesByIssueIdPrStatus).mockResolvedValue({
      data: mockPrStatus,
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as Awaited<ReturnType<typeof IssuePrStatus.getApiProjectsByProjectIdIssuesByIssueIdPrStatus>>)

    const session = createMockSession({
      entityId: 'clone:issue-123',
    })

    const { result } = renderHook(() => useIssuePrStatus(session), { wrapper })

    expect(result.current.isLoading).toBe(true)

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.data).toEqual(mockPrStatus)
    expect(IssuePrStatus.getApiProjectsByProjectIdIssuesByIssueIdPrStatus).toHaveBeenCalledWith({
      path: {
        projectId: 'project-1',
        issueId: 'issue-123',
      },
    })
  })

  it('handles API errors gracefully', async () => {
    vi.mocked(IssuePrStatus.getApiProjectsByProjectIdIssuesByIssueIdPrStatus).mockRejectedValue(
      new Error('API Error')
    )

    const session = createMockSession({
      entityId: 'clone:issue-123',
    })

    const { result } = renderHook(() => useIssuePrStatus(session), { wrapper })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })

    expect(result.current.data).toBe(undefined)
  })
})
