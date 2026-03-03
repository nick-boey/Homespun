import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PullRequestStatus, type PullRequestInfo, type IssueResponse } from '@/api'
import { MergedPrDetailPanel } from './merged-pr-detail-panel'

describe('MergedPrDetailPanel', () => {
  const mockPr: PullRequestInfo = {
    number: 120,
    title: 'Add feature X',
    body: 'This PR adds feature X.',
    status: PullRequestStatus[2],
    branchName: 'feature/feature-x',
    htmlUrl: 'https://github.com/owner/repo/pull/120',
    mergedAt: '2024-01-20T15:30:00Z',
    createdAt: '2024-01-15T10:00:00Z',
  }

  const mockLinkedIssue: IssueResponse = {
    id: 'issue-123',
    title: 'Implement feature X',
  }

  it('renders PR number and title', () => {
    render(<MergedPrDetailPanel pr={mockPr} />)
    expect(screen.getByText('#120')).toBeInTheDocument()
    expect(screen.getByText('Add feature X')).toBeInTheDocument()
  })

  it('renders Merged status badge', () => {
    render(<MergedPrDetailPanel pr={mockPr} />)
    expect(screen.getByText('Merged')).toBeInTheDocument()
  })

  it('renders merge date', () => {
    render(<MergedPrDetailPanel pr={mockPr} />)
    // Check for formatted date (depends on locale)
    expect(screen.getByText(/merged on/i)).toBeInTheDocument()
  })

  it('renders description', () => {
    render(<MergedPrDetailPanel pr={mockPr} />)
    expect(screen.getByText('This PR adds feature X.')).toBeInTheDocument()
  })

  it('renders no description message when body is empty', () => {
    const prWithoutBody = { ...mockPr, body: undefined }
    render(<MergedPrDetailPanel pr={prWithoutBody} />)
    expect(screen.getByText('No description')).toBeInTheDocument()
  })

  it('renders View on GitHub link', () => {
    render(<MergedPrDetailPanel pr={mockPr} />)
    const link = screen.getByRole('link', { name: /view on github/i })
    expect(link).toHaveAttribute('href', 'https://github.com/owner/repo/pull/120')
    expect(link).toHaveAttribute('target', '_blank')
  })

  it('renders linked issue when provided', () => {
    render(<MergedPrDetailPanel pr={mockPr} linkedIssue={mockLinkedIssue} />)
    expect(screen.getByText('Linked Issue')).toBeInTheDocument()
    expect(screen.getByText('Implement feature X')).toBeInTheDocument()
  })

  it('does not render linked issue section when no issue linked', () => {
    render(<MergedPrDetailPanel pr={mockPr} />)
    expect(screen.queryByText('Linked Issue')).not.toBeInTheDocument()
  })

  it('calls onViewIssue when linked issue is clicked', async () => {
    const user = userEvent.setup()
    const onViewIssue = vi.fn()
    render(
      <MergedPrDetailPanel
        pr={mockPr}
        linkedIssue={mockLinkedIssue}
        onViewIssue={onViewIssue}
      />
    )

    await user.click(screen.getByText('Implement feature X'))
    expect(onViewIssue).toHaveBeenCalledWith('issue-123')
  })

  it('calls onClose when close button is clicked', async () => {
    const user = userEvent.setup()
    const onClose = vi.fn()
    render(<MergedPrDetailPanel pr={mockPr} onClose={onClose} />)

    await user.click(screen.getByRole('button', { name: /close/i }))
    expect(onClose).toHaveBeenCalled()
  })

  it('renders time spent when provided', () => {
    render(<MergedPrDetailPanel pr={mockPr} timeSpentMinutes={120} />)
    expect(screen.getByText(/2h 0m/)).toBeInTheDocument()
  })

  it('formats time spent correctly for hours and minutes', () => {
    render(<MergedPrDetailPanel pr={mockPr} timeSpentMinutes={90} />)
    expect(screen.getByText(/1h 30m/)).toBeInTheDocument()
  })

  it('formats time spent correctly for minutes only', () => {
    render(<MergedPrDetailPanel pr={mockPr} timeSpentMinutes={45} />)
    expect(screen.getByText(/45m/)).toBeInTheDocument()
  })
})
