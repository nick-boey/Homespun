import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useSessionBranchInfo } from './use-session-branch-info'
import { Clones } from '@/api'
import React, { type ReactNode } from 'react'
import type { ClaudeSession } from '@/types/signalr'

// Mock the API module
vi.mock('@/api', () => ({
  Clones: {
    getApiClonesSessionBranchInfo: vi.fn(),
  },
}))

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  return ({ children }: { children: ReactNode }) => {
    return React.createElement(QueryClientProvider, { client: queryClient }, children)
  }
}

const createMockSession = (overrides: Partial<ClaudeSession> = {}): ClaudeSession => ({
  id: 'session-1',
  entityId: 'clone:feature-branch',
  projectId: 'project-1',
  workingDirectory: '/path/to/clone/workdir',
  model: 'opus',
  mode: 'build',
  status: 'running',
  createdAt: '2024-01-01T00:00:00Z',
  lastActivityAt: '2024-01-01T00:00:00Z',
  messages: [],
  totalCostUsd: 0,
  totalDurationMs: 0,
  hasPendingPlanApproval: false,
  contextClearMarkers: [],
  ...overrides,
})

describe('useSessionBranchInfo', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('fetches branch info for clone sessions', async () => {
    const mockBranchInfo = {
      data: {
        branchName: 'feature/test',
        commitSha: 'abc1234',
        commitMessage: 'Add feature',
        commitDate: '2024-03-15T10:30:00Z',
        aheadCount: 2,
        behindCount: 1,
        hasUncommittedChanges: true,
      },
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    }
    vi.mocked(Clones.getApiClonesSessionBranchInfo).mockResolvedValue(mockBranchInfo)

    const session = createMockSession()
    const { result } = renderHook(() => useSessionBranchInfo(session), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.data).toEqual(mockBranchInfo.data)
    })

    expect(Clones.getApiClonesSessionBranchInfo).toHaveBeenCalledWith({
      query: { workingDirectory: '/path/to/clone/workdir' },
    })
  })

  it('does not fetch when session is undefined', () => {
    const { result } = renderHook(() => useSessionBranchInfo(undefined), {
      wrapper: createWrapper(),
    })

    expect(result.current.data).toBeUndefined()
    expect(result.current.isLoading).toBe(false)
    expect(Clones.getApiClonesSessionBranchInfo).not.toHaveBeenCalled()
  })

  it('does not fetch for non-clone sessions (issue)', () => {
    const session = createMockSession({ entityId: 'issue-123' })
    const { result } = renderHook(() => useSessionBranchInfo(session), {
      wrapper: createWrapper(),
    })

    expect(result.current.data).toBeUndefined()
    expect(result.current.isLoading).toBe(false)
    expect(Clones.getApiClonesSessionBranchInfo).not.toHaveBeenCalled()
  })

  it('does not fetch for non-clone sessions (pr)', () => {
    const session = createMockSession({ entityId: 'pr-456' })
    const { result } = renderHook(() => useSessionBranchInfo(session), {
      wrapper: createWrapper(),
    })

    expect(result.current.data).toBeUndefined()
    expect(result.current.isLoading).toBe(false)
    expect(Clones.getApiClonesSessionBranchInfo).not.toHaveBeenCalled()
  })

  it('does not fetch when workingDirectory is null', () => {
    const session = createMockSession({
      entityId: 'clone:feature-branch',
      workingDirectory: null as unknown as string,
    })
    const { result } = renderHook(() => useSessionBranchInfo(session), {
      wrapper: createWrapper(),
    })

    expect(result.current.data).toBeUndefined()
    expect(Clones.getApiClonesSessionBranchInfo).not.toHaveBeenCalled()
  })

  it('handles API errors gracefully', async () => {
    const error = new Error('API Error')
    vi.mocked(Clones.getApiClonesSessionBranchInfo).mockRejectedValue(error)

    const session = createMockSession()
    const { result } = renderHook(() => useSessionBranchInfo(session), {
      wrapper: createWrapper(),
    })

    await waitFor(
      () => {
        expect(result.current.isError).toBe(true)
      },
      { timeout: 2000 }
    )

    expect(result.current.error).toBe(error)
    expect(result.current.data).toBeUndefined()
  })

  it('refetches when session workingDirectory changes', async () => {
    const mockBranchInfo1 = {
      data: {
        branchName: 'feature/branch-1',
        commitSha: 'abc1234',
        commitMessage: 'First',
        commitDate: '2024-03-15T10:30:00Z',
        aheadCount: 0,
        behindCount: 0,
        hasUncommittedChanges: false,
      },
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    }
    const mockBranchInfo2 = {
      data: {
        branchName: 'feature/branch-2',
        commitSha: 'def5678',
        commitMessage: 'Second',
        commitDate: '2024-03-16T11:00:00Z',
        aheadCount: 1,
        behindCount: 0,
        hasUncommittedChanges: true,
      },
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    }

    vi.mocked(Clones.getApiClonesSessionBranchInfo)
      .mockResolvedValueOnce(mockBranchInfo1)
      .mockResolvedValueOnce(mockBranchInfo2)

    const session1 = createMockSession({ workingDirectory: '/path/1' })
    const session2 = createMockSession({ workingDirectory: '/path/2' })

    const { result, rerender } = renderHook(({ session }) => useSessionBranchInfo(session), {
      wrapper: createWrapper(),
      initialProps: { session: session1 },
    })

    await waitFor(() => {
      expect(result.current.data?.branchName).toBe('feature/branch-1')
    })

    expect(Clones.getApiClonesSessionBranchInfo).toHaveBeenCalledTimes(1)

    // Re-render with different session
    rerender({ session: session2 })

    await waitFor(() => {
      expect(result.current.data?.branchName).toBe('feature/branch-2')
    })

    expect(Clones.getApiClonesSessionBranchInfo).toHaveBeenCalledTimes(2)
  })

  it('returns isCloneSession helper correctly', async () => {
    const mockBranchInfo = {
      data: {
        branchName: 'feature/test',
        commitSha: 'abc1234',
        commitMessage: 'Test',
        commitDate: '2024-03-15T10:30:00Z',
        aheadCount: 0,
        behindCount: 0,
        hasUncommittedChanges: false,
      },
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    }
    vi.mocked(Clones.getApiClonesSessionBranchInfo).mockResolvedValue(mockBranchInfo)

    // Clone session
    const cloneSession = createMockSession({ entityId: 'clone:feature-branch' })
    const { result: cloneResult } = renderHook(() => useSessionBranchInfo(cloneSession), {
      wrapper: createWrapper(),
    })

    expect(cloneResult.current.isCloneSession).toBe(true)

    // Non-clone session
    const issueSession = createMockSession({ entityId: 'issue-123' })
    const { result: issueResult } = renderHook(() => useSessionBranchInfo(issueSession), {
      wrapper: createWrapper(),
    })

    expect(issueResult.current.isCloneSession).toBe(false)
  })
})
