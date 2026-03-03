import { describe, it, expect } from 'vitest'
import { render } from '@testing-library/react'
import { BranchCardSkeleton } from './branch-card-skeleton'

describe('BranchCardSkeleton', () => {
  it('renders without crashing', () => {
    const { container } = render(<BranchCardSkeleton />)

    expect(container.firstChild).toBeInTheDocument()
  })

  it('renders skeleton elements with animation', () => {
    render(<BranchCardSkeleton />)

    // Should have multiple skeleton elements with animation
    const skeletons = document.querySelectorAll('[class*="animate-pulse"]')
    expect(skeletons.length).toBeGreaterThan(0)
  })
})
