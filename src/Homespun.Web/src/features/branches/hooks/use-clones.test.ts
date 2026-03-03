import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement, type ReactNode } from 'react'
import { useClones, useCreateClone, useDeleteClone, usePullClone, usePruneClones } from './use-clones'
import { Clones } from '@/api'
import type { CloneInfo } from '@/api/generated/types.gen'

vi.mock('@/api', () => ({
  Clones: {
    getApiClones: vi.fn(),
    postApiClones: vi.fn(),
    deleteApiClones: vi.fn(),
    postApiClonesPull: vi.fn(),
    postApiClonesPrune: vi.fn(),
  },
}))

const mockClones: CloneInfo[] = [
  {
    path: '/repos/.clones/feature+test-1',
    workdirPath: '/repos/.clones/feature+test-1/workdir',
    branch: 'refs/heads/feature/test-1',
    headCommit: 'abc123',
    isBare: false,
    isDetached: false,
    expectedBranch: 'feature/test-1',
  },
  {
    path: '/repos/.clones/feature+test-2',
    workdirPath: '/repos/.clones/feature+test-2/workdir',
    branch: 'refs/heads/feature/test-2',
    headCommit: 'def456',
    isBare: false,
    isDetached: false,
    expectedBranch: 'feature/test-2',
  },
]

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

describe('useClones', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('fetches clones successfully', async () => {
    const mockGetApiClones = Clones.getApiClones as Mock
    mockGetApiClones.mockResolvedValueOnce({ data: mockClones })

    const { result } = renderHook(() => useClones('project-1'), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(true)

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(result.current.data).toEqual(mockClones)
    expect(mockGetApiClones).toHaveBeenCalledWith({
      query: { projectId: 'project-1' },
    })
  })

  it('returns loading state initially', () => {
    const mockGetApiClones = Clones.getApiClones as Mock
    mockGetApiClones.mockReturnValue(new Promise(() => {}))

    const { result } = renderHook(() => useClones('project-1'), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(true)
    expect(result.current.data).toBeUndefined()
  })

  it('does not fetch when projectId is empty', () => {
    const mockGetApiClones = Clones.getApiClones as Mock

    const { result } = renderHook(() => useClones(''), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(false)
    expect(mockGetApiClones).not.toHaveBeenCalled()
  })

  it('handles error response', async () => {
    const mockGetApiClones = Clones.getApiClones as Mock
    mockGetApiClones.mockResolvedValueOnce({
      error: { detail: 'Project not found' },
    })

    const { result } = renderHook(() => useClones('nonexistent'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })

    expect(result.current.error?.message).toBe('Project not found')
  })
})

describe('useCreateClone', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('creates a clone successfully', async () => {
    const mockPostApiClones = Clones.postApiClones as Mock
    mockPostApiClones.mockResolvedValueOnce({
      data: { path: '/repos/.clones/new-branch', branchName: 'new-branch' },
    })

    const { result } = renderHook(() => useCreateClone(), {
      wrapper: createWrapper(),
    })

    result.current.mutate({
      projectId: 'project-1',
      branchName: 'new-branch',
      createBranch: true,
    })

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(mockPostApiClones).toHaveBeenCalledWith({
      body: {
        projectId: 'project-1',
        branchName: 'new-branch',
        createBranch: true,
      },
    })
  })

  it('handles creation error', async () => {
    const mockPostApiClones = Clones.postApiClones as Mock
    mockPostApiClones.mockResolvedValueOnce({
      error: { detail: 'Branch already exists' },
    })

    const { result } = renderHook(() => useCreateClone(), {
      wrapper: createWrapper(),
    })

    result.current.mutate({
      projectId: 'project-1',
      branchName: 'existing-branch',
    })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })

    expect(result.current.error?.message).toBe('Branch already exists')
  })
})

describe('useDeleteClone', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('deletes a clone successfully', async () => {
    const mockDeleteApiClones = Clones.deleteApiClones as Mock
    mockDeleteApiClones.mockResolvedValueOnce({})

    const { result } = renderHook(() => useDeleteClone(), {
      wrapper: createWrapper(),
    })

    result.current.mutate({
      projectId: 'project-1',
      clonePath: '/repos/.clones/feature+test',
    })

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(mockDeleteApiClones).toHaveBeenCalledWith({
      query: {
        projectId: 'project-1',
        clonePath: '/repos/.clones/feature+test',
      },
    })
  })

  it('handles deletion error', async () => {
    const mockDeleteApiClones = Clones.deleteApiClones as Mock
    mockDeleteApiClones.mockResolvedValueOnce({
      error: { detail: 'Clone not found' },
    })

    const { result } = renderHook(() => useDeleteClone(), {
      wrapper: createWrapper(),
    })

    result.current.mutate({
      projectId: 'project-1',
      clonePath: '/nonexistent',
    })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })

    expect(result.current.error?.message).toBe('Clone not found')
  })
})

describe('usePullClone', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('pulls latest changes successfully', async () => {
    const mockPostApiClonesPull = Clones.postApiClonesPull as Mock
    mockPostApiClonesPull.mockResolvedValueOnce({})

    const { result } = renderHook(() => usePullClone(), {
      wrapper: createWrapper(),
    })

    result.current.mutate({
      projectId: 'project-1',
      clonePath: '/repos/.clones/feature+test',
    })

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(mockPostApiClonesPull).toHaveBeenCalledWith({
      query: { clonePath: '/repos/.clones/feature+test' },
    })
  })

  it('handles pull error', async () => {
    const mockPostApiClonesPull = Clones.postApiClonesPull as Mock
    mockPostApiClonesPull.mockResolvedValueOnce({
      error: { detail: 'Merge conflict' },
    })

    const { result } = renderHook(() => usePullClone(), {
      wrapper: createWrapper(),
    })

    result.current.mutate({
      projectId: 'project-1',
      clonePath: '/repos/.clones/feature+test',
    })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })

    expect(result.current.error?.message).toBe('Merge conflict')
  })
})

describe('usePruneClones', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('prunes stale clones successfully', async () => {
    const mockPostApiClonesPrune = Clones.postApiClonesPrune as Mock
    mockPostApiClonesPrune.mockResolvedValueOnce({})

    const { result } = renderHook(() => usePruneClones(), {
      wrapper: createWrapper(),
    })

    result.current.mutate('project-1')

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(mockPostApiClonesPrune).toHaveBeenCalledWith({
      query: { projectId: 'project-1' },
    })
  })
})
