import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor, act } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import * as React from 'react'
import { useRefreshIssuesDiff } from './use-refresh-issues-diff'
import { IssuesAgent, type IssueDiffResponse } from '@/api'
import { issuesDiffQueryKey } from './use-issues-diff'

vi.mock('@/api', () => ({
  IssuesAgent: {
    postApiIssuesAgentBySessionIdRefreshDiff: vi.fn(),
  },
}))

function createQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
      mutations: {
        retry: false,
      },
    },
  })
}

function createWrapper(queryClient: QueryClient) {
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return React.createElement(QueryClientProvider, { client: queryClient }, children)
  }
}

const mockDiffResponse: IssueDiffResponse = {
  mainBranchGraph: {
    nodes: [],
  },
  sessionBranchGraph: {
    nodes: [],
  },
  changes: [
    {
      issueId: 'test123',
      changeType: 'created',
      title: 'New Issue',
      fieldChanges: [],
    },
  ],
  summary: {
    created: 1,
    updated: 0,
    deleted: 0,
  },
}

describe('useRefreshIssuesDiff', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('returns initial state', () => {
    const queryClient = createQueryClient()
    const { result } = renderHook(() => useRefreshIssuesDiff(), {
      wrapper: createWrapper(queryClient),
    })

    expect(result.current.isPending).toBe(false)
    expect(result.current.isSuccess).toBe(false)
    expect(result.current.isError).toBe(false)
  })

  it('calls refresh API with correct session ID', async () => {
    vi.mocked(IssuesAgent.postApiIssuesAgentBySessionIdRefreshDiff).mockResolvedValue({
      data: mockDiffResponse,
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as Awaited<ReturnType<typeof IssuesAgent.postApiIssuesAgentBySessionIdRefreshDiff>>)

    const queryClient = createQueryClient()
    const { result } = renderHook(() => useRefreshIssuesDiff(), {
      wrapper: createWrapper(queryClient),
    })

    await act(async () => {
      await result.current.mutateAsync('test-session-id')
    })

    expect(IssuesAgent.postApiIssuesAgentBySessionIdRefreshDiff).toHaveBeenCalledWith({
      path: { sessionId: 'test-session-id' },
    })
  })

  it('updates query cache with refreshed data on success', async () => {
    vi.mocked(IssuesAgent.postApiIssuesAgentBySessionIdRefreshDiff).mockResolvedValue({
      data: mockDiffResponse,
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as Awaited<ReturnType<typeof IssuesAgent.postApiIssuesAgentBySessionIdRefreshDiff>>)

    const queryClient = createQueryClient()
    const { result } = renderHook(() => useRefreshIssuesDiff(), {
      wrapper: createWrapper(queryClient),
    })

    await act(async () => {
      await result.current.mutateAsync('test-session-id')
    })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))

    // Check that the cache was updated
    const cachedData = queryClient.getQueryData(issuesDiffQueryKey('test-session-id'))
    expect(cachedData).toEqual(mockDiffResponse)
  })

  it('returns refreshed data on success', async () => {
    vi.mocked(IssuesAgent.postApiIssuesAgentBySessionIdRefreshDiff).mockResolvedValue({
      data: mockDiffResponse,
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as Awaited<ReturnType<typeof IssuesAgent.postApiIssuesAgentBySessionIdRefreshDiff>>)

    const queryClient = createQueryClient()
    const { result } = renderHook(() => useRefreshIssuesDiff(), {
      wrapper: createWrapper(queryClient),
    })

    let refreshResult
    await act(async () => {
      refreshResult = await result.current.mutateAsync('test-session-id')
    })

    expect(refreshResult).toEqual(mockDiffResponse)
    await waitFor(() => expect(result.current.isSuccess).toBe(true))
  })

  it('throws error when refresh fails', async () => {
    vi.mocked(IssuesAgent.postApiIssuesAgentBySessionIdRefreshDiff).mockResolvedValue({
      data: undefined,
      response: new Response(null, { status: 500 }),
      request: new Request('http://test'),
      error: { detail: 'Internal server error' },
    } as Awaited<ReturnType<typeof IssuesAgent.postApiIssuesAgentBySessionIdRefreshDiff>>)

    const queryClient = createQueryClient()
    const { result } = renderHook(() => useRefreshIssuesDiff(), {
      wrapper: createWrapper(queryClient),
    })

    await expect(
      act(async () => {
        await result.current.mutateAsync('test-session-id')
      })
    ).rejects.toThrow('Internal server error')
  })

  it('handles session not found error', async () => {
    vi.mocked(IssuesAgent.postApiIssuesAgentBySessionIdRefreshDiff).mockResolvedValue({
      data: undefined,
      response: new Response(null, { status: 404 }),
      request: new Request('http://test'),
      error: { detail: 'Session not found' },
    } as Awaited<ReturnType<typeof IssuesAgent.postApiIssuesAgentBySessionIdRefreshDiff>>)

    const queryClient = createQueryClient()
    const { result } = renderHook(() => useRefreshIssuesDiff(), {
      wrapper: createWrapper(queryClient),
    })

    await expect(
      act(async () => {
        await result.current.mutateAsync('nonexistent-session')
      })
    ).rejects.toThrow('Session not found')
  })
})
