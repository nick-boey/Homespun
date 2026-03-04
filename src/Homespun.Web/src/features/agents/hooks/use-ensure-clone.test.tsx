import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useEnsureClone } from './use-ensure-clone'
import { Clones, Issues } from '@/api'
import type { ReactNode } from 'react'
import type {
  CloneExistsResponse,
  CreateCloneResponse,
  ResolvedBranchResponse,
} from '@/api/generated/types.gen'

vi.mock('@/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api')>()
  return {
    ...actual,
    Clones: {
      getApiClonesExists: vi.fn(),
      postApiClones: vi.fn(),
    },
    Issues: {
      getApiIssuesByIssueIdResolvedBranch: vi.fn(),
    },
  }
})

const mockGetApiClonesExists = vi.mocked(Clones.getApiClonesExists)
const mockPostApiClones = vi.mocked(Clones.postApiClones)
const mockGetResolvedBranch = vi.mocked(Issues.getApiIssuesByIssueIdResolvedBranch)

// Helper to create mock API response
function createMockResponse<T>(data: T) {
  return {
    data,
    error: undefined,
    request: new Request('http://localhost/api/test'),
    response: new Response(),
  }
}

// Helper to create mock error response
function createMockErrorResponse(detail: string, status = 404) {
  return {
    data: undefined,
    error: { detail },
    request: new Request('http://localhost/api/test'),
    response: new Response(null, { status }),
  }
}

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  })
  return function Wrapper({ children }: { children: ReactNode }) {
    return <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  }
}

