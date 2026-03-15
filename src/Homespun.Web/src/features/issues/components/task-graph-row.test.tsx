import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { TaskGraphIssueRow } from './task-graph-row'
import { TaskGraphMarkerType } from '../services'
import type { TaskGraphIssueRenderLine } from '../services'
import type { UseQueryResult } from '@tanstack/react-query'
import type { IssuePullRequestStatus } from '@/api'
import { IssueType, IssueStatus, ExecutionMode } from '@/api'
import * as prStatusHook from '../hooks/use-linked-pr-status'

// Mock the hooks
vi.mock('../hooks/use-linked-pr-status')

// Helper to create a mock query result
function createMockQueryResult<T>(
  data: T | null,
  isLoading = false,
  error: Error | null = null
): UseQueryResult<T, Error> {
  return {
    data,
    error,
    isLoading,
    isError: !!error,
    isSuccess: !error && !isLoading,
    isPending: false,
    isStale: false,
    isFetching: isLoading,
    isRefetching: false,
    isRefetchError: false,
    isLoadingError: false,
    isInitialLoading: isLoading,
    isPaused: false,
    isEnabled: true,
    status: isLoading ? 'pending' : error ? 'error' : 'success',
    fetchStatus: isLoading ? 'fetching' : 'idle',
    refetch: vi.fn(),
    remove: vi.fn(),
    failureCount: 0,
    failureReason: error,
    errorUpdateCount: 0,
    isPlaceholderData: false,
    isPreviousData: false,
    isFetched: !isLoading,
    isFetchedAfterMount: !isLoading,
    errorUpdatedAt: 0,
    dataUpdatedAt: 0,
    promise: Promise.resolve(data),
  } as UseQueryResult<T, Error>
}

