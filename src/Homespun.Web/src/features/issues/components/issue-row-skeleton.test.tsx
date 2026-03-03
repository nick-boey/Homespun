import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { IssueRowSkeleton } from './issue-row-skeleton'

describe('IssueRowSkeleton', () => {
  it('renders skeleton with correct test id', () => {
    render(<IssueRowSkeleton />)

    expect(screen.getByTestId('issue-row-skeleton')).toBeInTheDocument()
  })

  it('renders multiple skeleton elements with animate-pulse', () => {
    render(<IssueRowSkeleton />)

    const skeletons = screen
      .getByTestId('issue-row-skeleton')
      .querySelectorAll('[data-slot="skeleton"]')
    expect(skeletons.length).toBeGreaterThanOrEqual(4)

    skeletons.forEach((skeleton) => {
      expect(skeleton).toHaveClass('animate-pulse')
    })
  })

  it('applies custom className', () => {
    render(<IssueRowSkeleton className="custom-class" />)

    expect(screen.getByTestId('issue-row-skeleton')).toHaveClass('custom-class')
  })

  it('has consistent height with actual rows', () => {
    render(<IssueRowSkeleton />)

    const skeleton = screen.getByTestId('issue-row-skeleton')
    // ROW_HEIGHT is 40px (from task-graph-svg.tsx)
    expect(skeleton).toHaveStyle({ height: '40px' })
  })
})
