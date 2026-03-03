import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { CiStatusBadge } from './ci-status-badge'

describe('CiStatusBadge', () => {
  it('renders passing status when checksPassing is true', () => {
    render(<CiStatusBadge checksPassing={true} />)
    expect(screen.getByText('Checks Passing')).toBeInTheDocument()
    expect(screen.getByText('Checks Passing')).toHaveClass('bg-green-500/20')
  })

  it('renders failing status when checksPassing is false', () => {
    render(<CiStatusBadge checksPassing={false} />)
    expect(screen.getByText('Checks Failing')).toBeInTheDocument()
    expect(screen.getByText('Checks Failing')).toHaveClass('bg-red-500/20')
  })

  it('renders pending status when checksPassing is null', () => {
    render(<CiStatusBadge checksPassing={null} />)
    expect(screen.getByText('Checks Pending')).toBeInTheDocument()
    expect(screen.getByText('Checks Pending')).toHaveClass('bg-yellow-500/20')
  })

  it('renders pending status when checksPassing is undefined', () => {
    render(<CiStatusBadge checksPassing={undefined} />)
    expect(screen.getByText('Checks Pending')).toBeInTheDocument()
  })

  it('renders with small size', () => {
    render(<CiStatusBadge checksPassing={true} size="sm" />)
    const badge = screen.getByText('Checks Passing')
    expect(badge).toHaveClass('px-1.5', 'py-0.5', 'text-xs')
  })

  it('renders with default (md) size', () => {
    render(<CiStatusBadge checksPassing={true} size="md" />)
    const badge = screen.getByText('Checks Passing')
    expect(badge).toHaveClass('px-2', 'py-1', 'text-sm')
  })

  it('applies custom className', () => {
    render(<CiStatusBadge checksPassing={true} className="custom-class" />)
    const badge = screen.getByText('Checks Passing')
    expect(badge).toHaveClass('custom-class')
  })
})
