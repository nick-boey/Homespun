import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement, type ReactNode } from 'react'
import { ChangeSnapshot } from '@/api'
import { useLinkOrphan } from './use-link-orphan'
import { taskGraphQueryKey } from './use-task-graph'

vi.mock('@/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api')>()
  return {
    ...actual,
    ChangeSnapshot: { postApiOpenspecChangesLink: vi.fn() },
  }
})

const mockLink = vi.mocked(ChangeSnapshot.postApiOpenspecChangesLink)

function okResponse() {
  return {
    data: undefined,
    error: undefined,
    request: new Request('http://localhost/api/test'),
    response: new Response(),
  }
}

function errorResponse(detail: string) {
  return {
    data: undefined,
    error: { detail },
    request: new Request('http://localhost/api/test'),
    response: new Response(),
  }
}

function makeClient() {
  return new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  })
}

function wrap(client: QueryClient) {
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client }, children)
}

describe('useLinkOrphan', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('emits exactly one branchless POST per mutateAsync invocation', async () => {
    mockLink.mockResolvedValue(okResponse())
    const client = makeClient()
    const { result } = renderHook(() => useLinkOrphan(), { wrapper: wrap(client) })

    await result.current.mutateAsync({
      projectId: 'p1',
      changeName: 'add-foo',
      fleeceId: 'f1',
    })

    expect(mockLink).toHaveBeenCalledTimes(1)
    expect(mockLink).toHaveBeenCalledWith({
      body: {
        projectId: 'p1',
        changeName: 'add-foo',
        fleeceId: 'f1',
      },
    })
  })

  it('does not include a branch field on the request body', async () => {
    mockLink.mockResolvedValue(okResponse())
    const client = makeClient()
    const { result } = renderHook(() => useLinkOrphan(), { wrapper: wrap(client) })

    await result.current.mutateAsync({
      projectId: 'p1',
      changeName: 'add-foo',
      fleeceId: 'f1',
    })

    const body = mockLink.mock.calls[0]?.[0]?.body
    expect(body).toBeDefined()
    expect(body).not.toHaveProperty('branch')
  })

  it('rejects with the server detail when the call fails', async () => {
    mockLink.mockResolvedValue(errorResponse('change directory not found in any tracked clone'))
    const client = makeClient()
    const { result } = renderHook(() => useLinkOrphan(), { wrapper: wrap(client) })

    await expect(
      result.current.mutateAsync({
        projectId: 'p1',
        changeName: 'add-foo',
        fleeceId: 'f1',
      })
    ).rejects.toThrow('change directory not found in any tracked clone')
  })

  it('onSuccess invalidates task-graph exactly once', async () => {
    mockLink.mockResolvedValue(okResponse())
    const client = makeClient()
    const spy = vi.spyOn(client, 'invalidateQueries')

    const { result } = renderHook(() => useLinkOrphan(), { wrapper: wrap(client) })

    await result.current.mutateAsync({
      projectId: 'p1',
      changeName: 'add-foo',
      fleeceId: 'f1',
    })

    await waitFor(() => {
      const matching = spy.mock.calls.filter(
        (c) => JSON.stringify(c[0]?.queryKey) === JSON.stringify(taskGraphQueryKey('p1'))
      )
      expect(matching).toHaveLength(1)
    })
  })
})
