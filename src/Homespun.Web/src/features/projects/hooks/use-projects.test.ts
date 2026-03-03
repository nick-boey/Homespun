import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement, type ReactNode } from 'react'
import { useProjects, useDeleteProject } from './use-projects'
import { Projects } from '@/api'
import type { Project } from '@/api/generated/types.gen'

vi.mock('@/api', () => ({
  Projects: {
    getApiProjects: vi.fn(),
    deleteApiProjectsById: vi.fn(),
  },
}))

const mockProjects: Project[] = [
  {
    id: '1',
    name: 'Project One',
    localPath: '/path/to/project-one',
    defaultBranch: 'main',
    createdAt: '2024-01-01T00:00:00Z',
    updatedAt: '2024-01-02T00:00:00Z',
  },
  {
    id: '2',
    name: 'Project Two',
    localPath: '/path/to/project-two',
    defaultBranch: 'develop',
    gitHubOwner: 'owner',
    gitHubRepo: 'repo',
    createdAt: '2024-02-01T00:00:00Z',
    updatedAt: '2024-02-02T00:00:00Z',
  },
]

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  })
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: queryClient }, children)
}

describe('useProjects', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('fetches projects successfully', async () => {
    const mockGetApiProjects = Projects.getApiProjects as Mock
    mockGetApiProjects.mockResolvedValueOnce({ data: mockProjects })

    const { result } = renderHook(() => useProjects(), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(true)

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(result.current.data).toEqual(mockProjects)
    expect(mockGetApiProjects).toHaveBeenCalledTimes(1)
  })

  it('returns loading state initially', () => {
    const mockGetApiProjects = Projects.getApiProjects as Mock
    mockGetApiProjects.mockReturnValue(new Promise(() => {})) // Never resolves

    const { result } = renderHook(() => useProjects(), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(true)
    expect(result.current.data).toBeUndefined()
  })

  it('handles error state', async () => {
    const mockGetApiProjects = Projects.getApiProjects as Mock
    mockGetApiProjects.mockRejectedValueOnce(new Error('Network error'))

    const { result } = renderHook(() => useProjects(), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })

    expect(result.current.error).toBeDefined()
  })

  it('returns empty array when no projects exist', async () => {
    const mockGetApiProjects = Projects.getApiProjects as Mock
    mockGetApiProjects.mockResolvedValueOnce({ data: [] })

    const { result } = renderHook(() => useProjects(), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(result.current.data).toEqual([])
  })
})

describe('useDeleteProject', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('deletes a project successfully', async () => {
    const mockDeleteApiProjectsById = Projects.deleteApiProjectsById as Mock
    mockDeleteApiProjectsById.mockResolvedValueOnce({})

    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    })
    const wrapper = ({ children }: { children: ReactNode }) =>
      createElement(QueryClientProvider, { client: queryClient }, children)

    const { result } = renderHook(() => useDeleteProject(), { wrapper })

    result.current.mutate('project-id-123')

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(mockDeleteApiProjectsById).toHaveBeenCalledWith({
      path: { id: 'project-id-123' },
    })
  })

  it('handles deletion error', async () => {
    const mockDeleteApiProjectsById = Projects.deleteApiProjectsById as Mock
    mockDeleteApiProjectsById.mockRejectedValueOnce(new Error('Delete failed'))

    const queryClient = new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
    })
    const wrapper = ({ children }: { children: ReactNode }) =>
      createElement(QueryClientProvider, { client: queryClient }, children)

    const { result } = renderHook(() => useDeleteProject(), { wrapper })

    result.current.mutate('project-id-123')

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })

    expect(result.current.error).toBeDefined()
  })
})
