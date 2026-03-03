import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement } from 'react'
import { useProjects, useProject, useCreateProject, useDeleteProject } from './use-projects'

// Mock the API client
vi.mock('@/api/generated/sdk.gen', () => ({
  Projects: {
    getApiProjects: vi.fn(),
    getApiProjectsById: vi.fn(),
    postApiProjects: vi.fn(),
    deleteApiProjectsById: vi.fn(),
  },
}))

import { Projects } from '@/api/generated/sdk.gen'

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  })
  return ({ children }: { children: React.ReactNode }) =>
    createElement(QueryClientProvider, { client: queryClient }, children)
}

describe('useProjects', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('returns projects when API call succeeds', async () => {
    const mockProjects = [
      {
        id: 'project-1',
        name: 'Test Project',
        localPath: '/path/to/project',
        defaultBranch: 'main',
      },
    ]

    vi.mocked(Projects.getApiProjects).mockResolvedValue({
      data: mockProjects,
      request: {} as Request,
      response: {} as Response,
    })

    const { result } = renderHook(() => useProjects(), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(result.current.data).toEqual(mockProjects)
  })

  it('returns error when API call fails', async () => {
    vi.mocked(Projects.getApiProjects).mockRejectedValue(new Error('API Error'))

    const { result } = renderHook(() => useProjects(), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })
  })
})

describe('useProject', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('returns project when API call succeeds', async () => {
    const mockProject = {
      id: 'project-1',
      name: 'Test Project',
      localPath: '/path/to/project',
      defaultBranch: 'main',
    }

    vi.mocked(Projects.getApiProjectsById).mockResolvedValue({
      data: mockProject,
      request: {} as Request,
      response: {} as Response,
    })

    const { result } = renderHook(() => useProject('project-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(result.current.data).toEqual(mockProject)
    expect(Projects.getApiProjectsById).toHaveBeenCalledWith({
      path: { id: 'project-1' },
    })
  })

  it('does not fetch when projectId is undefined', () => {
    const { result } = renderHook(() => useProject(undefined), {
      wrapper: createWrapper(),
    })

    expect(result.current.isFetching).toBe(false)
    expect(Projects.getApiProjectsById).not.toHaveBeenCalled()
  })
})

describe('useCreateProject', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('creates project successfully', async () => {
    const mockProject = {
      id: 'new-project',
      name: 'New Project',
      localPath: '/path/to/new',
      defaultBranch: 'main',
    }

    vi.mocked(Projects.postApiProjects).mockResolvedValue({
      data: mockProject,
      request: {} as Request,
      response: {} as Response,
    })

    const { result } = renderHook(() => useCreateProject(), {
      wrapper: createWrapper(),
    })

    result.current.mutate({ ownerRepo: 'owner/repo', defaultBranch: 'main' })

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(result.current.data).toEqual(mockProject)
  })
})

describe('useDeleteProject', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('deletes project successfully', async () => {
    vi.mocked(Projects.deleteApiProjectsById).mockResolvedValue({
      data: undefined,
      request: {} as Request,
      response: {} as Response,
    })

    const { result } = renderHook(() => useDeleteProject(), {
      wrapper: createWrapper(),
    })

    result.current.mutate('project-1')

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(Projects.deleteApiProjectsById).toHaveBeenCalledWith({
      path: { id: 'project-1' },
    })
  })
})
