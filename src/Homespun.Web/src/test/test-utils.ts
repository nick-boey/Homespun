import { vi } from 'vitest'
import type { UseQueryResult } from '@tanstack/react-query'
import type { ClaudeSession } from '@/api/generated'
import { SessionMode } from '@/api/generated'

// Helper type for mocking React Query hooks
export type MockQueryResult<TData> = Partial<UseQueryResult<TData, Error>>

// Helper to create a mock ClaudeSession with required fields
export function createMockSession(overrides: Partial<ClaudeSession> = {}): ClaudeSession {
  return {
    id: 'session-1',
    entityId: 'issue-123',
    projectId: 'project-1',
    workingDirectory: '/path/to/project',
    model: 'opus',
    mode: SessionMode[0], // Plan mode
    ...overrides,
  }
}

// Helper function to create properly typed mock query results
export function createMockQueryResult<TData>(
  overrides: MockQueryResult<TData>
): UseQueryResult<TData, Error> {
  return {
    data: undefined,
    error: null,
    isError: false,
    isLoading: false,
    isLoadingError: false,
    isRefetchError: false,
    isSuccess: false,
    status: 'idle',
    dataUpdatedAt: 0,
    errorUpdatedAt: 0,
    failureCount: 0,
    failureReason: null,
    errorUpdateCount: 0,
    isFetched: false,
    isFetchedAfterMount: false,
    isFetching: false,
    isPaused: false,
    isPlaceholderData: false,
    isRefetching: false,
    isStale: false,
    refetch: vi.fn(),
    ...overrides,
  } as UseQueryResult<TData, Error>
}
