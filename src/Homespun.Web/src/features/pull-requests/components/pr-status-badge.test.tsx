import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { PullRequestStatus } from '@/api'
import { PrStatusBadge } from './pr-status-badge'

describe('PrStatusBadge', () => {
  it('renders In Progress status', () => {
    render(<PrStatusBadge status={PullRequestStatus.IN_PROGRESS} />)
    expect(screen.getByText('In Progress')).toBeInTheDocument()
    expect(screen.getByText('In Progress')).toHaveClass('bg-gray-500/20')
  })

  it('renders Ready for Review status', () => {
    render(<PrStatusBadge status={PullRequestStatus.READY_FOR_REVIEW} />)
    expect(screen.getByText('Ready for Review')).toBeInTheDocument()
    expect(screen.getByText('Ready for Review')).toHaveClass('bg-green-500/20')
  })

  it('renders Merged status', () => {
    render(<PrStatusBadge status={PullRequestStatus.MERGED} />)
    expect(screen.getByText('Merged')).toBeInTheDocument()
    expect(screen.getByText('Merged')).toHaveClass('bg-purple-500/20')
  })

  it('renders Closed status', () => {
    render(<PrStatusBadge status={PullRequestStatus.CLOSED} />)
    expect(screen.getByText('Closed')).toBeInTheDocument()
    expect(screen.getByText('Closed')).toHaveClass('bg-yellow-500/20')
  })

  it('renders Checks Failing status', () => {
    render(<PrStatusBadge status={PullRequestStatus.CHECKS_FAILING} />)
    expect(screen.getByText('Checks Failing')).toBeInTheDocument()
    expect(screen.getByText('Checks Failing')).toHaveClass('bg-red-500/20')
  })

  it('renders Conflict status', () => {
    render(<PrStatusBadge status={PullRequestStatus.CONFLICT} />)
    expect(screen.getByText('Conflict')).toBeInTheDocument()
    expect(screen.getByText('Conflict')).toHaveClass('bg-orange-500/20')
  })

  it('renders Ready for Merging status', () => {
    render(<PrStatusBadge status={PullRequestStatus.READY_FOR_MERGING} />)
    expect(screen.getByText('Ready for Merging')).toBeInTheDocument()
    expect(screen.getByText('Ready for Merging')).toHaveClass('bg-blue-500/20')
  })

  it('renders Unknown status for unknown values', () => {
    render(<PrStatusBadge status={'unknown' as PullRequestStatus} />)
    expect(screen.getByText('Unknown')).toBeInTheDocument()
  })

  it('renders with small size', () => {
    render(<PrStatusBadge status={PullRequestStatus.READY_FOR_REVIEW} size="sm" />)
    const badge = screen.getByText('Ready for Review')
    expect(badge).toHaveClass('px-1.5', 'py-0.5', 'text-xs')
  })

  it('renders with default (md) size', () => {
    render(<PrStatusBadge status={PullRequestStatus.READY_FOR_REVIEW} size="md" />)
    const badge = screen.getByText('Ready for Review')
    expect(badge).toHaveClass('px-2', 'py-1', 'text-sm')
  })
})
