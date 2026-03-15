import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { PullRequests, type PullRequestWithTime, PullRequestStatus } from '@/api'
import { useMergedPullRequests } from './use-merged-pull-requests'
import type { ReactNode } from 'react'

vi.mock('@/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api')>()
  return {
    ...actual,
    PullRequests: {
      getApiProjectsByProjectIdPullRequestsMerged: vi.fn(),
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

describe('useMergedPullRequests', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('fetches merged pull requests successfully', async () => {
    const mockPRs: PullRequestWithTime[] = [
      {
        pullRequest: {
          number: 120,
          title: 'Merged feature',
          status: PullRequestStatus.MERGED,
          branchName: 'feature/merged',
          htmlUrl: 'https://github.com/owner/repo/pull/120',
          mergedAt: '2024-01-15T10:00:00Z',
        },
        time: 7200,
      },
    ]

    vi.mocked(PullRequests.getApiProjectsByProjectIdPullRequestsMerged).mockResolvedValue({
      data: mockPRs,
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })

    const { result } = renderHook(() => useMergedPullRequests('project-1'), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(true)

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(result.current.pullRequests).toEqual(mockPRs)
    expect(PullRequests.getApiProjectsByProjectIdPullRequestsMerged).toHaveBeenCalledWith({
      path: { projectId: 'project-1' },
    })
  })

  it('handles error when fetch fails', async () => {
    vi.mocked(PullRequests.getApiProjectsByProjectIdPullRequestsMerged).mockResolvedValue({
      data: undefined,
      error: { detail: 'Project not found' },
      request: new Request('http://test'),
      response: new Response(null, { status: 404 }),
    })

    const { result } = renderHook(() => useMergedPullRequests('invalid-project'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })

    expect(result.current.error?.message).toBe('Project not found')
  })

  it('does not fetch when projectId is empty', () => {
    const { result } = renderHook(() => useMergedPullRequests(''), {
      wrapper: createWrapper(),
    })

    expect(PullRequests.getApiProjectsByProjectIdPullRequestsMerged).not.toHaveBeenCalled()
    expect(result.current.isLoading).toBe(false)
  })

  it('returns empty array when no merged PRs exist', async () => {
    vi.mocked(PullRequests.getApiProjectsByProjectIdPullRequestsMerged).mockResolvedValue({
      data: [],
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })

    const { result } = renderHook(() => useMergedPullRequests('project-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(result.current.pullRequests).toEqual([])
  })
})
