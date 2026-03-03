import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MutationIndicator, getMutationStatus } from './mutation-indicator'

describe('MutationIndicator', () => {
  it('renders nothing when status is idle', () => {
    const { container } = render(<MutationIndicator status="idle" />)
    expect(container).toBeEmptyDOMElement()
  })

  it('shows spinner and pending text when status is pending', () => {
    render(<MutationIndicator status="pending" />)

    const indicator = screen.getByTestId('mutation-indicator')
    expect(indicator).toHaveAttribute('data-status', 'pending')
    expect(screen.getByText('Saving...')).toBeInTheDocument()
  })

  it('shows checkmark and success text when status is success', () => {
    render(<MutationIndicator status="success" />)

    const indicator = screen.getByTestId('mutation-indicator')
    expect(indicator).toHaveAttribute('data-status', 'success')
    expect(screen.getByText('Saved')).toBeInTheDocument()
    expect(indicator).toHaveClass('text-green-600')
  })

  it('shows X and error text when status is error', () => {
    render(<MutationIndicator status="error" />)

    const indicator = screen.getByTestId('mutation-indicator')
    expect(indicator).toHaveAttribute('data-status', 'error')
    expect(screen.getByText('Failed')).toBeInTheDocument()
    expect(indicator).toHaveClass('text-destructive')
  })

  it('uses custom text props', () => {
    render(<MutationIndicator status="pending" pendingText="Creating..." />)

    expect(screen.getByText('Creating...')).toBeInTheDocument()
  })

  it('hides text when showText is false', () => {
    render(<MutationIndicator status="pending" showText={false} />)

    expect(screen.queryByText('Saving...')).not.toBeInTheDocument()
  })

  it('applies custom className', () => {
    render(<MutationIndicator status="pending" className="custom-class" />)

    expect(screen.getByTestId('mutation-indicator')).toHaveClass('custom-class')
  })
})

describe('getMutationStatus', () => {
  it('returns pending when isPending is true', () => {
    expect(getMutationStatus({ isPending: true, isSuccess: false, isError: false })).toBe('pending')
  })

  it('returns success when isSuccess is true', () => {
    expect(getMutationStatus({ isPending: false, isSuccess: true, isError: false })).toBe('success')
  })

  it('returns error when isError is true', () => {
    expect(getMutationStatus({ isPending: false, isSuccess: false, isError: true })).toBe('error')
  })

  it('returns idle when all flags are false', () => {
    expect(getMutationStatus({ isPending: false, isSuccess: false, isError: false })).toBe('idle')
  })

  it('prioritizes pending over other states', () => {
    expect(getMutationStatus({ isPending: true, isSuccess: true, isError: false })).toBe('pending')
  })
})
