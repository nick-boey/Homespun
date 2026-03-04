import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClientProvider, QueryClient } from '@tanstack/react-query'
import { useBranches } from './use-branches'
import { Clones } from '@/api'
import type { BranchInfo } from '@/api'

vi.mock('@/api', () => ({
  Clones: {
    getApiClonesBranches: vi.fn(),
  },
}))

describe('useBranches', () => {
  let queryClient: QueryClient

  beforeEach(() => {
    queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    })
    vi.clearAllMocks()
  })

  const wrapper = ({ children }: { children: React.ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  )

  it('returns empty array when repoPath is undefined', async () => {
    const { result } = renderHook(() => useBranches(undefined), { wrapper })

    expect(result.current.branches).toEqual([])
    expect(result.current.isLoading).toBe(false)
  })

  it('fetches branches for a repo path', async () => {
    const mockBranches: BranchInfo[] = [
      { shortName: 'feature/test', name: 'refs/heads/feature/test' },
      { shortName: 'main', name: 'refs/heads/main' },
    ]

    vi.mocked(Clones.getApiClonesBranches).mockResolvedValue({
      data: mockBranches,
      response: {} as Response,
    })

    const { result } = renderHook(() => useBranches('/path/to/repo', 'main'), { wrapper })

    expect(result.current.isLoading).toBe(true)

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.branches).toHaveLength(2)
    // Default branch should be first
    expect(result.current.branches[0].shortName).toBe('main')
    expect(result.current.branches[1].shortName).toBe('feature/test')
  })

  it('handles errors gracefully', async () => {
    vi.mocked(Clones.getApiClonesBranches).mockResolvedValue({
      error: { detail: 'Failed to fetch branches' },
      response: {} as Response,
    })

    const { result } = renderHook(() => useBranches('/path/to/repo'), { wrapper })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })

    expect(result.current.error).toBeTruthy()
  })
})
