import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { ProjectCardSkeleton } from './project-card-skeleton'

describe('ProjectCardSkeleton', () => {
  it('renders skeleton card', () => {
    render(<ProjectCardSkeleton />)

    // Check that card structure exists
    const card = screen.getByTestId('project-card-skeleton')
    expect(card).toBeInTheDocument()
  })

  it('renders multiple skeleton elements', () => {
    render(<ProjectCardSkeleton />)

    // Should have multiple skeleton elements for name, path, etc.
    const skeletons = document.querySelectorAll('[class*="animate-pulse"]')
    expect(skeletons.length).toBeGreaterThan(0)
  })
})
