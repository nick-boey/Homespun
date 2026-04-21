import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createElement } from 'react'
import { useProjectAssignees } from './use-project-assignees'

// Mock the API
vi.mock('@/api', () => ({
  Issues: {
    getApiProjectsByProjectIdIssuesAssignees: vi.fn(),
  },
}))

import { Issues } from '@/api'

function createWrapper() {
  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        retry: false,
      },
    },
  })
  return function Wrapper({ children }: { children: React.ReactNode }) {
    return createElement(QueryClientProvider, { client: queryClient }, children)
  }
}

describe('useProjectAssignees', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('fetches assignees for project', async () => {
    const mockAssignees = ['alice@example.com', 'bob@example.com']
    vi.mocked(Issues.getApiProjectsByProjectIdIssuesAssignees).mockResolvedValue({
      data: { assignees: mockAssignees },
      error: undefined,
    } as never)

    const { result } = renderHook(() => useProjectAssignees('test-project'), {
      wrapper: createWrapper(),
    })

    // Initially loading
    expect(result.current.isLoading).toBe(true)

    // Wait for the query to complete
    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    // Verify assignees
    expect(result.current.assignees).toEqual(mockAssignees)
    expect(result.current.isError).toBe(false)
    expect(Issues.getApiProjectsByProjectIdIssuesAssignees).toHaveBeenCalledWith({
      path: { projectId: 'test-project' },
    })
  })

  it('returns empty array when no assignees', async () => {
    vi.mocked(Issues.getApiProjectsByProjectIdIssuesAssignees).mockResolvedValue({
      data: { assignees: [] },
      error: undefined,
    } as never)

    const { result } = renderHook(() => useProjectAssignees('test-project'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isLoading).toBe(false)
    })

    expect(result.current.assignees).toEqual([])
  })

  it('handles API errors', async () => {
    vi.mocked(Issues.getApiProjectsByProjectIdIssuesAssignees).mockResolvedValue({
      data: undefined,
      error: { detail: 'Project not found' },
    } as never)

    const { result } = renderHook(() => useProjectAssignees('test-project'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.isError).toBe(true)
    })

    expect(result.current.error?.message).toBe('Project not found')
  })

  it('does not fetch when projectId is empty', async () => {
    const { result } = renderHook(() => useProjectAssignees(''), {
      wrapper: createWrapper(),
    })

    // Should not be loading since the query is disabled
    expect(result.current.isLoading).toBe(false)
    expect(result.current.assignees).toEqual([])
    expect(Issues.getApiProjectsByProjectIdIssuesAssignees).not.toHaveBeenCalled()
  })
})
