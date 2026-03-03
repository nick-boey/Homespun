import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { PullRequestStatus } from '@/api'
import { PrStatusBadge } from './pr-status-badge'

describe('PrStatusBadge', () => {
  it('renders Draft status', () => {
    render(<PrStatusBadge status={PullRequestStatus[0]} />)
    expect(screen.getByText('Draft')).toBeInTheDocument()
    expect(screen.getByText('Draft')).toHaveClass('bg-gray-500/20')
  })

  it('renders Open status', () => {
    render(<PrStatusBadge status={PullRequestStatus[1]} />)
    expect(screen.getByText('Open')).toBeInTheDocument()
    expect(screen.getByText('Open')).toHaveClass('bg-green-500/20')
  })

  it('renders Merged status', () => {
    render(<PrStatusBadge status={PullRequestStatus[2]} />)
    expect(screen.getByText('Merged')).toBeInTheDocument()
    expect(screen.getByText('Merged')).toHaveClass('bg-purple-500/20')
  })

  it('renders Closed status', () => {
    render(<PrStatusBadge status={PullRequestStatus[3]} />)
    expect(screen.getByText('Closed')).toBeInTheDocument()
    expect(screen.getByText('Closed')).toHaveClass('bg-red-500/20')
  })

  it('renders ChangesRequested status', () => {
    render(<PrStatusBadge status={PullRequestStatus[4]} />)
    expect(screen.getByText('Changes Requested')).toBeInTheDocument()
    expect(screen.getByText('Changes Requested')).toHaveClass('bg-orange-500/20')
  })

  it('renders Approved status', () => {
    render(<PrStatusBadge status={PullRequestStatus[5]} />)
    expect(screen.getByText('Approved')).toBeInTheDocument()
    expect(screen.getByText('Approved')).toHaveClass('bg-blue-500/20')
  })

  it('renders Unknown status for unknown values', () => {
    render(<PrStatusBadge status={99 as PullRequestStatus} />)
    expect(screen.getByText('Unknown')).toBeInTheDocument()
  })

  it('renders with small size', () => {
    render(<PrStatusBadge status={PullRequestStatus[1]} size="sm" />)
    const badge = screen.getByText('Open')
    expect(badge).toHaveClass('px-1.5', 'py-0.5', 'text-xs')
  })

  it('renders with default (md) size', () => {
    render(<PrStatusBadge status={PullRequestStatus[1]} size="md" />)
    const badge = screen.getByText('Open')
    expect(badge).toHaveClass('px-2', 'py-1', 'text-sm')
  })
})