describe('useEnsureClone', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('resolves branch name and checks if clone exists', async () => {
    const resolvedBranch: ResolvedBranchResponse = { branchName: 'feature/test-123' }
    const cloneExists: CloneExistsResponse = { exists: true }

    mockGetResolvedBranch.mockResolvedValue(createMockResponse(resolvedBranch))
    mockGetApiClonesExists.mockResolvedValue(createMockResponse(cloneExists))

    const { result } = renderHook(
      () => useEnsureClone({ projectId: 'project-1', issueId: 'issue-123' }),
      { wrapper: createWrapper() }
    )

    expect(result.current.isLoading).toBe(true)

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(mockGetResolvedBranch).toHaveBeenCalledWith({
      path: { issueId: 'issue-123' },
      query: { projectId: 'project-1' },
    })
    expect(mockGetApiClonesExists).toHaveBeenCalledWith({
      query: { projectId: 'project-1', branchName: 'feature/test-123' },
    })
    expect(result.current.branchName).toBe('feature/test-123')
    expect(result.current.cloneExists).toBe(true)
  })

  it('returns cloneExists false when clone does not exist', async () => {
    const resolvedBranch: ResolvedBranchResponse = { branchName: 'feature/new-branch' }
    const cloneExists: CloneExistsResponse = { exists: false }

    mockGetResolvedBranch.mockResolvedValue(createMockResponse(resolvedBranch))
    mockGetApiClonesExists.mockResolvedValue(createMockResponse(cloneExists))

    const { result } = renderHook(
      () => useEnsureClone({ projectId: 'project-1', issueId: 'issue-456' }),
      { wrapper: createWrapper() }
    )

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.branchName).toBe('feature/new-branch')
    expect(result.current.cloneExists).toBe(false)
  })

  it('creates clone when ensureClone is called and clone does not exist', async () => {
    const resolvedBranch: ResolvedBranchResponse = { branchName: 'feature/new-branch' }
    const cloneExists: CloneExistsResponse = { exists: false }
    const createCloneResponse: CreateCloneResponse = {
      path: '/clones/project-1/feature-new-branch',
      branchName: 'feature/new-branch',
    }

    mockGetResolvedBranch.mockResolvedValue(createMockResponse(resolvedBranch))
    mockGetApiClonesExists.mockResolvedValue(createMockResponse(cloneExists))
    mockPostApiClones.mockResolvedValue(createMockResponse(createCloneResponse))

    const { result } = renderHook(
      () => useEnsureClone({ projectId: 'project-1', issueId: 'issue-789' }),
      { wrapper: createWrapper() }
    )

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.cloneExists).toBe(false)

    // Call ensureClone to create the clone
    const clonePath = await result.current.ensureClone()

    expect(mockPostApiClones).toHaveBeenCalledWith({
      body: {
        projectId: 'project-1',
        branchName: 'feature/new-branch',
        createBranch: true,
      },
    })
    expect(clonePath).toBe('/clones/project-1/feature-new-branch')
  })

  it('returns existing clone path when clone already exists', async () => {
    const resolvedBranch: ResolvedBranchResponse = { branchName: 'feature/existing' }
    // When clone exists, the check API returns exists: true
    // but we still need to create clone to get the path (or use a different API)
    // Actually, let's check if the Clones.getApiClones returns path info
    const cloneExists: CloneExistsResponse = { exists: true }
    const createCloneResponse: CreateCloneResponse = {
      path: '/clones/project-1/feature-existing',
      branchName: 'feature/existing',
    }

    mockGetResolvedBranch.mockResolvedValue(createMockResponse(resolvedBranch))
    mockGetApiClonesExists.mockResolvedValue(createMockResponse(cloneExists))
    // When clone exists, postApiClones should still be called (it's idempotent) and return the path
    mockPostApiClones.mockResolvedValue(createMockResponse(createCloneResponse))

    const { result } = renderHook(
      () => useEnsureClone({ projectId: 'project-1', issueId: 'issue-existing' }),
      { wrapper: createWrapper() }
    )

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    // Call ensureClone - it should still work even if clone exists
    const clonePath = await result.current.ensureClone()

    expect(clonePath).toBe('/clones/project-1/feature-existing')
  })

  it('handles error when resolving branch name fails', async () => {
    mockGetResolvedBranch.mockResolvedValue(createMockErrorResponse('Issue not found'))

    const { result } = renderHook(
      () => useEnsureClone({ projectId: 'project-1', issueId: 'invalid-issue' }),
      { wrapper: createWrapper() }
    )

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })

    expect(result.current.error?.message).toContain('Issue not found')
  })

  it('handles error when creating clone fails', async () => {
    const resolvedBranch: ResolvedBranchResponse = { branchName: 'feature/fail-clone' }
    const cloneExists: CloneExistsResponse = { exists: false }

    mockGetResolvedBranch.mockResolvedValue(createMockResponse(resolvedBranch))
    mockGetApiClonesExists.mockResolvedValue(createMockResponse(cloneExists))
    mockPostApiClones.mockResolvedValue(createMockErrorResponse('Clone creation failed'))

    const { result } = renderHook(
      () => useEnsureClone({ projectId: 'project-1', issueId: 'issue-fail' }),
      { wrapper: createWrapper() }
    )

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    // Call ensureClone - it should throw an error
    await expect(result.current.ensureClone()).rejects.toThrow('Clone creation failed')
  })

  it('does not fetch when issueId is empty', () => {
    const { result } = renderHook(() => useEnsureClone({ projectId: 'project-1', issueId: '' }), {
      wrapper: createWrapper(),
    })

    expect(mockGetResolvedBranch).not.toHaveBeenCalled()
    expect(result.current.isLoading).toBe(false)
    expect(result.current.branchName).toBeUndefined()
  })

  it('does not fetch when projectId is empty', () => {
    const { result } = renderHook(() => useEnsureClone({ projectId: '', issueId: 'issue-123' }), {
      wrapper: createWrapper(),
    })

    expect(mockGetResolvedBranch).not.toHaveBeenCalled()
    expect(result.current.isLoading).toBe(false)
    expect(result.current.branchName).toBeUndefined()
  })

  it('tracks isCreating state during clone creation', async () => {
    const resolvedBranch: ResolvedBranchResponse = { branchName: 'feature/creating' }
    const cloneExists: CloneExistsResponse = { exists: false }

    let resolveClonePromise: (value: ReturnType<typeof createMockResponse>) => void
    const delayedClonePromise = new Promise<ReturnType<typeof createMockResponse>>((resolve) => {
      resolveClonePromise = resolve
    })

    mockGetResolvedBranch.mockResolvedValue(createMockResponse(resolvedBranch))
    mockGetApiClonesExists.mockResolvedValue(createMockResponse(cloneExists))
    mockPostApiClones.mockReturnValue(delayedClonePromise as never)

    const { result } = renderHook(
      () => useEnsureClone({ projectId: 'project-1', issueId: 'issue-creating' }),
      { wrapper: createWrapper() }
    )

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    // Start creating clone
    const ensurePromise = result.current.ensureClone()

    // Should be in creating state
    await waitFor(() => {
      expect(result.current.isCreating).toBe(true)
    })

    // Resolve the promise
    const createCloneResponse: CreateCloneResponse = {
      path: '/clones/project-1/feature-creating',
      branchName: 'feature/creating',
    }
    resolveClonePromise!(createMockResponse(createCloneResponse))

    await ensurePromise

    // Should no longer be creating
    await waitFor(() => {
      expect(result.current.isCreating).toBe(false)
    })
  })
})
