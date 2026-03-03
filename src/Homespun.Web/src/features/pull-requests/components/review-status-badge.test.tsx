import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { ReviewStatusBadge } from './review-status-badge'

describe('ReviewStatusBadge', () => {
  it('renders approved status when isApproved is true', () => {
    render(<ReviewStatusBadge isApproved={true} approvalCount={2} changesRequestedCount={0} />)
    expect(screen.getByText('2 Approvals')).toBeInTheDocument()
    expect(screen.getByText('2 Approvals')).toHaveClass('bg-green-500/20')
  })

  it('renders single approval', () => {
    render(<ReviewStatusBadge isApproved={true} approvalCount={1} changesRequestedCount={0} />)
    expect(screen.getByText('1 Approval')).toBeInTheDocument()
  })

  it('renders changes requested status', () => {
    render(<ReviewStatusBadge isApproved={false} approvalCount={0} changesRequestedCount={2} />)
    expect(screen.getByText('2 Changes Requested')).toBeInTheDocument()
    expect(screen.getByText('2 Changes Requested')).toHaveClass('bg-orange-500/20')
  })

  it('renders single change requested', () => {
    render(<ReviewStatusBadge isApproved={false} approvalCount={0} changesRequestedCount={1} />)
    expect(screen.getByText('1 Change Requested')).toBeInTheDocument()
  })

  it('renders awaiting review when no reviews', () => {
    render(<ReviewStatusBadge isApproved={false} approvalCount={0} changesRequestedCount={0} />)
    expect(screen.getByText('Awaiting Review')).toBeInTheDocument()
    expect(screen.getByText('Awaiting Review')).toHaveClass('bg-gray-500/20')
  })

  it('renders with small size', () => {
    render(
      <ReviewStatusBadge
        isApproved={true}
        approvalCount={1}
        changesRequestedCount={0}
        size="sm"
      />
    )
    const badge = screen.getByText('1 Approval')
    expect(badge).toHaveClass('px-1.5', 'py-0.5', 'text-xs')
  })

  it('handles undefined values', () => {
    render(
      <ReviewStatusBadge
        isApproved={undefined}
        approvalCount={undefined}
        changesRequestedCount={undefined}
      />
    )
    expect(screen.getByText('Awaiting Review')).toBeInTheDocument()
  })
})
