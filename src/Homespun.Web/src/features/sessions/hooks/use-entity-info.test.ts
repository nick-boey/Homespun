import { describe, it, expect, vi } from 'vitest'
import { renderHook, waitFor, act } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useEntityInfo } from './use-entity-info'
import { Issues, PullRequests } from '@/api'
import React, { type ReactNode } from 'react'

// Mock the API modules
vi.mock('@/api', () => ({
  Issues: {
    getApiIssuesByIssueId: vi.fn(),
  },
  PullRequests: {
    getApiPullRequestsById: vi.fn(),
  },
}))

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  return ({ children }: { children: ReactNode }) => {
    return React.createElement(QueryClientProvider, { client: queryClient }, children)
  }
}

describe('useEntityInfo', () => {
  const wrapper = createWrapper()

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('detects issue entity type from ID format', async () => {
    const mockIssue = {
      data: { id: 'issue-123', title: 'Test Issue' },
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    }
    vi.mocked(Issues.getApiIssuesByIssueId).mockResolvedValue(mockIssue)

    const { result } = renderHook(() => useEntityInfo('issue-123'), { wrapper })

    await waitFor(() => {
      expect(result.current.data).toEqual({
        type: 'issue',
        title: 'Test Issue',
        id: 'issue-123',
      })
    })

    expect(Issues.getApiIssuesByIssueId).toHaveBeenCalledWith({
      path: { issueId: 'issue-123' },
      query: undefined,
    })
    expect(PullRequests.getApiPullRequestsById).not.toHaveBeenCalled()
  })

  it('detects issue entity type with projectId', async () => {
    const mockIssue = {
      data: { id: 'issue-123', title: 'Test Issue' },
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    }
    vi.mocked(Issues.getApiIssuesByIssueId).mockResolvedValue(mockIssue)

    const { result } = renderHook(() => useEntityInfo('issue-123', 'project-1'), { wrapper })

    await waitFor(() => {
      expect(result.current.data).toEqual({
        type: 'issue',
        title: 'Test Issue',
        id: 'issue-123',
      })
    })

    expect(Issues.getApiIssuesByIssueId).toHaveBeenCalledWith({
      path: { issueId: 'issue-123' },
      query: { projectId: 'project-1' },
    })
    expect(PullRequests.getApiPullRequestsById).not.toHaveBeenCalled()
  })

  it('detects PR entity type from ID format', async () => {
    const mockPR = {
      data: { id: 'pr-456', title: 'Test PR', projectId: 'project-1' },
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    } as Awaited<ReturnType<typeof PullRequests.getApiPullRequestsById>>
    vi.mocked(PullRequests.getApiPullRequestsById).mockResolvedValue(mockPR)

    const { result } = renderHook(() => useEntityInfo('pr-456'), { wrapper })

    await waitFor(() => {
      expect(result.current.data).toEqual({
        type: 'pr',
        title: 'Test PR',
        id: 'pr-456',
      })
    })

    expect(PullRequests.getApiPullRequestsById).toHaveBeenCalledWith({
      path: { id: 'pr-456' },
    })
    expect(Issues.getApiIssuesByIssueId).not.toHaveBeenCalled()
  })

  it('ignores projectId for PR entity type', async () => {
    const mockPR = {
      data: { id: 'pr-789', title: 'Test PR with Project', projectId: 'project-1' },
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    } as Awaited<ReturnType<typeof PullRequests.getApiPullRequestsById>>
    vi.mocked(PullRequests.getApiPullRequestsById).mockResolvedValue(mockPR)

    const { result } = renderHook(() => useEntityInfo('pr-789', 'project-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.data).toEqual({
        type: 'pr',
        title: 'Test PR with Project',
        id: 'pr-789',
      })
    })

    // PRs don't use projectId, so it should only pass the path parameter
    expect(PullRequests.getApiPullRequestsById).toHaveBeenCalledWith({
      path: { id: 'pr-789' },
    })
    expect(Issues.getApiIssuesByIssueId).not.toHaveBeenCalled()
  })

  it('returns null data when entity ID is null', () => {
    const { result } = renderHook(() => useEntityInfo(null), { wrapper })

    expect(result.current.data).toBeUndefined()
    expect(result.current.isLoading).toBe(false)
    expect(Issues.getApiIssuesByIssueId).not.toHaveBeenCalled()
    expect(PullRequests.getApiPullRequestsById).not.toHaveBeenCalled()
  })

  it('returns null data when entity ID is undefined', () => {
    const { result } = renderHook(() => useEntityInfo(undefined), { wrapper })

    expect(result.current.data).toBeUndefined()
    expect(result.current.isLoading).toBe(false)
    expect(Issues.getApiIssuesByIssueId).not.toHaveBeenCalled()
    expect(PullRequests.getApiPullRequestsById).not.toHaveBeenCalled()
  })

  it('attempts to fetch as issue for unknown ID format', async () => {
    const mockIssue = {
      data: { id: 'custom-id', title: 'Custom Entity' },
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    }
    vi.mocked(Issues.getApiIssuesByIssueId).mockResolvedValue(mockIssue)

    const { result } = renderHook(() => useEntityInfo('custom-id'), { wrapper })

    await waitFor(() => {
      expect(result.current.data).toEqual({
        type: 'issue',
        title: 'Custom Entity',
        id: 'custom-id',
      })
    })

    expect(Issues.getApiIssuesByIssueId).toHaveBeenCalledWith({
      path: { issueId: 'custom-id' },
      query: undefined,
    })
  })

  it('does not fetch entity info for issues-agent prefixed entity IDs', async () => {
    vi.mocked(Issues.getApiIssuesByIssueId).mockResolvedValue({
      data: { id: 'should-not-fetch', title: 'Should Not Fetch' },
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })

    const { result } = renderHook(() => useEntityInfo('issues-agent-20260326-000737'), {
      wrapper: createWrapper(),
    })

    // Allow the query time to settle (async queryFn runs in microtask)
    await act(async () => {
      await new Promise((r) => setTimeout(r, 200))
    })

    // The query should not have fired for issues-agent entity IDs
    expect(Issues.getApiIssuesByIssueId).not.toHaveBeenCalled()
    expect(PullRequests.getApiPullRequestsById).not.toHaveBeenCalled()
    expect(result.current.data).toBeUndefined()
  })

  it('does not fetch entity info for rebase prefixed entity IDs', async () => {
    vi.mocked(Issues.getApiIssuesByIssueId).mockResolvedValue({
      data: { id: 'should-not-fetch', title: 'Should Not Fetch' },
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })

    const { result } = renderHook(() => useEntityInfo('rebase-feature-branch'), {
      wrapper: createWrapper(),
    })

    // Allow the query time to settle
    await act(async () => {
      await new Promise((r) => setTimeout(r, 200))
    })

    expect(Issues.getApiIssuesByIssueId).not.toHaveBeenCalled()
    expect(PullRequests.getApiPullRequestsById).not.toHaveBeenCalled()
    expect(result.current.data).toBeUndefined()
  })

  it('handles API returning undefined data gracefully', async () => {
    const mockResponse = {
      data: undefined,
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    }
    vi.mocked(Issues.getApiIssuesByIssueId).mockResolvedValue(
      mockResponse as unknown as Awaited<ReturnType<typeof Issues.getApiIssuesByIssueId>>
    )

    const { result } = renderHook(() => useEntityInfo('issue-404'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.data).toEqual({
        type: 'issue',
        title: 'issue-404',
        id: 'issue-404',
      })
    })
  })

  it('handles API errors gracefully', async () => {
    const error = new Error('API Error')
    vi.mocked(Issues.getApiIssuesByIssueId).mockRejectedValue(error)

    const { result } = renderHook(() => useEntityInfo('issue-789'), { wrapper })

    await waitFor(
      () => {
        expect(result.current.isError).toBe(true)
      },
      { timeout: 2000 }
    )

    expect(result.current.error).toBe(error)
    expect(result.current.data).toBeUndefined()
  })

  it('caches results with proper query key', async () => {
    const mockIssue = {
      data: { id: 'issue-999', title: 'Cached Issue' },
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    }
    vi.mocked(Issues.getApiIssuesByIssueId).mockResolvedValue(mockIssue)

    // First render
    const { result, rerender } = renderHook(({ entityId }) => useEntityInfo(entityId), {
      wrapper,
      initialProps: { entityId: 'issue-999' },
    })

    await waitFor(() => {
      expect(result.current.data).toEqual({
        type: 'issue',
        title: 'Cached Issue',
        id: 'issue-999',
      })
    })

    expect(Issues.getApiIssuesByIssueId).toHaveBeenCalledTimes(1)

    // Re-render with same ID - should use cache
    rerender({ entityId: 'issue-999' })

    expect(Issues.getApiIssuesByIssueId).toHaveBeenCalledTimes(1) // Still only 1 call
  })

  it('uses different cache key when projectId is provided', async () => {
    const mockIssue1 = {
      data: { id: 'issue-888', title: 'Issue without project' },
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    }
    const mockIssue2 = {
      data: { id: 'issue-888', title: 'Issue with project' },
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    }
    vi.mocked(Issues.getApiIssuesByIssueId)
      .mockResolvedValueOnce(mockIssue1)
      .mockResolvedValueOnce(mockIssue2)

    // First render without projectId
    const { result, rerender } = renderHook(
      ({ entityId, projectId }) => useEntityInfo(entityId, projectId),
      {
        wrapper: createWrapper(),
        initialProps: { entityId: 'issue-888', projectId: undefined } as {
          entityId: string
          projectId?: string
        },
      }
    )

    await waitFor(() => {
      expect(result.current.data?.title).toBe('Issue without project')
    })

    expect(Issues.getApiIssuesByIssueId).toHaveBeenCalledTimes(1)

    // Re-render with projectId - should make new request (different cache key)
    rerender({ entityId: 'issue-888', projectId: 'project-1' })

    await waitFor(() => {
      expect(result.current.data?.title).toBe('Issue with project')
    })

    expect(Issues.getApiIssuesByIssueId).toHaveBeenCalledTimes(2)
    expect(Issues.getApiIssuesByIssueId).toHaveBeenCalledWith({
      path: { issueId: 'issue-888' },
      query: { projectId: 'project-1' },
    })
  })

  it('refetches when entity ID changes', async () => {
    const mockIssue1 = {
      data: { id: 'issue-001', title: 'First Issue' },
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    }
    const mockIssue2 = {
      data: { id: 'issue-002', title: 'Second Issue' },
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    }

    vi.mocked(Issues.getApiIssuesByIssueId)
      .mockResolvedValueOnce(mockIssue1)
      .mockResolvedValueOnce(mockIssue2)

    const { result, rerender } = renderHook(({ entityId }) => useEntityInfo(entityId), {
      wrapper: createWrapper(),
      initialProps: { entityId: 'issue-001' },
    })

    await waitFor(() => {
      expect(result.current.data?.title).toBe('First Issue')
    })

    // Change entity ID
    rerender({ entityId: 'issue-002' })

    await waitFor(() => {
      expect(result.current.data?.title).toBe('Second Issue')
    })

    expect(Issues.getApiIssuesByIssueId).toHaveBeenCalledTimes(2)
  })
})
