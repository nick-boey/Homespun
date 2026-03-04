import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor, act } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import * as React from 'react'
import { useFleecePull, useFleeceSync } from './use-fleece-sync'
import { FleeceIssueSync } from '@/api'

vi.mock('@/api', () => ({
  FleeceIssueSync: {
    postApiFleeceSyncByProjectIdPull: vi.fn(),
    postApiFleeceSyncByProjectIdSync: vi.fn(),
  },
}))

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
      mutations: {
        retry: false,
      },
    },
  })
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return React.createElement(QueryClientProvider, { client: queryClient }, children)
  }
}

describe('useFleecePull', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('returns initial state', () => {
    const { result } = renderHook(() => useFleecePull(), {
      wrapper: createWrapper(),
    })

    expect(result.current.isPending).toBe(false)
    expect(result.current.isSuccess).toBe(false)
    expect(result.current.isError).toBe(false)
  })

  it('calls pull API with correct project ID', async () => {
    const mockResult = {
      success: true,
      issuesMerged: 5,
      wasBehindRemote: true,
      commitsPulled: 2,
    }

    vi.mocked(FleeceIssueSync.postApiFleeceSyncByProjectIdPull).mockResolvedValue({
      data: mockResult,
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as Awaited<ReturnType<typeof FleeceIssueSync.postApiFleeceSyncByProjectIdPull>>)

    const { result } = renderHook(() => useFleecePull(), {
      wrapper: createWrapper(),
    })

    await act(async () => {
      await result.current.pull('test-project-id')
    })

    expect(FleeceIssueSync.postApiFleeceSyncByProjectIdPull).toHaveBeenCalledWith({
      path: { projectId: 'test-project-id' },
    })
  })

  it('returns pull result on success', async () => {
    const mockResult = {
      success: true,
      issuesMerged: 5,
      wasBehindRemote: true,
      commitsPulled: 2,
    }

    vi.mocked(FleeceIssueSync.postApiFleeceSyncByProjectIdPull).mockResolvedValue({
      data: mockResult,
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as Awaited<ReturnType<typeof FleeceIssueSync.postApiFleeceSyncByProjectIdPull>>)

    const { result } = renderHook(() => useFleecePull(), {
      wrapper: createWrapper(),
    })

    let pullResult
    await act(async () => {
      pullResult = await result.current.pull('test-project-id')
    })

    expect(pullResult).toEqual(mockResult)
    await waitFor(() => expect(result.current.isSuccess).toBe(true))
  })

  it('throws error when pull fails', async () => {
    vi.mocked(FleeceIssueSync.postApiFleeceSyncByProjectIdPull).mockResolvedValue({
      data: undefined,
      response: new Response(null, { status: 500 }),
      request: new Request('http://test'),
      error: { detail: 'Internal server error' },
    } as Awaited<ReturnType<typeof FleeceIssueSync.postApiFleeceSyncByProjectIdPull>>)

    const { result } = renderHook(() => useFleecePull(), {
      wrapper: createWrapper(),
    })

    await expect(
      act(async () => {
        await result.current.pull('test-project-id')
      })
    ).rejects.toThrow()
  })
})

describe('useFleeceSync', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('returns initial state', () => {
    const { result } = renderHook(() => useFleeceSync(), {
      wrapper: createWrapper(),
    })

    expect(result.current.isPending).toBe(false)
    expect(result.current.isSuccess).toBe(false)
    expect(result.current.isError).toBe(false)
  })

  it('calls sync API with correct project ID', async () => {
    const mockResult = {
      success: true,
      filesCommitted: 3,
      pushSucceeded: true,
    }

    vi.mocked(FleeceIssueSync.postApiFleeceSyncByProjectIdSync).mockResolvedValue({
      data: mockResult,
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as Awaited<ReturnType<typeof FleeceIssueSync.postApiFleeceSyncByProjectIdSync>>)

    const { result } = renderHook(() => useFleeceSync(), {
      wrapper: createWrapper(),
    })

    await act(async () => {
      await result.current.sync('test-project-id')
    })

    expect(FleeceIssueSync.postApiFleeceSyncByProjectIdSync).toHaveBeenCalledWith({
      path: { projectId: 'test-project-id' },
    })
  })

  it('returns sync result on success', async () => {
    const mockResult = {
      success: true,
      filesCommitted: 3,
      pushSucceeded: true,
    }

    vi.mocked(FleeceIssueSync.postApiFleeceSyncByProjectIdSync).mockResolvedValue({
      data: mockResult,
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as Awaited<ReturnType<typeof FleeceIssueSync.postApiFleeceSyncByProjectIdSync>>)

    const { result } = renderHook(() => useFleeceSync(), {
      wrapper: createWrapper(),
    })

    let syncResult
    await act(async () => {
      syncResult = await result.current.sync('test-project-id')
    })

    expect(syncResult).toEqual(mockResult)
    await waitFor(() => expect(result.current.isSuccess).toBe(true))
  })

  it('throws error when sync fails', async () => {
    vi.mocked(FleeceIssueSync.postApiFleeceSyncByProjectIdSync).mockResolvedValue({
      data: undefined,
      response: new Response(null, { status: 500 }),
      request: new Request('http://test'),
      error: { detail: 'Internal server error' },
    } as Awaited<ReturnType<typeof FleeceIssueSync.postApiFleeceSyncByProjectIdSync>>)

    const { result } = renderHook(() => useFleeceSync(), {
      wrapper: createWrapper(),
    })

    await expect(
      act(async () => {
        await result.current.sync('test-project-id')
      })
    ).rejects.toThrow()
  })
})
