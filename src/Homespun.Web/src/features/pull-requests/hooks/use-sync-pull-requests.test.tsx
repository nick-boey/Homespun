import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor, act } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { PullRequests, type SyncResult } from '@/api'
import { useSyncPullRequests } from './use-sync-pull-requests'
import type { ReactNode } from 'react'

vi.mock('@/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api')>()
  return {
    ...actual,
    PullRequests: {
      postApiProjectsByProjectIdSync: vi.fn(),
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

describe('useSyncPullRequests', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('syncs pull requests successfully', async () => {
    const mockResult: SyncResult = {
      imported: 2,
      updated: 1,
      removed: 0,
      errors: [],
    }

    vi.mocked(PullRequests.postApiProjectsByProjectIdSync).mockResolvedValue({
      data: mockResult,
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })

    const { result } = renderHook(() => useSyncPullRequests(), {
      wrapper: createWrapper(),
    })

    expect(result.current.isPending).toBe(false)

    await act(async () => {
      await result.current.syncPullRequests('project-1')
    })

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(PullRequests.postApiProjectsByProjectIdSync).toHaveBeenCalledWith({
      path: { projectId: 'project-1' },
    })
  })

  it('handles sync failure', async () => {
    vi.mocked(PullRequests.postApiProjectsByProjectIdSync).mockResolvedValue({
      data: undefined,
      error: { detail: 'Sync failed' },
      request: new Request('http://test'),
      response: new Response(null, { status: 500 }),
    })

    const { result } = renderHook(() => useSyncPullRequests(), {
      wrapper: createWrapper(),
    })

    await act(async () => {
      try {
        await result.current.syncPullRequests('project-1')
      } catch {
        // Expected to throw
      }
    })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })

    expect(result.current.error?.message).toBe('Sync failed')
  })
})
