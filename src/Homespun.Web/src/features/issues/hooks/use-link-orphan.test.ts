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

  it('T011.1 single-occurrence input emits exactly one POST with the occurrence branch', async () => {
    mockLink.mockResolvedValue(okResponse())
    const client = makeClient()
    const { result } = renderHook(() => useLinkOrphan(), { wrapper: wrap(client) })

    await result.current.mutateAsync({
      projectId: 'p1',
      occurrences: [{ branch: 'feat/x', changeName: 'add-foo' }],
      fleeceId: 'f1',
    })

    expect(mockLink).toHaveBeenCalledTimes(1)
    expect(mockLink).toHaveBeenCalledWith({
      body: {
        projectId: 'p1',
        branch: 'feat/x',
        changeName: 'add-foo',
        fleeceId: 'f1',
      },
    })
  })

  it('T011.2 multi-occurrence input emits one POST per occurrence in parallel', async () => {
    mockLink.mockResolvedValue(okResponse())
    const client = makeClient()
    const { result } = renderHook(() => useLinkOrphan(), { wrapper: wrap(client) })

    await result.current.mutateAsync({
      projectId: 'p1',
      occurrences: [
        { branch: null, changeName: 'add-foo' },
        { branch: 'feat/x', changeName: 'add-foo' },
      ],
      fleeceId: 'f1',
    })

    expect(mockLink).toHaveBeenCalledTimes(2)
    const calls = mockLink.mock.calls.map((c) => c[0]?.body)
    expect(calls).toEqual(
      expect.arrayContaining([
        { projectId: 'p1', branch: null, changeName: 'add-foo', fleeceId: 'f1' },
        { projectId: 'p1', branch: 'feat/x', changeName: 'add-foo', fleeceId: 'f1' },
      ])
    )
  })

  it('T011.3 any call failing rejects the mutation', async () => {
    mockLink.mockResolvedValueOnce(okResponse())
    mockLink.mockResolvedValueOnce(errorResponse('change directory not found'))

    const client = makeClient()
    const { result } = renderHook(() => useLinkOrphan(), { wrapper: wrap(client) })

    await expect(
      result.current.mutateAsync({
        projectId: 'p1',
        occurrences: [
          { branch: null, changeName: 'add-foo' },
          { branch: 'feat/x', changeName: 'add-foo' },
        ],
        fleeceId: 'f1',
      })
    ).rejects.toThrow('change directory not found')
  })

  it('T011.4 onSuccess invalidates task-graph once', async () => {
    mockLink.mockResolvedValue(okResponse())
    const client = makeClient()
    const spy = vi.spyOn(client, 'invalidateQueries')

    const { result } = renderHook(() => useLinkOrphan(), { wrapper: wrap(client) })

    await result.current.mutateAsync({
      projectId: 'p1',
      occurrences: [
        { branch: null, changeName: 'add-foo' },
        { branch: 'feat/x', changeName: 'add-foo' },
      ],
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
