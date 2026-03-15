import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PullRequestStatus, type PullRequestInfo } from '@/api'
import { OpenPrDetailPanel } from './open-pr-detail-panel'

describe('OpenPrDetailPanel', () => {
  const mockPr: PullRequestInfo = {
    number: 123,
    title: 'Add new feature',
    body: 'This is a description of the PR.',
    status: PullRequestStatus.READY_FOR_REVIEW,
    branchName: 'feature/new-feature',
    htmlUrl: 'https://github.com/owner/repo/pull/123',
    createdAt: '2024-01-15T10:00:00Z',
    checksPassing: true,
    isApproved: true,
    approvalCount: 2,
    changesRequestedCount: 0,
    isMergeable: true,
  }

  it('renders PR number and title', () => {
    render(<OpenPrDetailPanel pr={mockPr} />)
    expect(screen.getByText('#123')).toBeInTheDocument()
    expect(screen.getByText('Add new feature')).toBeInTheDocument()
  })

  it('renders branch name', () => {
    render(<OpenPrDetailPanel pr={mockPr} />)
    expect(screen.getByText('feature/new-feature')).toBeInTheDocument()
  })

  it('renders PR status badge', () => {
    render(<OpenPrDetailPanel pr={mockPr} />)
    expect(screen.getByText('Ready for Review')).toBeInTheDocument()
  })

  it('renders CI status badge', () => {
    render(<OpenPrDetailPanel pr={mockPr} />)
    expect(screen.getByText('Checks Passing')).toBeInTheDocument()
  })

  it('renders review status badge', () => {
    render(<OpenPrDetailPanel pr={mockPr} />)
    expect(screen.getByText('2 Approvals')).toBeInTheDocument()
  })

  it('renders description', () => {
    render(<OpenPrDetailPanel pr={mockPr} />)
    expect(screen.getByText('This is a description of the PR.')).toBeInTheDocument()
  })

  it('renders no description message when body is empty', () => {
    const prWithoutBody = { ...mockPr, body: undefined }
    render(<OpenPrDetailPanel pr={prWithoutBody} />)
    expect(screen.getByText('No description')).toBeInTheDocument()
  })

  it('renders View on GitHub link', () => {
    render(<OpenPrDetailPanel pr={mockPr} />)
    const link = screen.getByRole('link', { name: /view on github/i })
    expect(link).toHaveAttribute('href', 'https://github.com/owner/repo/pull/123')
    expect(link).toHaveAttribute('target', '_blank')
  })

  it('calls onViewIssue when linked issue is clicked', async () => {
    const user = userEvent.setup()
    const onViewIssue = vi.fn()
    render(<OpenPrDetailPanel pr={mockPr} linkedIssueId="issue-123" onViewIssue={onViewIssue} />)

    await user.click(screen.getByText('View Linked Issue'))
    expect(onViewIssue).toHaveBeenCalledWith('issue-123')
  })

  it('does not render linked issue button when no issue linked', () => {
    render(<OpenPrDetailPanel pr={mockPr} />)
    expect(screen.queryByText('View Linked Issue')).not.toBeInTheDocument()
  })

  it('calls onClose when close button is clicked', async () => {
    const user = userEvent.setup()
    const onClose = vi.fn()
    render(<OpenPrDetailPanel pr={mockPr} onClose={onClose} />)

    await user.click(screen.getByRole('button', { name: /close/i }))
    expect(onClose).toHaveBeenCalled()
  })

  it('calls onStartAgent when start agent button is clicked', async () => {
    const user = userEvent.setup()
    const onStartAgent = vi.fn()
    render(<OpenPrDetailPanel pr={mockPr} onStartAgent={onStartAgent} />)

    await user.click(screen.getByRole('button', { name: /start agent/i }))
    expect(onStartAgent).toHaveBeenCalledWith('feature/new-feature')
  })

  it('renders mergeable status', () => {
    render(<OpenPrDetailPanel pr={mockPr} />)
    expect(screen.getByText('Ready to merge')).toBeInTheDocument()
  })

  it('renders not mergeable status when has conflicts', () => {
    const prWithConflicts = { ...mockPr, isMergeable: false }
    render(<OpenPrDetailPanel pr={prWithConflicts} />)
    expect(screen.getByText('Has conflicts')).toBeInTheDocument()
  })
})
