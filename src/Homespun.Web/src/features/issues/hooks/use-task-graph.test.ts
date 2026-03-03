import { describe, it, expect, vi, beforeEach, type Mock } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement, type ReactNode } from 'react'
import { useTaskGraph, taskGraphQueryKey } from './use-task-graph'
import { Graph } from '@/api'
import type { TaskGraphResponse } from '@/api'

vi.mock('@/api', () => ({
  Graph: {
    getApiGraphByProjectIdTaskgraphData: vi.fn(),
  },
}))

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false },
    },
  })
  return ({ children }: { children: ReactNode }) =>
    createElement(QueryClientProvider, { client: queryClient }, children)
}

const mockTaskGraphResponse: TaskGraphResponse = {
  nodes: [
    {
      issue: {
        id: 'abc123',
        title: 'Test Issue',
        description: 'Test description',
        status: 0, // Open
        type: 0, // Task
        parentIssues: [],
        executionMode: 0, // Parallel
      },
      lane: 0,
      row: 0,
      isActionable: true,
    },
  ],
  totalLanes: 1,
  mergedPrs: [],
  hasMorePastPrs: false,
  totalPastPrsShown: 0,
  agentStatuses: {},
  linkedPrs: {},
}

describe('useTaskGraph', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('returns query key with project id', () => {
    expect(taskGraphQueryKey('project-123')).toEqual(['taskGraph', 'project-123'])
  })

  it('fetches task graph data successfully', async () => {
    const mockGetTaskGraph = Graph.getApiGraphByProjectIdTaskgraphData as Mock
    mockGetTaskGraph.mockResolvedValueOnce({ data: mockTaskGraphResponse })

    const { result } = renderHook(() => useTaskGraph('project-123'), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(true)

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(result.current.taskGraph).toEqual(mockTaskGraphResponse)
    expect(mockGetTaskGraph).toHaveBeenCalledWith({
      path: { projectId: 'project-123' },
      query: { maxPastPRs: 5 },
    })
  })

  it('handles API errors', async () => {
    const mockGetTaskGraph = Graph.getApiGraphByProjectIdTaskgraphData as Mock
    mockGetTaskGraph.mockResolvedValueOnce({
      error: { detail: 'Project not found' },
    })

    const { result } = renderHook(() => useTaskGraph('invalid-project'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })

    expect(result.current.error).toBeDefined()
  })

  it('does not fetch when projectId is empty', async () => {
    const mockGetTaskGraph = Graph.getApiGraphByProjectIdTaskgraphData as Mock

    const { result } = renderHook(() => useTaskGraph(''), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(mockGetTaskGraph).not.toHaveBeenCalled()
  })

  it('allows custom maxPastPRs parameter', async () => {
    const mockGetTaskGraph = Graph.getApiGraphByProjectIdTaskgraphData as Mock
    mockGetTaskGraph.mockResolvedValueOnce({ data: mockTaskGraphResponse })

    renderHook(() => useTaskGraph('project-123', { maxPastPRs: 10 }), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(mockGetTaskGraph).toHaveBeenCalledWith({
        path: { projectId: 'project-123' },
        query: { maxPastPRs: 10 },
      })
    })
  })

  it('provides refetch function', async () => {
    const mockGetTaskGraph = Graph.getApiGraphByProjectIdTaskgraphData as Mock
    mockGetTaskGraph.mockResolvedValue({ data: mockTaskGraphResponse })

    const { result } = renderHook(() => useTaskGraph('project-123'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isSuccess).toBe(true)
    })

    expect(typeof result.current.refetch).toBe('function')
  })
})