describe('TaskGraphIssueRow', () => {
  const mockLine: TaskGraphIssueRenderLine = {
    type: 'issue',
    issueId: 'test-123',
    title: 'Test Issue',
    description: 'Test description',
    branchName: 'test-branch',
    lane: 1,
    marker: TaskGraphMarkerType.Open,
    parentLane: null,
    isFirstChild: false,
    isSeriesChild: false,
    drawTopLine: true,
    drawBottomLine: true,
    seriesConnectorFromLane: null,
    issueType: IssueType.BUG,
    status: IssueStatus.OPEN,
    hasDescription: true,
    linkedPr: null,
    agentStatus: null,
    assignedTo: null,
    drawLane0Connector: false,
    isLastLane0Connector: false,
    drawLane0PassThrough: false,
    lane0Color: null,
    hasHiddenParent: false,
    hiddenParentIsSeriesMode: false,
    executionMode: ExecutionMode.SERIES,
  }

  const defaultProps = {
    line: mockLine,
    maxLanes: 3,
    projectId: 'proj-123',
  }

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders issue title and type', () => {
    vi.spyOn(prStatusHook, 'useLinkedPrStatus').mockReturnValue(
      createMockQueryResult<IssuePullRequestStatus | null>(null)
    )

    render(<TaskGraphIssueRow {...defaultProps} />)

    expect(screen.getByText('Test Issue')).toBeInTheDocument()
    expect(screen.getByText('Bug')).toBeInTheDocument() // Issue type 1 = Bug
  })

  describe('PR status indicator', () => {
    it('does not show PR status when no linked PR', () => {
      vi.spyOn(prStatusHook, 'useLinkedPrStatus').mockReturnValue(
        createMockQueryResult<IssuePullRequestStatus | null>(null)
      )

      render(<TaskGraphIssueRow {...defaultProps} />)

      expect(screen.queryByLabelText(/merge conflicts/i)).not.toBeInTheDocument()
      expect(screen.queryByLabelText(/tests/i)).not.toBeInTheDocument()
    })

    it('shows PR link without status indicator when PR status is loading', () => {
      const lineWithPr = {
        ...mockLine,
        linkedPr: {
          number: 123,
          url: 'https://github.com/test/test/pull/123',
          status: 'Open',
        },
      }

      vi.spyOn(prStatusHook, 'useLinkedPrStatus').mockReturnValue(
        createMockQueryResult<IssuePullRequestStatus | null>(null, true)
      )

      render(<TaskGraphIssueRow {...defaultProps} line={lineWithPr} />)

      expect(screen.getByText('#123')).toBeInTheDocument()
      expect(screen.queryByLabelText(/merge conflicts/i)).not.toBeInTheDocument()
      expect(screen.queryByLabelText(/tests/i)).not.toBeInTheDocument()
    })

    it('shows PR status indicators when PR has status data', () => {
      const lineWithPr = {
        ...mockLine,
        linkedPr: {
          number: 456,
          url: 'https://github.com/test/test/pull/456',
          status: 'Open',
        },
      }

      vi.spyOn(prStatusHook, 'useLinkedPrStatus').mockReturnValue(
        createMockQueryResult<IssuePullRequestStatus | null>({
          prNumber: 456,
          prUrl: 'https://github.com/test/test/pull/456',
          checksPassing: true,
          mergeableState: 'clean',
          isMergeableByGitHub: true,
          hasConflicts: false,
        })
      )

      render(<TaskGraphIssueRow {...defaultProps} line={lineWithPr} />)

      expect(screen.getByText('#456')).toBeInTheDocument()
      expect(screen.getByLabelText('No merge conflicts')).toBeInTheDocument()
      expect(screen.getByLabelText('Tests passing')).toBeInTheDocument()
    })

    it('shows conflict indicator when PR has merge conflicts', () => {
      const lineWithPr = {
        ...mockLine,
        linkedPr: {
          number: 789,
          url: 'https://github.com/test/test/pull/789',
          status: 'Open',
        },
      }

      vi.spyOn(prStatusHook, 'useLinkedPrStatus').mockReturnValue(
        createMockQueryResult<IssuePullRequestStatus | null>({
          prNumber: 789,
          prUrl: 'https://github.com/test/test/pull/789',
          checksPassing: true,
          mergeableState: 'dirty', // GitHub API returns 'dirty' for merge conflicts
          isMergeableByGitHub: false,
          hasConflicts: true, // Use the computed property from backend
        })
      )

      render(<TaskGraphIssueRow {...defaultProps} line={lineWithPr} />)

      expect(screen.getByLabelText('Has merge conflicts')).toBeInTheDocument()
    })

    it('shows failing tests indicator when checks are failing', () => {
      const lineWithPr = {
        ...mockLine,
        linkedPr: {
          number: 999,
          url: 'https://github.com/test/test/pull/999',
          status: 'Open',
        },
      }

      vi.spyOn(prStatusHook, 'useLinkedPrStatus').mockReturnValue(
        createMockQueryResult<IssuePullRequestStatus | null>({
          prNumber: 999,
          prUrl: 'https://github.com/test/test/pull/999',
          checksPassing: false,
          mergeableState: 'clean',
          isMergeableByGitHub: true,
          hasConflicts: false,
        })
      )

      render(<TaskGraphIssueRow {...defaultProps} line={lineWithPr} />)

      expect(screen.getByLabelText('Tests failing')).toBeInTheDocument()
    })

    it('shows running tests indicator when checks are null', () => {
      const lineWithPr = {
        ...mockLine,
        linkedPr: {
          number: 111,
          url: 'https://github.com/test/test/pull/111',
          status: 'Open',
        },
      }

      vi.spyOn(prStatusHook, 'useLinkedPrStatus').mockReturnValue(
        createMockQueryResult<IssuePullRequestStatus | null>({
          prNumber: 111,
          prUrl: 'https://github.com/test/test/pull/111',
          checksPassing: null,
          mergeableState: 'clean',
          isMergeableByGitHub: true,
          hasConflicts: false,
        })
      )

      render(<TaskGraphIssueRow {...defaultProps} line={lineWithPr} />)

      expect(screen.getByLabelText('Tests running')).toBeInTheDocument()
    })
  })

  it('calls useLinkedPrStatus with correct parameters', () => {
    const lineWithPr = {
      ...mockLine,
      linkedPr: {
        number: 222,
        url: 'https://github.com/test/test/pull/222',
        status: 'Open',
      },
    }

    const mockUseLinkedPrStatus = vi
      .spyOn(prStatusHook, 'useLinkedPrStatus')
      .mockReturnValue(createMockQueryResult(null))

    render(<TaskGraphIssueRow {...defaultProps} line={lineWithPr} />)

    expect(mockUseLinkedPrStatus).toHaveBeenCalledWith('proj-123', 'test-123', true)
  })

  it('does not call useLinkedPrStatus when no linked PR', () => {
    const mockUseLinkedPrStatus = vi
      .spyOn(prStatusHook, 'useLinkedPrStatus')
      .mockReturnValue(createMockQueryResult(null))

    render(<TaskGraphIssueRow {...defaultProps} />)

    expect(mockUseLinkedPrStatus).toHaveBeenCalledWith('proj-123', undefined, true)
  })

  describe('assignee badge', () => {
    it('does not show badge when assignedTo is null', () => {
      vi.spyOn(prStatusHook, 'useLinkedPrStatus').mockReturnValue(
        createMockQueryResult<IssuePullRequestStatus | null>(null)
      )

      render(<TaskGraphIssueRow {...defaultProps} />)

      expect(screen.queryByText('user')).not.toBeInTheDocument()
    })

    it('shows username portion of email in badge when assignedTo has email', () => {
      vi.spyOn(prStatusHook, 'useLinkedPrStatus').mockReturnValue(
        createMockQueryResult<IssuePullRequestStatus | null>(null)
      )

      const lineWithAssignee = {
        ...mockLine,
        assignedTo: 'testuser@example.com',
      }

      render(<TaskGraphIssueRow {...defaultProps} line={lineWithAssignee} />)

      expect(screen.getByText('testuser')).toBeInTheDocument()
      expect(screen.queryByText('testuser@example.com')).not.toBeInTheDocument()
    })

    it('shows full value when assignedTo has no @ symbol', () => {
      vi.spyOn(prStatusHook, 'useLinkedPrStatus').mockReturnValue(
        createMockQueryResult<IssuePullRequestStatus | null>(null)
      )

      const lineWithAssignee = {
        ...mockLine,
        assignedTo: 'plainusername',
      }

      render(<TaskGraphIssueRow {...defaultProps} line={lineWithAssignee} />)

      expect(screen.getByText('plainusername')).toBeInTheDocument()
    })
  })

  describe('execution mode toggle', () => {
    it('renders toggle button with correct mode for Series (executionMode=0)', () => {
      vi.spyOn(prStatusHook, 'useLinkedPrStatus').mockReturnValue(
        createMockQueryResult<IssuePullRequestStatus | null>(null)
      )

      const lineWithSeriesMode = {
        ...mockLine,
        executionMode: ExecutionMode.SERIES,
      }

      render(<TaskGraphIssueRow {...defaultProps} line={lineWithSeriesMode} />)

      const toggleButton = screen.getByRole('button', { name: 'Series execution mode' })
      expect(toggleButton).toBeInTheDocument()
    })

    it('renders toggle button with correct mode for Parallel', () => {
      vi.spyOn(prStatusHook, 'useLinkedPrStatus').mockReturnValue(
        createMockQueryResult<IssuePullRequestStatus | null>(null)
      )

      const lineWithParallelMode = {
        ...mockLine,
        executionMode: ExecutionMode.PARALLEL,
      }

      render(<TaskGraphIssueRow {...defaultProps} line={lineWithParallelMode} />)

      const toggleButton = screen.getByRole('button', { name: 'Parallel execution mode' })
      expect(toggleButton).toBeInTheDocument()
    })

    it('calls onExecutionModeChange with (issueId, newMode) when toggled from Series to Parallel', () => {
      vi.spyOn(prStatusHook, 'useLinkedPrStatus').mockReturnValue(
        createMockQueryResult<IssuePullRequestStatus | null>(null)
      )

      const onExecutionModeChange = vi.fn()
      const lineWithSeriesMode = {
        ...mockLine,
        executionMode: ExecutionMode.SERIES,
      }

      render(
        <TaskGraphIssueRow
          {...defaultProps}
          line={lineWithSeriesMode}
          onExecutionModeChange={onExecutionModeChange}
        />
      )

      const toggleButton = screen.getByRole('button', { name: 'Series execution mode' })
      fireEvent.click(toggleButton)

      expect(onExecutionModeChange).toHaveBeenCalledWith('test-123', ExecutionMode.PARALLEL)
    })

    it('calls onExecutionModeChange with (issueId, newMode) when toggled from Parallel to Series', () => {
      vi.spyOn(prStatusHook, 'useLinkedPrStatus').mockReturnValue(
        createMockQueryResult<IssuePullRequestStatus | null>(null)
      )

      const onExecutionModeChange = vi.fn()
      const lineWithParallelMode = {
        ...mockLine,
        executionMode: ExecutionMode.PARALLEL,
      }

      render(
        <TaskGraphIssueRow
          {...defaultProps}
          line={lineWithParallelMode}
          onExecutionModeChange={onExecutionModeChange}
        />
      )

      const toggleButton = screen.getByRole('button', { name: 'Parallel execution mode' })
      fireEvent.click(toggleButton)

      expect(onExecutionModeChange).toHaveBeenCalledWith('test-123', ExecutionMode.SERIES)
    })
  })
})
