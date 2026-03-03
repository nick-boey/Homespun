import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { PrRowSkeleton } from './pr-row-skeleton'

describe('PrRowSkeleton', () => {
  it('renders skeleton elements', () => {
    render(<PrRowSkeleton />)
    // Should render multiple skeleton elements
    expect(screen.getByTestId('pr-row-skeleton')).toBeInTheDocument()
  })

  it('applies custom className', () => {
    render(<PrRowSkeleton className="custom-class" />)
    const skeleton = screen.getByTestId('pr-row-skeleton')
    expect(skeleton).toHaveClass('custom-class')
  })
})
