import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor, act } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { PullRequests, type FullRefreshResult } from '@/api'
import { useFullRefresh } from './use-full-refresh'
import type { ReactNode } from 'react'

vi.mock('@/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api')>()
  return {
    ...actual,
    PullRequests: {
      postApiProjectsByProjectIdFullRefresh: vi.fn(),
    },
  }
})

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  })
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  )
}

describe('useFullRefresh', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('performs full refresh successfully', async () => {
    const mockResult: FullRefreshResult = {
      openPrs: 5,
      closedPrs: 15,
      linkedIssues: 3,
      refreshedAt: new Date().toISOString(),
      errors: [],
    }

    vi.mocked(PullRequests.postApiProjectsByProjectIdFullRefresh).mockResolvedValue({
      data: mockResult,
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })

    const { result } = renderHook(() => useFullRefresh(), {
      wrapper: createWrapper(),
    })

    expect(result.current.isPending).toBe(false)

    await act(async () => {
      await result.current.fullRefresh('project-1')
    })

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(PullRequests.postApiProjectsByProjectIdFullRefresh).toHaveBeenCalledWith({
      path: { projectId: 'project-1' },
    })
  })

  it('handles full refresh failure', async () => {
    vi.mocked(PullRequests.postApiProjectsByProjectIdFullRefresh).mockResolvedValue({
      data: undefined,
      error: { detail: 'GitHub API rate limited' },
      request: new Request('http://test'),
      response: new Response(null, { status: 429 }),
    })

    const { result } = renderHook(() => useFullRefresh(), {
      wrapper: createWrapper(),
    })

    await act(async () => {
      try {
        await result.current.fullRefresh('project-1')
      } catch {
        // Expected to throw
      }
    })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })

    expect(result.current.error?.message).toBe('GitHub API rate limited')
  })

  it('returns default error message when no detail provided', async () => {
    vi.mocked(PullRequests.postApiProjectsByProjectIdFullRefresh).mockResolvedValue({
      data: undefined,
      error: {},
      request: new Request('http://test'),
      response: new Response(null, { status: 500 }),
    })

    const { result } = renderHook(() => useFullRefresh(), {
      wrapper: createWrapper(),
    })

    await act(async () => {
      try {
        await result.current.fullRefresh('project-1')
      } catch {
        // Expected to throw
      }
    })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })

    expect(result.current.error?.message).toBe('Failed to refresh pull requests')
  })
})
