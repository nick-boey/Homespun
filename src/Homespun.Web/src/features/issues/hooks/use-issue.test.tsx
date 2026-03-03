import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Issues, type IssueResponse, IssueStatus, IssueType, ExecutionMode } from '@/api'
import { useIssue } from './use-issue'
import type { ReactNode } from 'react'

vi.mock('@/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api')>()
  return {
    ...actual,
    Issues: {
      getApiIssuesByIssueId: vi.fn(),
    },
  }
})

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
    },
  })
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  )
}

describe('useIssue', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('fetches issue data successfully', async () => {
    const mockIssue: IssueResponse = {
      id: 'issue-123',
      title: 'Test Issue',
      description: 'Test description',
      status: IssueStatus[0],
      type: IssueType[0],
      priority: 3,
      executionMode: ExecutionMode[0],
      workingBranchId: 'feature/test',
      parentIssues: [],
      tags: ['test'],
    }

    vi.mocked(Issues.getApiIssuesByIssueId).mockResolvedValue({
      data: mockIssue,
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })

    const { result } = renderHook(() => useIssue('issue-123', 'project-1'), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(true)

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(result.current.issue).toEqual(mockIssue)
    expect(Issues.getApiIssuesByIssueId).toHaveBeenCalledWith({
      path: { issueId: 'issue-123' },
      query: { projectId: 'project-1' },
    })
  })

  it('handles error when issue fetch fails', async () => {
    vi.mocked(Issues.getApiIssuesByIssueId).mockResolvedValue({
      data: undefined,
      error: { detail: 'Issue not found' },
      request: new Request('http://test'),
      response: new Response(null, { status: 404 }),
    })

    const { result } = renderHook(() => useIssue('invalid-id', 'project-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })

    expect(result.current.error?.message).toBe('Issue not found')
  })

  it('does not fetch when issueId is empty', () => {
    const { result } = renderHook(() => useIssue('', 'project-1'), {
      wrapper: createWrapper(),
    })

    expect(Issues.getApiIssuesByIssueId).not.toHaveBeenCalled()
    expect(result.current.isLoading).toBe(false)
  })

  it('does not fetch when projectId is empty', () => {
    const { result } = renderHook(() => useIssue('issue-123', ''), {
      wrapper: createWrapper(),
    })

    expect(Issues.getApiIssuesByIssueId).not.toHaveBeenCalled()
    expect(result.current.isLoading).toBe(false)
  })
})
