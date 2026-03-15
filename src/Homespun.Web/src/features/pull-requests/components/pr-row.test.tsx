import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PullRequestStatus, type PullRequestInfo } from '@/api'
import { PrRow } from './pr-row'

describe('PrRow', () => {
  const mockPr: PullRequestInfo = {
    number: 123,
    title: 'Add new feature',
    status: PullRequestStatus.READY_FOR_REVIEW,
    branchName: 'feature/new-feature',
    htmlUrl: 'https://github.com/owner/repo/pull/123',
    checksPassing: true,
    isApproved: true,
    approvalCount: 2,
    changesRequestedCount: 0,
  }

  it('renders PR number and title', () => {
    render(<PrRow pr={mockPr} onSelect={vi.fn()} />)
    expect(screen.getByText('#123')).toBeInTheDocument()
    expect(screen.getByText('Add new feature')).toBeInTheDocument()
  })

  it('renders status badge', () => {
    render(<PrRow pr={mockPr} onSelect={vi.fn()} />)
    expect(screen.getByText('Ready for Review')).toBeInTheDocument()
  })

  it('renders CI status badge', () => {
    render(<PrRow pr={mockPr} onSelect={vi.fn()} />)
    expect(screen.getByText('Checks Passing')).toBeInTheDocument()
  })

  it('renders review status badge', () => {
    render(<PrRow pr={mockPr} onSelect={vi.fn()} />)
    expect(screen.getByText('2 Approvals')).toBeInTheDocument()
  })

  it('calls onSelect when row is clicked', async () => {
    const user = userEvent.setup()
    const onSelect = vi.fn()
    render(<PrRow pr={mockPr} onSelect={onSelect} />)

    await user.click(screen.getByRole('button'))
    expect(onSelect).toHaveBeenCalledWith(mockPr)
  })

  it('renders linked issue indicator when linkedIssueId is provided', () => {
    render(<PrRow pr={mockPr} linkedIssueId="issue-123" onSelect={vi.fn()} />)
    expect(screen.getByLabelText('Has linked issue')).toBeInTheDocument()
  })

  it('does not render linked issue indicator when no issue linked', () => {
    render(<PrRow pr={mockPr} onSelect={vi.fn()} />)
    expect(screen.queryByLabelText('Has linked issue')).not.toBeInTheDocument()
  })

  it('applies selected styling when isSelected is true', () => {
    render(<PrRow pr={mockPr} isSelected onSelect={vi.fn()} />)
    const row = screen.getByRole('button')
    expect(row).toHaveClass('bg-accent')
  })

  it('renders View on GitHub link', () => {
    render(<PrRow pr={mockPr} onSelect={vi.fn()} />)
    const link = screen.getByRole('link', { name: /view on github/i })
    expect(link).toHaveAttribute('href', 'https://github.com/owner/repo/pull/123')
  })
})
