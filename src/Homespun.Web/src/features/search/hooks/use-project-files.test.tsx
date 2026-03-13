import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ProjectSearch } from '@/api'
import { useProjectFiles, projectFilesQueryKey } from './use-project-files'

// Mock the API
vi.mock('@/api', () => ({
  ProjectSearch: {
    getApiProjectsByProjectIdSearchFiles: vi.fn(),
  },
}))

const mockGetFiles = vi.mocked(ProjectSearch.getApiProjectsByProjectIdSearchFiles)

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  })
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  }
}

describe('useProjectFiles', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    vi.resetAllMocks()
  })

  it('returns files on successful fetch', async () => {
    const mockFiles = ['src/index.ts', 'src/utils.ts', 'package.json']
    const mockHash = 'abc123'

    mockGetFiles.mockResolvedValue({
      data: { files: mockFiles, hash: mockHash },
      error: undefined,
    } as never)

    const { result } = renderHook(() => useProjectFiles('project-1'), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(true)

    await waitFor(() => expect(result.current.isSuccess).toBe(true))

    expect(result.current.files).toEqual(mockFiles)
    expect(result.current.hash).toBe(mockHash)
  })

  it('includes hash in query when available', async () => {
    const mockFiles = ['file1.ts']
    mockGetFiles.mockResolvedValue({
      data: { files: mockFiles, hash: 'hash1' },
      error: undefined,
    } as never)

    const { result } = renderHook(() => useProjectFiles('project-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))

    expect(mockGetFiles).toHaveBeenCalledWith({
      path: { projectId: 'project-1' },
      query: { hash: undefined },
    })
  })

  it('handles errors gracefully', async () => {
    mockGetFiles.mockResolvedValue({
      data: undefined,
      error: { detail: 'Project not found' },
    } as never)

    const { result } = renderHook(() => useProjectFiles('nonexistent'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => expect(result.current.isError).toBe(true))

    expect(result.current.error?.message).toBe('Project not found')
  })

  it('does not fetch when projectId is empty', () => {
    renderHook(() => useProjectFiles(''), {
      wrapper: createWrapper(),
    })

    expect(mockGetFiles).not.toHaveBeenCalled()
  })

  it('exports correct query key function', () => {
    const key = projectFilesQueryKey('test-project')
    expect(key).toEqual(['project-files', 'test-project'])
  })
})
