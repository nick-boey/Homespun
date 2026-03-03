import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement, type ReactNode } from 'react'
import { useCreateIssue } from './use-create-issue'
import { Issues, type IssueResponse, IssueType } from '@/api'

// Mock the API
vi.mock('@/api', () => ({
  Issues: {
    postApiIssues: vi.fn(),
  },
  IssueType: {
    0: 0,
    1: 1,
    2: 2,
    3: 3,
  },
}))

describe('useCreateIssue', () => {
  let queryClient: QueryClient

  const createWrapper = () => {
    return function Wrapper({ children }: { children: ReactNode }) {
      return createElement(QueryClientProvider, { client: queryClient }, children)
    }
  }

  beforeEach(() => {
    vi.clearAllMocks()
    queryClient = new QueryClient({
      defaultOptions: {
        queries: {
          retry: false,
        },
        mutations: {
          retry: false,
        },
      },
    })
  })

  it('creates an issue with title only', async () => {
    const mockIssue: IssueResponse = {
      id: 'abc123',
      title: 'Test Issue',
      type: IssueType[0],
    }

    vi.mocked(Issues.postApiIssues).mockResolvedValueOnce({
      data: mockIssue,
      request: {} as Request,
      response: {} as Response,
    })

    const { result } = renderHook(
      () => useCreateIssue({ projectId: 'proj-1' }),
      { wrapper: createWrapper() }
    )

    await result.current.createIssue({
      title: 'Test Issue',
    })

    await waitFor(() => {
      expect(vi.mocked(Issues.postApiIssues)).toHaveBeenCalledWith({
        body: {
          projectId: 'proj-1',
          title: 'Test Issue',
          type: 0,
          parentIssueId: undefined,
          childIssueId: undefined,
        },
      })
    })
  })

  it('creates an issue as child of parent (Shift+Tab case)', async () => {
    const mockIssue: IssueResponse = {
      id: 'abc123',
      title: 'New Child Issue',
      type: IssueType[0],
    }

    vi.mocked(Issues.postApiIssues).mockResolvedValueOnce({
      data: mockIssue,
      request: {} as Request,
      response: {} as Response,
    })

    const { result } = renderHook(
      () => useCreateIssue({ projectId: 'proj-1' }),
      { wrapper: createWrapper() }
    )

    await result.current.createIssue({
      title: 'New Child Issue',
      parentIssueId: 'parent-123',
    })

    await waitFor(() => {
      expect(vi.mocked(Issues.postApiIssues)).toHaveBeenCalledWith({
        body: {
          projectId: 'proj-1',
          title: 'New Child Issue',
          type: 0,
          parentIssueId: 'parent-123',
          childIssueId: undefined,
        },
      })
    })
  })

  it('creates an issue as parent of child (Tab case)', async () => {
    const mockIssue: IssueResponse = {
      id: 'abc123',
      title: 'New Parent Issue',
      type: IssueType[0],
    }

    vi.mocked(Issues.postApiIssues).mockResolvedValueOnce({
      data: mockIssue,
      request: {} as Request,
      response: {} as Response,
    })

    const { result } = renderHook(
      () => useCreateIssue({ projectId: 'proj-1' }),
      { wrapper: createWrapper() }
    )

    await result.current.createIssue({
      title: 'New Parent Issue',
      childIssueId: 'child-123',
    })

    await waitFor(() => {
      expect(vi.mocked(Issues.postApiIssues)).toHaveBeenCalledWith({
        body: {
          projectId: 'proj-1',
          title: 'New Parent Issue',
          type: 0,
          parentIssueId: undefined,
          childIssueId: 'child-123',
        },
      })
    })
  })

  it('returns the created issue on success', async () => {
    const mockIssue: IssueResponse = {
      id: 'abc123',
      title: 'Test Issue',
      type: IssueType[0],
    }

    vi.mocked(Issues.postApiIssues).mockResolvedValueOnce({
      data: mockIssue,
      request: {} as Request,
      response: {} as Response,
    })

    const { result } = renderHook(
      () => useCreateIssue({ projectId: 'proj-1' }),
      { wrapper: createWrapper() }
    )

    const createdIssue = await result.current.createIssue({
      title: 'Test Issue',
    })

    expect(createdIssue).toEqual(mockIssue)
  })

  it('calls onSuccess callback when provided', async () => {
    const mockIssue: IssueResponse = {
      id: 'abc123',
      title: 'Test Issue',
      type: IssueType[0],
    }

    vi.mocked(Issues.postApiIssues).mockResolvedValueOnce({
      data: mockIssue,
      request: {} as Request,
      response: {} as Response,
    })

    const onSuccess = vi.fn()
    const { result } = renderHook(
      () => useCreateIssue({ projectId: 'proj-1', onSuccess }),
      { wrapper: createWrapper() }
    )

    await result.current.createIssue({
      title: 'Test Issue',
    })

    await waitFor(() => {
      expect(onSuccess).toHaveBeenCalledWith(mockIssue)
    })
  })

  it('sets isCreating to true during mutation', async () => {
    let resolvePromise: (value: unknown) => void = () => {}
    const pendingPromise = new Promise((resolve) => {
      resolvePromise = resolve
    })

    vi.mocked(Issues.postApiIssues).mockReturnValueOnce(pendingPromise as never)

    const { result } = renderHook(
      () => useCreateIssue({ projectId: 'proj-1' }),
      { wrapper: createWrapper() }
    )

    expect(result.current.isCreating).toBe(false)

    const createPromise = result.current.createIssue({ title: 'Test' })

    await waitFor(() => {
      expect(result.current.isCreating).toBe(true)
    })

    resolvePromise({
      data: { id: 'abc123', title: 'Test', type: IssueType[0] } as IssueResponse,
      request: {} as Request,
      response: {} as Response,
    })

    await createPromise

    await waitFor(() => {
      expect(result.current.isCreating).toBe(false)
    })
  })

  it('handles API errors gracefully', async () => {
    vi.mocked(Issues.postApiIssues).mockRejectedValueOnce(new Error('Network error'))

    const { result } = renderHook(
      () => useCreateIssue({ projectId: 'proj-1' }),
      { wrapper: createWrapper() }
    )

    await expect(result.current.createIssue({ title: 'Test' })).rejects.toThrow('Network error')
  })

  it('supports different issue types', async () => {
    const mockIssue: IssueResponse = {
      id: 'abc123',
      title: 'Bug Fix',
      type: IssueType[1], // Bug
    }

    vi.mocked(Issues.postApiIssues).mockResolvedValueOnce({
      data: mockIssue,
      request: {} as Request,
      response: {} as Response,
    })

    const { result } = renderHook(
      () => useCreateIssue({ projectId: 'proj-1' }),
      { wrapper: createWrapper() }
    )

    await result.current.createIssue({
      title: 'Bug Fix',
      type: IssueType[1],
    })

    await waitFor(() => {
      expect(vi.mocked(Issues.postApiIssues)).toHaveBeenCalledWith({
        body: expect.objectContaining({
          type: 1,
        }),
      })
    })
  })
})
