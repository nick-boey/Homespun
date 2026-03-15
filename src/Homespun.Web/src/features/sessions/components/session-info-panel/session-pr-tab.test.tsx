import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { SessionPrTab } from './session-pr-tab'
import { useIssuePrStatus } from '@/features/sessions/hooks'
import type { IssuePullRequestStatus } from '@/api/generated'
import { PullRequestStatus } from '@/api/generated'
import { createMockSession } from '@/test/test-utils'

vi.mock('@/features/sessions/hooks', () => ({
  useIssuePrStatus: vi.fn(),
}))

describe('SessionPrTab', () => {
  const mockSession = createMockSession({
    entityId: 'clone:issue-123',
  })

  const nonCloneSession = createMockSession({
    entityId: 'issue-456',
  })

  it('shows empty state for non-clone entities', () => {
    vi.mocked(useIssuePrStatus).mockReturnValue({
      data: undefined,
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof useIssuePrStatus>)

    render(<SessionPrTab session={nonCloneSession} />)

    expect(screen.getByText('No PR available for this session')).toBeInTheDocument()
  })

  it('shows loading state', () => {
    vi.mocked(useIssuePrStatus).mockReturnValue({
      data: undefined,
      isLoading: true,
      isError: false,
    } as unknown as ReturnType<typeof useIssuePrStatus>)

    const { container } = render(<SessionPrTab session={mockSession} />)

    expect(container.querySelectorAll('[data-slot="skeleton"]').length).toBeGreaterThan(0)
  })

  it('shows error state', () => {
    vi.mocked(useIssuePrStatus).mockReturnValue({
      data: undefined,
      isLoading: false,
      isError: true,
    } as unknown as ReturnType<typeof useIssuePrStatus>)

    render(<SessionPrTab session={mockSession} />)

    expect(screen.getByText('Failed to load PR details')).toBeInTheDocument()
  })

  it('shows no PR created state', () => {
    const prStatus: IssuePullRequestStatus = {
      prUrl: null,
      branchName: 'feature/test-branch',
    }

    vi.mocked(useIssuePrStatus).mockReturnValue({
      data: prStatus,
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof useIssuePrStatus>)

    render(<SessionPrTab session={mockSession} />)

    expect(screen.getByText('No pull request created yet')).toBeInTheDocument()
    expect(screen.getByText('Branch: feature/test-branch')).toBeInTheDocument()
  })

  it('shows open PR with ready to merge status', () => {
    const prStatus: IssuePullRequestStatus = {
      prNumber: 42,
      prUrl: 'https://github.com/test/repo/pull/42',
      branchName: 'feature/test-branch',
      status: PullRequestStatus.READY_FOR_REVIEW,
      checksPassing: true,
      isApproved: true,
      approvalCount: 2,
      changesRequestedCount: 0,
      isMergeable: true,
      checksRunning: false,
      checksFailing: false,
      hasConflicts: false,
    }

    vi.mocked(useIssuePrStatus).mockReturnValue({
      data: prStatus,
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof useIssuePrStatus>)

    render(<SessionPrTab session={mockSession} />)

    expect(screen.getByText('#42')).toBeInTheDocument()
    expect(screen.getByText('Ready to Merge')).toBeInTheDocument()
    expect(screen.getByText('View on GitHub')).toBeInTheDocument()
    expect(screen.getByText('feature/test-branch')).toBeInTheDocument()
    expect(screen.getByText('Passing')).toBeInTheDocument()
    expect(screen.getByText('Approved (2)')).toBeInTheDocument()
  })

  it('shows PR with conflicts', () => {
    const prStatus: IssuePullRequestStatus = {
      prNumber: 43,
      prUrl: 'https://github.com/test/repo/pull/43',
      branchName: 'feature/conflicts',
      status: PullRequestStatus.CONFLICT,
      hasConflicts: true,
      checksPassing: false,
      isApproved: false,
      approvalCount: 0,
      changesRequestedCount: 1,
    }

    vi.mocked(useIssuePrStatus).mockReturnValue({
      data: prStatus,
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof useIssuePrStatus>)

    render(<SessionPrTab session={mockSession} />)

    expect(screen.getByText('Conflicts')).toBeInTheDocument()
    expect(screen.getByText('This PR has merge conflicts')).toBeInTheDocument()
    expect(screen.getByText('(1 changes requested)')).toBeInTheDocument()
  })

  it('shows PR with running checks', () => {
    const prStatus: IssuePullRequestStatus = {
      prNumber: 44,
      prUrl: 'https://github.com/test/repo/pull/44',
      branchName: 'feature/running-checks',
      status: PullRequestStatus.IN_PROGRESS,
      checksRunning: true,
      checksPassing: false,
      checksFailing: false,
    }

    vi.mocked(useIssuePrStatus).mockReturnValue({
      data: prStatus,
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof useIssuePrStatus>)

    render(<SessionPrTab session={mockSession} />)

    expect(screen.getByText('Checks Running')).toBeInTheDocument()
    expect(screen.getByText('Running')).toBeInTheDocument()
  })

  it('shows merged PR', () => {
    const prStatus: IssuePullRequestStatus = {
      prNumber: 45,
      prUrl: 'https://github.com/test/repo/pull/45',
      branchName: 'feature/merged',
      status: PullRequestStatus.MERGED,
    }

    vi.mocked(useIssuePrStatus).mockReturnValue({
      data: prStatus,
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof useIssuePrStatus>)

    render(<SessionPrTab session={mockSession} />)

    expect(screen.getByText('Merged')).toBeInTheDocument()
  })

  it('shows closed PR', () => {
    const prStatus: IssuePullRequestStatus = {
      prNumber: 46,
      prUrl: 'https://github.com/test/repo/pull/46',
      branchName: 'feature/closed',
      status: PullRequestStatus.CLOSED,
    }

    vi.mocked(useIssuePrStatus).mockReturnValue({
      data: prStatus,
      isLoading: false,
      isError: false,
    } as unknown as ReturnType<typeof useIssuePrStatus>)

    render(<SessionPrTab session={mockSession} />)

    expect(screen.getByText('Closed')).toBeInTheDocument()
  })
})
