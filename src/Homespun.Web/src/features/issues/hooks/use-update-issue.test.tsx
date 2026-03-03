import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor, act } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { Issues, type IssueResponse, IssueStatus } from '@/api'
import { useUpdateIssue } from './use-update-issue'
import type { ReactNode } from 'react'

vi.mock('@/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/api')>()
  return {
    ...actual,
    Issues: {
      putApiIssuesByIssueId: vi.fn(),
    },
  }
})

const createWrapper = () => {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  })
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={queryClient}>{children}</QueryClientProvider>
  )
}

describe('useUpdateIssue', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('updates issue successfully', async () => {
    const updatedIssue: IssueResponse = {
      id: 'issue-123',
      title: 'Updated Title',
      status: IssueStatus[1],
    }

    vi.mocked(Issues.putApiIssuesByIssueId).mockResolvedValue({
      data: updatedIssue,
      error: undefined,
      request: new Request('http://test'),
      response: new Response(),
    })

    const onSuccess = vi.fn()
    const { result } = renderHook(() => useUpdateIssue({ onSuccess }), {
      wrapper: createWrapper(),
    })

    act(() => {
      result.current.mutate({
        issueId: 'issue-123',
        data: {
          projectId: 'project-1',
          title: 'Updated Title',
          status: 1,
        },
      })
    })

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(Issues.putApiIssuesByIssueId).toHaveBeenCalledWith({
      path: { issueId: 'issue-123' },
      body: {
        projectId: 'project-1',
        title: 'Updated Title',
        status: 1,
      },
    })
    expect(onSuccess).toHaveBeenCalledWith(updatedIssue)
  })

  it('handles error when update fails', async () => {
    vi.mocked(Issues.putApiIssuesByIssueId).mockResolvedValue({
      data: undefined,
      error: { detail: 'Issue not found' },
      request: new Request('http://test'),
      response: new Response(null, { status: 404 }),
    })

    const onError = vi.fn()
    const { result } = renderHook(() => useUpdateIssue({ onError }), {
      wrapper: createWrapper(),
    })

    act(() => {
      result.current.mutate({
        issueId: 'invalid-id',
        data: {
          projectId: 'project-1',
          title: 'Test',
        },
      })
    })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })

    expect(onError).toHaveBeenCalled()
  })
})
