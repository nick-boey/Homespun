import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement, type ReactNode } from 'react'
import { useBranches, getRemoteOnlyBranches, getLocalBranches } from './use-branches'
import { Clones } from '@/api'
import type { BranchInfo } from '@/api/generated/types.gen'

vi.mock('@/api', () => ({
  Clones: {
    getApiClonesBranches: vi.fn(),
  },
}))

// Using BranchInfoWritable for test data as hasRemote is readonly in BranchInfo
// but the API returns it computed from upstream
const mockBranches: BranchInfo[] = [
  {
    name: 'refs/heads/main',
    shortName: 'main',
    isCurrent: false,
    commitSha: 'abc123',
    upstream: 'origin/main',
    aheadCount: 0,
    behindCount: 0,
    hasClone: false,
    isMerged: false,
    lastCommitMessage: 'Initial commit',
    lastCommitDate: '2024-01-01T00:00:00Z',
  } as BranchInfo,
  {
    name: 'refs/heads/feature/test-1',
    shortName: 'feature/test-1',
    isCurrent: false,
    commitSha: 'def456',
    upstream: 'origin/feature/test-1',
    aheadCount: 2,
    behindCount: 1,
    hasClone: true,
    clonePath: '/repos/.clones/feature+test-1',
    isMerged: false,
    lastCommitMessage: 'Add tests',
    lastCommitDate: '2024-01-15T00:00:00Z',
  } as BranchInfo,
  {
    name: 'refs/heads/feature/remote-only',
    shortName: 'feature/remote-only',
    isCurrent: false,
    commitSha: 'ghi789',
    upstream: 'origin/feature/remote-only',
    hasRemote: true, // Explicitly set since it's computed from upstream on server
    aheadCount: 0,
    behindCount: 0,
    hasClone: false,
    isMerged: false,
    lastCommitMessage: 'Remote work',
    lastCommitDate: '2024-01-10T00:00:00Z',
  } as BranchInfo,
  {
    name: 'refs/heads/feature/local-only',
    shortName: 'feature/local-only',
    isCurrent: false,
    commitSha: 'jkl012',
    hasRemote: false, // No upstream
    aheadCount: 3,
    behindCount: 0,
    hasClone: true,
    clonePath: '/repos/.clones/feature+local-only',
    isMerged: false,
    lastCommitMessage: 'Local changes',
    lastCommitDate: '2024-01-20T00:00:00Z',
  } as BranchInfo,
]

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
    },
  })
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: queryClient }, children)
}

describe('useBranches', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('fetches branches successfully', async () => {
    const mockGetApiClonesBranches = Clones.getApiClonesBranches as Mock
    mockGetApiClonesBranches.mockResolvedValueOnce({ data: mockBranches })

    const { result } = renderHook(() => useBranches('/repos/project'), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(true)

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(result.current.data).toEqual(mockBranches)
    expect(mockGetApiClonesBranches).toHaveBeenCalledWith({
      query: { repoPath: '/repos/project' },
    })
  })

  it('does not fetch when repoPath is empty', () => {
    const mockGetApiClonesBranches = Clones.getApiClonesBranches as Mock

    const { result } = renderHook(() => useBranches(''), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(false)
    expect(mockGetApiClonesBranches).not.toHaveBeenCalled()
  })

  it('handles error response', async () => {
    const mockGetApiClonesBranches = Clones.getApiClonesBranches as Mock
    mockGetApiClonesBranches.mockResolvedValueOnce({
      error: { detail: 'Repository not found' },
    })

    const { result } = renderHook(() => useBranches('/nonexistent'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })

    expect(result.current.error?.message).toBe('Repository not found')
  })
})

describe('getRemoteOnlyBranches', () => {
  it('filters branches that are only on remote', () => {
    const result = getRemoteOnlyBranches(mockBranches)

    expect(result).toHaveLength(1)
    expect(result[0].shortName).toBe('feature/remote-only')
  })

  it('excludes main and master branches', () => {
    const branchesWithMaster: BranchInfo[] = [
      ...mockBranches,
      {
        name: 'refs/heads/master',
        shortName: 'master',
        isCurrent: false,
        upstream: 'origin/master',
        hasClone: false,
      },
    ]

    const result = getRemoteOnlyBranches(branchesWithMaster)

    expect(result.find((b) => b.shortName === 'main')).toBeUndefined()
    expect(result.find((b) => b.shortName === 'master')).toBeUndefined()
  })

  it('returns empty array when no remote-only branches exist', () => {
    const localOnlyBranches: BranchInfo[] = [
      {
        name: 'refs/heads/local-1',
        shortName: 'local-1',
        hasClone: true,
      },
    ]

    const result = getRemoteOnlyBranches(localOnlyBranches)

    expect(result).toHaveLength(0)
  })
})

describe('getLocalBranches', () => {
  it('filters branches that have local clones', () => {
    const result = getLocalBranches(mockBranches)

    expect(result).toHaveLength(2)
    expect(result.map((b) => b.shortName)).toContain('feature/test-1')
    expect(result.map((b) => b.shortName)).toContain('feature/local-only')
  })

  it('returns empty array when no local branches exist', () => {
    const remoteOnlyBranches: BranchInfo[] = [
      {
        name: 'refs/heads/remote-1',
        shortName: 'remote-1',
        hasClone: false,
        upstream: 'origin/remote-1',
      },
    ]

    const result = getLocalBranches(remoteOnlyBranches)

    expect(result).toHaveLength(0)
  })
})
