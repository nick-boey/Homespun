import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement, type ReactNode } from 'react'
import { useEnrichedClones, enrichedClonesQueryKey } from './use-enriched-clones'
import { ProjectClones } from '@/api'
import { PullRequestStatus, type EnrichedCloneInfo } from '@/api/generated/types.gen'

vi.mock('@/api', () => ({
  ProjectClones: {
    getApiProjectsByProjectIdClonesEnriched: vi.fn(),
  },
}))

const mockEnrichedClones: EnrichedCloneInfo[] = [
  {
    clone: {
      path: '/repos/.clones/feature+test-1',
      workdirPath: '/repos/.clones/feature+test-1/workdir',
      branch: 'refs/heads/feature/test-1',
      headCommit: 'abc123',
      isBare: false,
      isDetached: false,
      expectedBranch: 'feature/test-1',
    },
    linkedIssueId: 'issue-1',
    linkedIssue: {
      id: 'issue-1',
      title: 'Test Issue',
      status: 'Open',
      type: 'task',
    },
    linkedPr: undefined,
    isDeletable: true,
    deletionReason: null,
    isIssuesAgentClone: false,
  },
  {
    clone: {
      path: '/repos/.clones/feature+test-2',
      workdirPath: '/repos/.clones/feature+test-2/workdir',
      branch: 'refs/heads/feature/test-2',
      headCommit: 'def456',
      isBare: false,
      isDetached: false,
      expectedBranch: 'feature/test-2',
    },
    linkedIssueId: null,
    linkedIssue: undefined,
    linkedPr: {
      number: 123,
      title: 'Test PR',
      status: PullRequestStatus.IN_PROGRESS,
      htmlUrl: 'https://github.com/test/repo/pull/123',
    },
    isDeletable: false,
    deletionReason: 'Has active PR',
    isIssuesAgentClone: false,
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

describe('enrichedClonesQueryKey', () => {
  it('returns query key with project id', () => {
    const key = enrichedClonesQueryKey('project-1')
    expect(key).toEqual(['clones', 'enriched', 'project-1'])
  })
})

describe('useEnrichedClones', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('returns enriched clone data', async () => {
    const mockGetEnriched = ProjectClones.getApiProjectsByProjectIdClonesEnriched as Mock
    mockGetEnriched.mockResolvedValueOnce({ data: mockEnrichedClones })

    const { result } = renderHook(() => useEnrichedClones('project-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(result.current.data).toEqual(mockEnrichedClones)
    expect(mockGetEnriched).toHaveBeenCalledWith({
      path: { projectId: 'project-1' },
    })
  })

  it('handles loading state', () => {
    const mockGetEnriched = ProjectClones.getApiProjectsByProjectIdClonesEnriched as Mock
    mockGetEnriched.mockReturnValue(new Promise(() => {}))

    const { result } = renderHook(() => useEnrichedClones('project-1'), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(true)
    expect(result.current.data).toBeUndefined()
  })

  it('handles error state', async () => {
    const mockGetEnriched = ProjectClones.getApiProjectsByProjectIdClonesEnriched as Mock
    mockGetEnriched.mockResolvedValueOnce({
      error: { detail: 'Project not found' },
    })

    const { result } = renderHook(() => useEnrichedClones('nonexistent'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })

    expect(result.current.error?.message).toBe('Project not found')
  })

  it('does not fetch when projectId is empty', () => {
    const mockGetEnriched = ProjectClones.getApiProjectsByProjectIdClonesEnriched as Mock

    const { result } = renderHook(() => useEnrichedClones(''), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(false)
    expect(mockGetEnriched).not.toHaveBeenCalled()
  })
})
