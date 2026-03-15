import { describe, it, expect, vi, beforeEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useEnrichedSessions } from './use-enriched-sessions'
import { useSessions } from './use-sessions'
import { useProjects } from '@/features/projects'
import { Issues, PullRequests } from '@/api'
import React, { type ReactNode } from 'react'
import type { SessionSummary } from '@/api/generated/types.gen'
import { SessionMode, ClaudeSessionStatus } from '@/api/generated/types.gen'

// Mock dependencies
vi.mock('./use-sessions')
vi.mock('@/features/projects')
vi.mock('@/api', () => ({
  Issues: {
    getApiIssuesByIssueId: vi.fn(),
  },
  PullRequests: {
    getApiPullRequestsById: vi.fn(),
  },
}))

describe('useEnrichedSessions', () => {
  const wrapper = ({ children }: { children: ReactNode }) => {
    const queryClient = new QueryClient({
      defaultOptions: { queries: { retry: false } },
    })
    return React.createElement(QueryClientProvider, { client: queryClient }, children)
  }

  const mockSessions: SessionSummary[] = [
    {
      id: 'session-1',
      entityId: 'issue-123',
      projectId: 'project-1',
      model: 'claude-3.5-sonnet',
      mode: SessionMode.BUILD,
      status: ClaudeSessionStatus.RUNNING,
      createdAt: new Date().toISOString(),
      lastActivityAt: new Date().toISOString(),
      messageCount: 0,
      totalCostUsd: 0.05,
    },
    {
      id: 'session-2',
      entityId: 'pr-456',
      projectId: 'project-2',
      model: 'claude-3.5-sonnet',
      mode: SessionMode.PLAN,
      status: ClaudeSessionStatus.STOPPED,
      createdAt: new Date().toISOString(),
      lastActivityAt: new Date().toISOString(),
      messageCount: 0,
      totalCostUsd: 0.02,
    },
  ]

  const mockProjects = [
    { id: 'project-1', name: 'Project One' },
    { id: 'project-2', name: 'Project Two' },
  ]

  beforeEach(() => {
    vi.clearAllMocks()

    // Setup default mocks
    vi.mocked(useSessions).mockReturnValue({
      data: mockSessions,
      isLoading: false,
      isError: false,
      error: null,
      refetch: vi.fn(),
    } as unknown as ReturnType<typeof useSessions>)

    vi.mocked(useProjects).mockReturnValue({
      data: mockProjects,
      isLoading: false,
      isError: false,
      error: null,
      refetch: vi.fn(),
    } as unknown as ReturnType<typeof useProjects>)

    // Setup API mocks
    vi.mocked(Issues.getApiIssuesByIssueId).mockResolvedValue({
      data: { id: 'issue-123', title: 'Fix login bug' },
    } as Awaited<ReturnType<typeof Issues.getApiIssuesByIssueId>>)

    vi.mocked(PullRequests.getApiPullRequestsById).mockResolvedValue({
      data: { id: 'pr-456', title: 'Add new feature' },
    } as Awaited<ReturnType<typeof PullRequests.getApiPullRequestsById>>)
  })

  it('enriches sessions with entity and project information', async () => {
    const { result } = renderHook(() => useEnrichedSessions(), { wrapper })

    await waitFor(() => {
      expect(result.current.sessions).toHaveLength(2)
      expect(result.current.sessions[0].entityTitle).toBeDefined()
      expect(result.current.sessions[1].entityTitle).toBeDefined()
    })

    // Verify API calls include projectId for issues
    expect(Issues.getApiIssuesByIssueId).toHaveBeenCalledWith({
      path: { issueId: 'issue-123' },
      query: { projectId: 'project-1' },
    })

    // Verify API calls don't include query for PRs
    expect(PullRequests.getApiPullRequestsById).toHaveBeenCalledWith({
      path: { id: 'pr-456' },
    })

    expect(result.current.sessions[0]).toEqual({
      session: mockSessions[0],
      entityTitle: 'Fix login bug',
      entityType: 'issue',
      projectName: 'Project One',
      messageCount: 0,
    })

    expect(result.current.sessions[1]).toEqual({
      session: mockSessions[1],
      entityTitle: 'Add new feature',
      entityType: 'pr',
      projectName: 'Project Two',
      messageCount: 0,
    })
  })

  it('handles loading states correctly', () => {
    vi.mocked(useSessions).mockReturnValue({
      data: undefined,
      isLoading: true,
      isError: false,
      error: null,
      refetch: vi.fn(),
    } as unknown as ReturnType<typeof useSessions>)

    const { result } = renderHook(() => useEnrichedSessions(), { wrapper })

    expect(result.current.isLoading).toBe(true)
    expect(result.current.sessions).toEqual([])
  })

  it('handles error states correctly', () => {
    const error = new Error('Failed to load sessions')
    vi.mocked(useSessions).mockReturnValue({
      data: undefined,
      isLoading: false,
      isError: true,
      error,
      refetch: vi.fn(),
    } as unknown as ReturnType<typeof useSessions>)

    const { result } = renderHook(() => useEnrichedSessions(), { wrapper })

    expect(result.current.isError).toBe(true)
    expect(result.current.error).toBe(error)
    expect(result.current.sessions).toEqual([])
  })

  it('handles sessions without entity IDs', async () => {
    vi.mocked(useSessions).mockReturnValue({
      data: [{ ...mockSessions[0], entityId: null }],
      isLoading: false,
      isError: false,
      error: null,
      refetch: vi.fn(),
    } as unknown as ReturnType<typeof useSessions>)

    const { result } = renderHook(() => useEnrichedSessions(), { wrapper })

    await waitFor(() => {
      expect(result.current.sessions).toHaveLength(1)
    })

    expect(result.current.sessions[0]).toEqual({
      session: { ...mockSessions[0], entityId: null },
      entityTitle: undefined,
      entityType: undefined,
      projectName: 'Project One',
      messageCount: 0,
    })
  })

  it('handles sessions without project IDs', async () => {
    vi.mocked(useSessions).mockReturnValue({
      data: [{ ...mockSessions[0], projectId: null }],
      isLoading: false,
      isError: false,
      error: null,
      refetch: vi.fn(),
    } as unknown as ReturnType<typeof useSessions>)

    const { result } = renderHook(() => useEnrichedSessions(), { wrapper })

    await waitFor(() => {
      expect(result.current.sessions).toHaveLength(1)
      expect(result.current.sessions[0].entityTitle).toBeDefined()
    })

    // Verify API call is made without projectId query parameter
    expect(Issues.getApiIssuesByIssueId).toHaveBeenCalledWith({
      path: { issueId: 'issue-123' },
      query: undefined,
    })

    expect(result.current.sessions[0].entityTitle).toBe('Fix login bug')
    expect(result.current.sessions[0].entityType).toBe('issue')
    expect(result.current.sessions[0].projectName).toBeUndefined()
  })

  it('handles entity info API failures gracefully', async () => {
    vi.mocked(Issues.getApiIssuesByIssueId).mockRejectedValue(new Error('API Error'))

    vi.mocked(useSessions).mockReturnValue({
      data: [mockSessions[0]],
      isLoading: false,
      isError: false,
      error: null,
      refetch: vi.fn(),
    } as unknown as ReturnType<typeof useSessions>)

    const { result } = renderHook(() => useEnrichedSessions(), { wrapper })

    await waitFor(() => {
      expect(result.current.sessions).toHaveLength(1)
    })

    // Should still show session even if entity info fails
    expect(result.current.sessions[0].entityTitle).toBeUndefined()
    expect(result.current.sessions[0].entityType).toBeUndefined()
  })

  it('groups sessions by project correctly', async () => {
    vi.mocked(useSessions).mockReturnValue({
      data: [...mockSessions, { ...mockSessions[0], id: 'session-3', entityId: 'issue-789' }],
      isLoading: false,
      isError: false,
      error: null,
      refetch: vi.fn(),
    } as unknown as ReturnType<typeof useSessions>)

    vi.mocked(Issues.getApiIssuesByIssueId)
      .mockResolvedValueOnce({ data: { id: 'issue-123', title: 'Fix login bug' } } as Awaited<
        ReturnType<typeof Issues.getApiIssuesByIssueId>
      >)
      .mockResolvedValueOnce({ data: { id: 'issue-789', title: 'Another issue' } } as Awaited<
        ReturnType<typeof Issues.getApiIssuesByIssueId>
      >)

    const { result } = renderHook(() => useEnrichedSessions(), { wrapper })

    await waitFor(() => {
      expect(result.current.groupedByProject.size).toBe(2)
    })

    // Verify API calls include projectId for both issues
    expect(Issues.getApiIssuesByIssueId).toHaveBeenCalledWith({
      path: { issueId: 'issue-123' },
      query: { projectId: 'project-1' },
    })
    expect(Issues.getApiIssuesByIssueId).toHaveBeenCalledWith({
      path: { issueId: 'issue-789' },
      query: { projectId: 'project-1' },
    })

    expect(result.current.groupedByProject.get('project-1')).toHaveLength(2)
    expect(result.current.groupedByProject.get('project-2')).toHaveLength(1)
  })

  it('calculates message counts correctly', async () => {
    const sessionWithMessages = {
      ...mockSessions[0],
      messageCount: 3,
    }

    vi.mocked(useSessions).mockReturnValue({
      data: [sessionWithMessages],
      isLoading: false,
      isError: false,
      error: null,
      refetch: vi.fn(),
    } as unknown as ReturnType<typeof useSessions>)

    const { result } = renderHook(() => useEnrichedSessions(), { wrapper })

    await waitFor(() => {
      expect(result.current.sessions[0].messageCount).toBe(3)
    })
  })

  it('provides refetch function', () => {
    const mockRefetch = vi.fn()
    vi.mocked(useSessions).mockReturnValue({
      data: mockSessions,
      isLoading: false,
      isError: false,
      error: null,
      refetch: mockRefetch,
    } as unknown as ReturnType<typeof useSessions>)

    const { result } = renderHook(() => useEnrichedSessions(), { wrapper })

    result.current.refetch()
    expect(mockRefetch).toHaveBeenCalled()
  })
})
