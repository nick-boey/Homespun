import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement } from 'react'
import { useCreateProject } from './use-create-project'
import { Projects } from '@/api'

vi.mock('@/api', () => ({
  Projects: {
    postApiProjects: vi.fn(),
  },
}))

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  })
  return ({ children }: { children: React.ReactNode }) =>
    createElement(QueryClientProvider, { client: queryClient }, children)
}

describe('useCreateProject', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('returns mutation with isPending false initially', () => {
    const { result } = renderHook(() => useCreateProject(), {
      wrapper: createWrapper(),
    })

    expect(result.current.isPending).toBe(false)
    expect(result.current.isError).toBe(false)
    expect(result.current.isSuccess).toBe(false)
  })

  it('calls Projects.postApiProjects with correct data on mutate', async () => {
    const mockProject = {
      id: 'project-123',
      name: 'Test Project',
      localPath: '/path/to/repo',
      defaultBranch: 'main',
    }

    vi.mocked(Projects.postApiProjects).mockResolvedValue({
      data: mockProject,
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })

    const { result } = renderHook(() => useCreateProject(), {
      wrapper: createWrapper(),
    })

    result.current.mutate({
      name: 'Test Project',
      ownerRepo: 'owner/repo',
      defaultBranch: 'main',
    })

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(Projects.postApiProjects).toHaveBeenCalledWith({
      body: {
        name: 'Test Project',
        ownerRepo: 'owner/repo',
        defaultBranch: 'main',
      },
    })

    expect(result.current.data).toEqual(mockProject)
  })

  it('handles API errors correctly', async () => {
    const mockError = { title: 'Project already exists' }
    vi.mocked(Projects.postApiProjects).mockResolvedValue({
      data: undefined,
      error: mockError as never,
      request: new Request('http://test'),
      response: new Response(null, { status: 400 }),
    })

    const { result } = renderHook(() => useCreateProject(), {
      wrapper: createWrapper(),
    })

    result.current.mutate({
      name: 'Duplicate Project',
      ownerRepo: 'owner/repo',
    })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })

    expect(result.current.error).toBeDefined()
  })

  it('calls onSuccess callback when mutation succeeds', async () => {
    const mockProject = {
      id: 'project-456',
      name: 'New Project',
      localPath: '/path/to/repo',
      defaultBranch: 'develop',
    }

    vi.mocked(Projects.postApiProjects).mockResolvedValue({
      data: mockProject,
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })

    const onSuccess = vi.fn()

    const { result } = renderHook(() => useCreateProject({ onSuccess }), {
      wrapper: createWrapper(),
    })

    result.current.mutate({
      name: 'New Project',
      ownerRepo: 'owner/new-repo',
      defaultBranch: 'develop',
    })

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(onSuccess).toHaveBeenCalledWith(mockProject)
  })

  it('calls onError callback when mutation fails', async () => {
    const mockError = { title: 'Network error' }
    vi.mocked(Projects.postApiProjects).mockResolvedValue({
      data: undefined,
      error: mockError as never,
      request: new Request('http://test'),
      response: new Response(null, { status: 500 }),
    })

    const onError = vi.fn()

    const { result } = renderHook(() => useCreateProject({ onError }), {
      wrapper: createWrapper(),
    })

    result.current.mutate({
      name: 'Failed Project',
      ownerRepo: 'owner/repo',
    })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })

    expect(onError).toHaveBeenCalled()
  })
})
