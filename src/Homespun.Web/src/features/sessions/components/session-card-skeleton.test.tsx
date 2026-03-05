import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { SessionCardSkeleton } from './session-card-skeleton'

describe('SessionCardSkeleton', () => {
  it('renders skeleton elements for all card sections', () => {
    render(<SessionCardSkeleton />)

    // Check for skeleton elements
    const skeletons = screen.getAllByTestId(/skeleton/)
    expect(skeletons.length).toBeGreaterThan(0)

    // Check for specific skeleton sections
    expect(screen.getByTestId('title-skeleton')).toBeInTheDocument()
    expect(screen.getByTestId('badges-skeleton')).toBeInTheDocument()
    expect(screen.getByTestId('description-skeleton')).toBeInTheDocument()
    expect(screen.getByTestId('session-info-skeleton')).toBeInTheDocument()
    expect(screen.getByTestId('agent-status-skeleton')).toBeInTheDocument()
  })

  it('has the same layout structure as the actual card', () => {
    render(<SessionCardSkeleton />)

    const card = screen.getByTestId('session-card-skeleton')
    expect(card).toHaveClass('rounded-lg', 'border', 'p-4')
  })

  it('renders multiple instances without id conflicts', () => {
    const { container } = render(
      <>
        <SessionCardSkeleton />
        <SessionCardSkeleton />
        <SessionCardSkeleton />
      </>
    )

    const cards = container.querySelectorAll('[data-testid="session-card-skeleton"]')
    expect(cards).toHaveLength(3)
  })

  it('has appropriate skeleton sizes for different content areas', () => {
    render(<SessionCardSkeleton />)

    // Title should be larger
    const titleSkeleton = screen.getByTestId('title-skeleton')
    expect(titleSkeleton).toHaveClass('h-6', 'w-3/4')

    // Badges should be smaller
    const badgesSkeleton = screen.getByTestId('badges-skeleton')
    const badges = badgesSkeleton.querySelectorAll('[data-testid*="skeleton"]')
    badges.forEach(badge => {
      expect(badge).toHaveClass('h-5')
    })

    // Description should be multi-line
    const descriptionSkeleton = screen.getByTestId('description-skeleton')
    const lines = descriptionSkeleton.querySelectorAll('[data-testid*="skeleton"]')
    expect(lines.length).toBeGreaterThanOrEqual(2)
  })

  it('uses shimmer animation', () => {
    const { container } = render(<SessionCardSkeleton />)

    // Select actual skeleton components by their data-slot attribute
    const skeletons = container.querySelectorAll('[data-slot="skeleton"]')
    expect(skeletons.length).toBeGreaterThan(0)

    skeletons.forEach(skeleton => {
      expect(skeleton).toHaveClass('animate-pulse')
    })
  })
})