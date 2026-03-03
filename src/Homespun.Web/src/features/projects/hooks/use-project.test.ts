import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import * as React from 'react'
import { useProject } from './use-project'
import { Projects } from '@/api'

vi.mock('@/api', () => ({
  Projects: {
    getApiProjectsById: vi.fn(),
  },
}))

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  })
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return React.createElement(QueryClientProvider, { client: queryClient }, children)
  }
}

describe('useProject', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('returns loading state initially', () => {
    vi.mocked(Projects.getApiProjectsById).mockReturnValue(
      new Promise(() => {}) as ReturnType<typeof Projects.getApiProjectsById>
    )

    const { result } = renderHook(() => useProject('test-project-id'), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(true)
    expect(result.current.project).toBeUndefined()
  })

  it('returns project data on success', async () => {
    const mockProject = {
      id: 'test-project-id',
      name: 'Test Project',
      localPath: '/path/to/project',
      defaultBranch: 'main',
    }

    vi.mocked(Projects.getApiProjectsById).mockResolvedValue({
      data: mockProject,
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as Awaited<ReturnType<typeof Projects.getApiProjectsById>>)

    const { result } = renderHook(() => useProject('test-project-id'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(result.current.project).toEqual(mockProject)
    expect(result.current.isError).toBe(false)
  })

  it('returns error state when project not found', async () => {
    vi.mocked(Projects.getApiProjectsById).mockResolvedValue({
      data: undefined,
      response: new Response(null, { status: 404 }),
      request: new Request('http://test'),
      error: { detail: 'Project not found' },
    } as Awaited<ReturnType<typeof Projects.getApiProjectsById>>)

    const { result } = renderHook(() => useProject('nonexistent-id'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(result.current.isError).toBe(true)
    expect(result.current.project).toBeUndefined()
  })

  it('calls API with correct project ID', async () => {
    vi.mocked(Projects.getApiProjectsById).mockResolvedValue({
      data: { id: 'my-id', name: 'My Project', localPath: '/', defaultBranch: 'main' },
      response: new Response(),
      request: new Request('http://test'),
      error: undefined,
    } as Awaited<ReturnType<typeof Projects.getApiProjectsById>>)

    renderHook(() => useProject('my-id'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(Projects.getApiProjectsById).toHaveBeenCalledWith({
        path: { id: 'my-id' },
      })
    })
  })
})
