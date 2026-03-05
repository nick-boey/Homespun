import { describe, it, expect, vi } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { useIssueByEntityId } from './use-issue-by-entity-id'
import * as issueHooks from '@/features/issues/hooks/use-issue'
import type { ReactNode } from 'react'

// Mock the useIssue hook
vi.mock('@/features/issues/hooks/use-issue')

describe('useIssueByEntityId', () => {
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

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('parses issue entityId format correctly', async () => {
    const mockIssue = { id: 'abc123', title: 'Test Issue' }
    vi.mocked(issueHooks.useIssue).mockReturnValue({
      issue: mockIssue,
      isLoading: false,
      error: null,
      isSuccess: true,
      isError: false,
      refetch: vi.fn(),
    })

    const { result } = renderHook(() => useIssueByEntityId('issue:abc123', 'project-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.issue).toEqual(mockIssue)
      expect(result.current.isLoading).toBe(false)
      expect(result.current.error).toBeNull()
    })

    // Verify useIssue was called with correct issueId
    expect(issueHooks.useIssue).toHaveBeenCalledWith('abc123', 'project-1')
  })

  it('handles feature entityId format', async () => {
    const mockIssue = { id: 'def456', title: 'Test Feature' }
    vi.mocked(issueHooks.useIssue).mockReturnValue({
      issue: mockIssue,
      isLoading: false,
      error: null,
      isSuccess: true,
      isError: false,
      refetch: vi.fn(),
    })

    const { result } = renderHook(() => useIssueByEntityId('feature:def456', 'project-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.issue).toEqual(mockIssue)
    })

    expect(issueHooks.useIssue).toHaveBeenCalledWith('def456', 'project-1')
  })

  it('handles project entityId format', async () => {
    // Mock useIssue to return undefined when called with empty issueId
    vi.mocked(issueHooks.useIssue).mockReturnValue({
      issue: undefined,
      isLoading: false,
      error: null,
      isSuccess: false,
      isError: false,
      refetch: vi.fn(),
    })

    const { result } = renderHook(() => useIssueByEntityId('project:proj123', 'project-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.issue).toBeUndefined()
      expect(result.current.isLoading).toBe(false)
      expect(result.current.error).toBeNull()
    })

    // Should not call useIssue for project entities
    expect(issueHooks.useIssue).toHaveBeenCalledWith('', 'project-1')
  })

  it('handles invalid entityId format', async () => {
    vi.mocked(issueHooks.useIssue).mockReturnValue({
      issue: undefined,
      isLoading: false,
      error: null,
      isSuccess: false,
      isError: false,
      refetch: vi.fn(),
    })

    const { result } = renderHook(() => useIssueByEntityId('invalid-format', 'project-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.issue).toBeUndefined()
      expect(result.current.isLoading).toBe(false)
      expect(result.current.error).toBeNull()
    })

    expect(issueHooks.useIssue).toHaveBeenCalledWith('', 'project-1')
  })

  it('handles empty entityId', async () => {
    vi.mocked(issueHooks.useIssue).mockReturnValue({
      issue: undefined,
      isLoading: false,
      error: null,
      isSuccess: false,
      isError: false,
      refetch: vi.fn(),
    })

    const { result } = renderHook(() => useIssueByEntityId('', 'project-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.issue).toBeUndefined()
      expect(result.current.isLoading).toBe(false)
      expect(result.current.error).toBeNull()
    })

    expect(issueHooks.useIssue).toHaveBeenCalledWith('', 'project-1')
  })

  it('returns loading state from useIssue', async () => {
    vi.mocked(issueHooks.useIssue).mockReturnValue({
      issue: undefined,
      isLoading: true,
      error: null,
      isSuccess: false,
      isError: false,
      refetch: vi.fn(),
    })

    const { result } = renderHook(() => useIssueByEntityId('issue:abc123', 'project-1'), {
      wrapper: createWrapper(),
    })

    expect(result.current.isLoading).toBe(true)
    expect(result.current.issue).toBeUndefined()
  })

  it('returns error state from useIssue', async () => {
    const mockError = new Error('Failed to fetch issue')
    vi.mocked(issueHooks.useIssue).mockReturnValue({
      issue: undefined,
      isLoading: false,
      error: mockError,
      isSuccess: false,
      isError: true,
      refetch: vi.fn(),
    })

    const { result } = renderHook(() => useIssueByEntityId('issue:abc123', 'project-1'), {
      wrapper: createWrapper(),
    })

    expect(result.current.error).toEqual(mockError)
    expect(result.current.issue).toBeUndefined()
    expect(result.current.isLoading).toBe(false)
  })

  it('handles entityId with special characters', async () => {
    const mockIssue = { id: 'abc-123_xyz', title: 'Test Issue' }
    vi.mocked(issueHooks.useIssue).mockReturnValue({
      issue: mockIssue,
      isLoading: false,
      error: null,
      isSuccess: true,
      isError: false,
      refetch: vi.fn(),
    })

    const { result } = renderHook(() => useIssueByEntityId('issue:abc-123_xyz', 'project-1'), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.issue).toEqual(mockIssue)
    })

    expect(issueHooks.useIssue).toHaveBeenCalledWith('abc-123_xyz', 'project-1')
  })

  it('does not call useIssue when projectId is empty', async () => {
    vi.mocked(issueHooks.useIssue).mockReturnValue({
      issue: undefined,
      isLoading: false,
      error: null,
      isSuccess: false,
      isError: false,
      refetch: vi.fn(),
    })

    const { result } = renderHook(() => useIssueByEntityId('issue:abc123', ''), {
      wrapper: createWrapper(),
    })

    await waitFor(() => {
      expect(result.current.issue).toBeUndefined()
      expect(result.current.isLoading).toBe(false)
    })

    expect(issueHooks.useIssue).toHaveBeenCalledWith('abc123', '')
  })
})
