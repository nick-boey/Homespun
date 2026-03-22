import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement, type ReactNode } from 'react'
import { useBulkDeleteClones } from './use-bulk-delete-clones'
import { Clones } from '@/api'
import type { BulkDeleteClonesResponse } from '@/api/generated/types.gen'

vi.mock('@/api', () => ({
  Clones: {
    deleteApiClonesBulk: vi.fn(),
  },
}))

const mockBulkDeleteResponse: BulkDeleteClonesResponse = {
  results: [
    { clonePath: '/repos/.clones/feature+test-1', success: true, error: null },
    { clonePath: '/repos/.clones/feature+test-2', success: true, error: null },
  ],
}

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  })
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: queryClient }, children)
}

describe('useBulkDeleteClones', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('calls API with correct parameters', async () => {
    const mockDeleteApiClonesBulk = Clones.deleteApiClonesBulk as Mock
    mockDeleteApiClonesBulk.mockResolvedValueOnce({ data: mockBulkDeleteResponse })

    const { result } = renderHook(() => useBulkDeleteClones(), {
      wrapper: createWrapper(),
    })

    result.current.mutate({
      projectId: 'project-1',
      clonePaths: ['/repos/.clones/feature+test-1', '/repos/.clones/feature+test-2'],
    })

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(mockDeleteApiClonesBulk).toHaveBeenCalledWith({
      query: { projectId: 'project-1' },
      body: { clonePaths: ['/repos/.clones/feature+test-1', '/repos/.clones/feature+test-2'] },
    })
    expect(result.current.data).toEqual(mockBulkDeleteResponse)
  })

  it('invalidates queries on success', async () => {
    const mockDeleteApiClonesBulk = Clones.deleteApiClonesBulk as Mock
    mockDeleteApiClonesBulk.mockResolvedValueOnce({ data: mockBulkDeleteResponse })

    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    })
    const invalidateQueriesSpy = vi.spyOn(queryClient, 'invalidateQueries')

    const wrapper = ({ children }: { children: ReactNode }) =>
      createElement(QueryClientProvider, { client: queryClient }, children)

    const { result } = renderHook(() => useBulkDeleteClones(), { wrapper })

    result.current.mutate({
      projectId: 'project-1',
      clonePaths: ['/repos/.clones/feature+test-1'],
    })

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(invalidateQueriesSpy).toHaveBeenCalledWith({
      queryKey: ['clones', 'enriched', 'project-1'],
    })
    expect(invalidateQueriesSpy).toHaveBeenCalledWith({
      queryKey: ['clones', 'project-1'],
    })
  })

  it('handles errors', async () => {
    const mockDeleteApiClonesBulk = Clones.deleteApiClonesBulk as Mock
    mockDeleteApiClonesBulk.mockResolvedValueOnce({
      error: { detail: 'Project not found' },
    })

    const { result } = renderHook(() => useBulkDeleteClones(), {
      wrapper: createWrapper(),
    })

    result.current.mutate({
      projectId: 'nonexistent',
      clonePaths: ['/repos/.clones/feature+test-1'],
    })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })

    expect(result.current.error?.message).toBe('Project not found')
  })
})
