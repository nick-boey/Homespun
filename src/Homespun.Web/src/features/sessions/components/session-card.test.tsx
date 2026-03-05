import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { SessionCard } from './session-card'
import type {
  SessionSummary,
  IssueResponse,
  IssueStatus,
  IssueType,
  ClaudeSessionStatus,
  SessionMode,
  IssuePullRequestStatus,
} from '@/api'
import { ISSUE_STATUS, ISSUE_TYPE } from '@/lib/issue-constants'
import * as issueHooks from '../hooks/use-issue-by-entity-id'
import * as prHooks from '../hooks/use-issue-pr-status'

// Mock the hooks
vi.mock('../hooks/use-issue-by-entity-id')
vi.mock('../hooks/use-issue-pr-status')
vi.mock('../hooks/use-sessions', () => ({
  useStopSession: () => ({
    mutate: vi.fn(),
    isPending: false,
  }),
}))

// Mock router
const mockNavigate = vi.fn()
vi.mock('@tanstack/react-router', () => ({
  useNavigate: () => mockNavigate,
}))

describe('SessionCard', () => {
  const mockSession: SessionSummary = {
    id: 'session-123',
    entityId: 'issue:abc123',
    projectId: 'project-456',
    mode: 1 as SessionMode, // Build
    model: 'claude-3.5-sonnet',
    status: 2 as ClaudeSessionStatus, // Running
    messageCount: 42,
    createdAt: new Date().toISOString(),
    lastActivityAt: new Date().toISOString(),
    totalCostUsd: 0,
    containerId: null,
    containerName: null,
  }

  const mockIssue: IssueResponse = {
    id: 'abc123',
    title: 'Fix authentication bug',
    type: ISSUE_TYPE.Bug as IssueType,
    status: ISSUE_STATUS.Progress as IssueStatus,
    description:
      'The authentication system is not working properly when users try to log in with OAuth providers.',
    priority: 1,
  }

  const mockPrStatus: IssuePullRequestStatus = {
    prNumber: 123,
    prUrl: 'https://github.com/example/repo/pull/123',
    status: 0, // Open
    branchName: null,
    checksPassing: null,
  }

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders skeleton while loading issue data', () => {
    vi.mocked(issueHooks.useIssueByEntityId).mockReturnValue({
      issue: undefined,
      isLoading: true,
      error: null,
    })
    vi.mocked(prHooks.useIssuePrStatus).mockReturnValue({
      prStatus: undefined,
      isLoading: true,
      error: null,
    })

    render(<SessionCard session={mockSession} />)

    // Should show skeleton elements
    expect(screen.getByTestId('session-card-skeleton')).toBeInTheDocument()
  })

  it('renders card with all issue data when loaded', async () => {
    vi.mocked(issueHooks.useIssueByEntityId).mockReturnValue({
      issue: mockIssue,
      isLoading: false,
      error: null,
    })
    vi.mocked(prHooks.useIssuePrStatus).mockReturnValue({
      prStatus: mockPrStatus,
      isLoading: false,
      error: null,
    })

    render(<SessionCard session={mockSession} />)

    await waitFor(() => {
      // Issue content
      expect(screen.getByText('Fix authentication bug')).toBeInTheDocument()
      expect(screen.getByText('bug')).toBeInTheDocument()
      expect(screen.getByText('In Progress')).toBeInTheDocument()
      expect(screen.getByText(/authentication system is not working/)).toBeInTheDocument()

      // Session info
      expect(screen.getByText('Build')).toBeInTheDocument()
      expect(screen.getByText(/claude-3.5-sonnet/)).toBeInTheDocument()
      expect(screen.getByText(/42 messages/)).toBeInTheDocument()

      // PR badge
      expect(screen.getByText('PR #123')).toBeInTheDocument()

      // Session status
      expect(screen.getByText('Running')).toBeInTheDocument()
    })
  })

  it('shows error message when issue fetch fails', async () => {
    vi.mocked(issueHooks.useIssueByEntityId).mockReturnValue({
      issue: undefined,
      isLoading: false,
      error: new Error('Failed to fetch issue'),
    })
    vi.mocked(prHooks.useIssuePrStatus).mockReturnValue({
      prStatus: undefined,
      isLoading: false,
      error: null,
    })

    render(<SessionCard session={mockSession} />)

    await waitFor(() => {
      expect(screen.getByText(/Failed to load issue details/)).toBeInTheDocument()
      // Should still show session info
      expect(screen.getByText('Build')).toBeInTheDocument()
      expect(screen.getByText(/42 messages/)).toBeInTheDocument()
    })
  })

  it('displays entity ID when issue data is unavailable', async () => {
    vi.mocked(issueHooks.useIssueByEntityId).mockReturnValue({
      issue: undefined,
      isLoading: false,
      error: null,
    })
    vi.mocked(prHooks.useIssuePrStatus).mockReturnValue({
      prStatus: undefined,
      isLoading: false,
      error: null,
    })

    render(<SessionCard session={mockSession} />)

    await waitFor(() => {
      expect(screen.getByText('issue:abc123')).toBeInTheDocument()
    })
  })

  it('shows stop button for active sessions', async () => {
    vi.mocked(issueHooks.useIssueByEntityId).mockReturnValue({
      issue: mockIssue,
      isLoading: false,
      error: null,
    })
    vi.mocked(prHooks.useIssuePrStatus).mockReturnValue({
      prStatus: undefined,
      isLoading: false,
      error: null,
    })

    render(<SessionCard session={mockSession} />)

    await waitFor(() => {
      const stopButton = screen.getByRole('button', { name: /stop session/i })
      expect(stopButton).toBeInTheDocument()
    })
  })

  it('hides stop button for stopped sessions', async () => {
    vi.mocked(issueHooks.useIssueByEntityId).mockReturnValue({
      issue: mockIssue,
      isLoading: false,
      error: null,
    })
    vi.mocked(prHooks.useIssuePrStatus).mockReturnValue({
      prStatus: undefined,
      isLoading: false,
      error: null,
    })

    const stoppedSession = { ...mockSession, status: 6 as ClaudeSessionStatus } // Stopped
    render(<SessionCard session={stoppedSession} />)

    await waitFor(() => {
      expect(screen.queryByRole('button', { name: /stop session/i })).not.toBeInTheDocument()
    })
  })

  it('does not show PR badge when no PR exists', async () => {
    vi.mocked(issueHooks.useIssueByEntityId).mockReturnValue({
      issue: mockIssue,
      isLoading: false,
      error: null,
    })
    vi.mocked(prHooks.useIssuePrStatus).mockReturnValue({
      prStatus: {
        prNumber: undefined,
        prUrl: null,
        status: undefined,
        branchName: null,
        checksPassing: null,
      } as IssuePullRequestStatus,
      isLoading: false,
      error: null,
    })

    render(<SessionCard session={mockSession} />)

    await waitFor(() => {
      expect(screen.queryByText(/PR #/)).not.toBeInTheDocument()
    })
  })

  it('navigates to session detail when card is clicked', async () => {
    mockNavigate.mockReset()

    vi.mocked(issueHooks.useIssueByEntityId).mockReturnValue({
      issue: mockIssue,
      isLoading: false,
      error: null,
    })
    vi.mocked(prHooks.useIssuePrStatus).mockReturnValue({
      prStatus: undefined,
      isLoading: false,
      error: null,
    })

    const user = userEvent.setup()
    render(<SessionCard session={mockSession} />)

    await waitFor(() => {
      expect(screen.getByText('Fix authentication bug')).toBeInTheDocument()
    })

    // Click the card (but not the stop button)
    const card = screen.getByTestId('session-card')
    await user.click(card)

    expect(mockNavigate).toHaveBeenCalledWith(`/projects/project-456/sessions/session-123`)
  })

  it('shows different session status indicators', async () => {
    vi.mocked(issueHooks.useIssueByEntityId).mockReturnValue({
      issue: mockIssue,
      isLoading: false,
      error: null,
    })
    vi.mocked(prHooks.useIssuePrStatus).mockReturnValue({
      prStatus: undefined,
      isLoading: false,
      error: null,
    })

    // Test different session statuses
    const statuses = [
      { value: 2, label: 'Running', dataStatus: 'running' },
      { value: 3, label: 'Waiting', dataStatus: 'idle' },
      { value: 6, label: 'Stopped', dataStatus: 'stopped' },
    ]

    for (const status of statuses) {
      const { unmount } = render(
        <SessionCard session={{ ...mockSession, status: status.value as ClaudeSessionStatus }} />
      )

      await waitFor(() => {
        const statusIndicator = screen.getByTestId('agent-status-indicator')
        expect(statusIndicator).toHaveAttribute('data-status', status.dataStatus)
        expect(screen.getByText(status.label)).toBeInTheDocument()
      })

      unmount()
    }
  })

  it('truncates long descriptions', async () => {
    const longDescription = 'A'.repeat(200)
    vi.mocked(issueHooks.useIssueByEntityId).mockReturnValue({
      issue: { ...mockIssue, description: longDescription },
      isLoading: false,
      error: null,
    })
    vi.mocked(prHooks.useIssuePrStatus).mockReturnValue({
      prStatus: undefined,
      isLoading: false,
      error: null,
    })

    render(<SessionCard session={mockSession} />)

    await waitFor(() => {
      const description = screen.getByTestId('issue-description')
      expect(description.textContent).toHaveLength(153) // 150 chars + "..."
    })
  })

  it('handles stop button click without propagating to card', async () => {
    mockNavigate.mockReset()

    vi.mocked(issueHooks.useIssueByEntityId).mockReturnValue({
      issue: mockIssue,
      isLoading: false,
      error: null,
    })
    vi.mocked(prHooks.useIssuePrStatus).mockReturnValue({
      prStatus: undefined,
      isLoading: false,
      error: null,
    })

    const user = userEvent.setup()
    render(<SessionCard session={mockSession} />)

    await waitFor(() => {
      expect(screen.getByText('Fix authentication bug')).toBeInTheDocument()
    })

    const stopButton = screen.getByRole('button', { name: /stop session/i })
    await user.click(stopButton)

    // Should not navigate when stop button is clicked
    expect(mockNavigate).not.toHaveBeenCalled()
  })

  it('shows Plan mode correctly', async () => {
    vi.mocked(issueHooks.useIssueByEntityId).mockReturnValue({
      issue: mockIssue,
      isLoading: false,
      error: null,
    })
    vi.mocked(prHooks.useIssuePrStatus).mockReturnValue({
      prStatus: undefined,
      isLoading: false,
      error: null,
    })

    const planSession = { ...mockSession, mode: 0 as SessionMode } // Plan mode
    render(<SessionCard session={planSession} />)

    await waitFor(() => {
      expect(screen.getByText('Plan')).toBeInTheDocument()
    })
  })
})
