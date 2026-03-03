import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import React from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useIssueHistory } from './use-issue-history'
import { Issues } from '@/api'

vi.mock('@/api', () => ({
  Issues: {
    getApiProjectsByProjectIdIssuesHistoryState: vi.fn(),
    postApiProjectsByProjectIdIssuesHistoryUndo: vi.fn(),
    postApiProjectsByProjectIdIssuesHistoryRedo: vi.fn(),
  },
}))

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
      mutations: { retry: false },
    },
  })
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return React.createElement(QueryClientProvider, { client: queryClient }, children)
  }
}

describe('useIssueHistory', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('returns loading state initially', () => {
    vi.mocked(Issues.getApiProjectsByProjectIdIssuesHistoryState).mockReturnValue(
      new Promise(() => {}) as ReturnType<typeof Issues.getApiProjectsByProjectIdIssuesHistoryState>
    )

    const { result } = renderHook(() => useIssueHistory('test-project'), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(true)
  })

  it('returns history state when loaded', async () => {
    const mockState = {
      canUndo: true,
      canRedo: false,
      undoDescription: 'Create issue "Test"',
      redoDescription: null,
      totalEntries: 5,
      currentTimestamp: '2024-01-01T00:00:00Z',
    }

    vi.mocked(Issues.getApiProjectsByProjectIdIssuesHistoryState).mockResolvedValue({
      data: mockState,
      error: undefined,
    } as Awaited<ReturnType<typeof Issues.getApiProjectsByProjectIdIssuesHistoryState>>)

    const { result } = renderHook(() => useIssueHistory('test-project'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(result.current.historyState).toEqual(mockState)
    expect(result.current.canUndo).toBe(true)
    expect(result.current.canRedo).toBe(false)
    expect(result.current.undoDescription).toBe('Create issue "Test"')
    expect(result.current.redoDescription).toBeNull()
  })

  it('returns canUndo and canRedo as false when history state is not loaded', async () => {
    vi.mocked(Issues.getApiProjectsByProjectIdIssuesHistoryState).mockResolvedValue({
      data: undefined,
      error: undefined,
    } as unknown as Awaited<ReturnType<typeof Issues.getApiProjectsByProjectIdIssuesHistoryState>>)

    const { result } = renderHook(() => useIssueHistory('test-project'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    expect(result.current.canUndo).toBe(false)
    expect(result.current.canRedo).toBe(false)
  })

  it('calls undo API when undo is invoked', async () => {
    const mockState = {
      canUndo: true,
      canRedo: false,
      undoDescription: 'Create issue',
      redoDescription: null,
    }

    const mockUndoResponse = {
      success: true,
      state: {
        canUndo: false,
        canRedo: true,
        undoDescription: null,
        redoDescription: 'Create issue',
      },
    }

    vi.mocked(Issues.getApiProjectsByProjectIdIssuesHistoryState).mockResolvedValue({
      data: mockState,
      error: undefined,
    } as Awaited<ReturnType<typeof Issues.getApiProjectsByProjectIdIssuesHistoryState>>)

    vi.mocked(Issues.postApiProjectsByProjectIdIssuesHistoryUndo).mockResolvedValue({
      data: mockUndoResponse,
      error: undefined,
    } as Awaited<ReturnType<typeof Issues.postApiProjectsByProjectIdIssuesHistoryUndo>>)

    const { result } = renderHook(() => useIssueHistory('test-project'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    result.current.undo()

    await waitFor(() => {
      expect(Issues.postApiProjectsByProjectIdIssuesHistoryUndo).toHaveBeenCalledWith({
        path: { projectId: 'test-project' },
      })
    })
  })

  it('calls redo API when redo is invoked', async () => {
    const mockState = {
      canUndo: false,
      canRedo: true,
      undoDescription: null,
      redoDescription: 'Create issue',
    }

    const mockRedoResponse = {
      success: true,
      state: {
        canUndo: true,
        canRedo: false,
        undoDescription: 'Create issue',
        redoDescription: null,
      },
    }

    vi.mocked(Issues.getApiProjectsByProjectIdIssuesHistoryState).mockResolvedValue({
      data: mockState,
      error: undefined,
    } as Awaited<ReturnType<typeof Issues.getApiProjectsByProjectIdIssuesHistoryState>>)

    vi.mocked(Issues.postApiProjectsByProjectIdIssuesHistoryRedo).mockResolvedValue({
      data: mockRedoResponse,
      error: undefined,
    } as Awaited<ReturnType<typeof Issues.postApiProjectsByProjectIdIssuesHistoryRedo>>)

    const { result } = renderHook(() => useIssueHistory('test-project'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => expect(result.current.isLoading).toBe(false))

    result.current.redo()

    await waitFor(() => {
      expect(Issues.postApiProjectsByProjectIdIssuesHistoryRedo).toHaveBeenCalledWith({
        path: { projectId: 'test-project' },
      })
    })
  })

  it('does not fetch when projectId is empty', () => {
    const { result } = renderHook(() => useIssueHistory(''), {
      wrapper: createWrapper(),
    })

    expect(Issues.getApiProjectsByProjectIdIssuesHistoryState).not.toHaveBeenCalled()
    expect(result.current.isLoading).toBe(false)
  })
})
