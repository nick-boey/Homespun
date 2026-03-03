import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { PullRequests, type PullRequestWithStatus, PullRequestStatus } from '@/api'
import { useOpenPullRequests } from './use-open-pull-requests'
import type { ReactNode } from 'react'

vi.mock('@/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api')>()
  return {
    ...actual,
    PullRequests: {
      getApiProjectsByProjectIdPullRequestsOpen: vi.fn(),
    },
  }
})

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

describe('useOpenPullRequests', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('fetches open pull requests successfully', async () => {
    const mockPRs: PullRequestWithStatus[] = [
      {
        pullRequest: {
          number: 123,
          title: 'Add new feature',
          status: PullRequestStatus[0],
          branchName: 'feature/new-feature',
          htmlUrl: 'https://github.com/owner/repo/pull/123',
          checksPassing: true,
          isApproved: false,
          approvalCount: 0,
          changesRequestedCount: 0,
        },
        status: PullRequestStatus[0],
        time: 3600,
      },
    ]

    vi.mocked(PullRequests.getApiProjectsByProjectIdPullRequestsOpen).mockResolvedValue({
      data: mockPRs,
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })

    const { result } = renderHook(() => useOpenPullRequests('project-1'), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(true)

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(result.current.pullRequests).toEqual(mockPRs)
    expect(PullRequests.getApiProjectsByProjectIdPullRequestsOpen).toHaveBeenCalledWith({
      path: { projectId: 'project-1' },
    })
  })

  it('handles error when fetch fails', async () => {
    vi.mocked(PullRequests.getApiProjectsByProjectIdPullRequestsOpen).mockResolvedValue({
      data: undefined,
      error: { detail: 'Project not found' },
      request: new Request('http://test'),
      response: new Response(null, { status: 404 }),
    })

    const { result } = renderHook(() => useOpenPullRequests('invalid-project'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })

    expect(result.current.error?.message).toBe('Project not found')
  })

  it('does not fetch when projectId is empty', () => {
    const { result } = renderHook(() => useOpenPullRequests(''), {
      wrapper: createWrapper(),
    })

    expect(PullRequests.getApiProjectsByProjectIdPullRequestsOpen).not.toHaveBeenCalled()
    expect(result.current.isLoading).toBe(false)
  })

  it('returns empty array when no PRs exist', async () => {
    vi.mocked(PullRequests.getApiProjectsByProjectIdPullRequestsOpen).mockResolvedValue({
      data: [],
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })

    const { result } = renderHook(() => useOpenPullRequests('project-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(result.current.pullRequests).toEqual([])
  })
})
