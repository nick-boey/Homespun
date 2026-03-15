import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import React, { type ReactNode } from 'react'
import { useIssueContext } from './use-issue-context'
import { Issues } from '@/api'
import { IssueType, IssueStatus } from '@/api/generated/types.gen'

vi.mock('@/api', async (importOriginal) => {
  const actual = await importOriginal()
  return {
    ...(actual as object),
    Issues: {
      getApiIssuesByIssueId: vi.fn(),
    },
  }
})

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  return ({ children }: { children: ReactNode }) => {
    return React.createElement(QueryClientProvider, { client: queryClient }, children)
  }
}

describe('useIssueContext', () => {
  const wrapper = createWrapper()

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('returns undefined when entityId is null', () => {
    const { result } = renderHook(() => useIssueContext(null, 'project-1'), {
      wrapper,
    })

    expect(result.current.data).toBeUndefined()
    expect(result.current.isLoading).toBe(false)
  })

  it('returns undefined when entityId is undefined', () => {
    const { result } = renderHook(() => useIssueContext(undefined, 'project-1'), {
      wrapper,
    })

    expect(result.current.data).toBeUndefined()
    expect(result.current.isLoading).toBe(false)
  })

  it('returns undefined for PR entities', () => {
    const { result } = renderHook(() => useIssueContext('PR-123', 'project-1'), {
      wrapper,
    })

    // Query should not run for PR entities
    expect(result.current.data).toBeUndefined()
    expect(result.current.isLoading).toBe(false)
  })

  it('fetches issue context for valid issue', async () => {
    vi.mocked(Issues.getApiIssuesByIssueId).mockResolvedValue({
      data: {
        id: 'abc123',
        title: 'Test Issue',
        description: 'Test description',
        type: IssueType.FEATURE,
        status: IssueStatus.OPEN,
        workingBranchId: 'my-feature',
      },
      request: {} as Request,
      response: {} as Response,
    })

    const { result } = renderHook(() => useIssueContext('abc123', 'project-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))

    expect(result.current.data).toEqual({
      title: 'Test Issue',
      id: 'abc123',
      description: 'Test description',
      branch: 'feature/my-feature+abc123',
      type: 'Feature',
    })
  })

  it('handles issue without workingBranchId', async () => {
    vi.mocked(Issues.getApiIssuesByIssueId).mockResolvedValue({
      data: {
        id: 'abc123',
        title: 'Simple Task',
        description: 'A task with no branch',
        type: IssueType.TASK,
        status: IssueStatus.OPEN,
        workingBranchId: null,
      },
      request: {} as Request,
      response: {} as Response,
    })

    const { result } = renderHook(() => useIssueContext('abc123', 'project-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))

    expect(result.current.data).toEqual({
      title: 'Simple Task',
      id: 'abc123',
      description: 'A task with no branch',
      branch: 'task/simple-task+abc123',
      type: 'Task',
    })
  })

  it('handles empty description', async () => {
    vi.mocked(Issues.getApiIssuesByIssueId).mockResolvedValue({
      data: {
        id: 'abc123',
        title: 'Test Issue',
        description: null,
        type: IssueType.BUG,
        status: IssueStatus.OPEN,
        workingBranchId: 'fix-bug',
      },
      request: {} as Request,
      response: {} as Response,
    })

    const { result } = renderHook(() => useIssueContext('abc123', 'project-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))

    expect(result.current.data?.description).toBe('')
  })

  it('capitalizes issue type for display', async () => {
    vi.mocked(Issues.getApiIssuesByIssueId).mockResolvedValue({
      data: {
        id: 'abc123',
        title: 'Test',
        description: 'Test',
        type: IssueType.CHORE,
        status: IssueStatus.OPEN,
        workingBranchId: 'cleanup',
      },
      request: {} as Request,
      response: {} as Response,
    })

    const { result } = renderHook(() => useIssueContext('abc123', 'project-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => expect(result.current.isSuccess).toBe(true))

    expect(result.current.data?.type).toBe('Chore')
  })
})
