import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { ProjectSearch } from '@/api'
import { useSearchablePrs, searchablePrsQueryKey } from './use-searchable-prs'

// Mock the API
vi.mock('@/api', () => ({
  ProjectSearch: {
    getApiProjectsByProjectIdSearchPrs: vi.fn(),
  },
}))

const mockGetPrs = vi.mocked(ProjectSearch.getApiProjectsByProjectIdSearchPrs)

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

describe('useSearchablePrs', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  afterEach(() => {
    vi.resetAllMocks()
  })

  it('returns PRs on successful fetch', async () => {
    const mockPrs = [
      { number: 123, title: 'Add feature X', branchName: 'feature/x' },
      { number: 456, title: 'Fix bug Y', branchName: 'fix/y' },
    ]
    const mockHash = 'def456'

    mockGetPrs.mockResolvedValue({
      data: { prs: mockPrs, hash: mockHash },
      error: undefined,
    } as never)

    const { result } = renderHook(() => useSearchablePrs('project-1'), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(true)

    await waitFor(() => expect(result.current.isSuccess).toBe(true))

    expect(result.current.prs).toEqual(mockPrs)
    expect(result.current.hash).toBe(mockHash)
  })

  it('handles errors gracefully', async () => {
    mockGetPrs.mockResolvedValue({
      data: undefined,
      error: { detail: 'Project not found' },
    } as never)

    const { result } = renderHook(() => useSearchablePrs('nonexistent'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => expect(result.current.isError).toBe(true))

    expect(result.current.error?.message).toBe('Project not found')
  })

  it('does not fetch when projectId is empty', () => {
    renderHook(() => useSearchablePrs(''), {
      wrapper: createWrapper(),
    })

    expect(mockGetPrs).not.toHaveBeenCalled()
  })

  it('exports correct query key function', () => {
    const key = searchablePrsQueryKey('test-project')
    expect(key).toEqual(['searchable-prs', 'test-project'])
  })
})
