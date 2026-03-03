import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { BranchesEmptyState } from './branches-empty-state'

describe('BranchesEmptyState', () => {
  it('renders default title', () => {
    render(<BranchesEmptyState />)

    expect(screen.getByText('No worktrees found')).toBeInTheDocument()
  })

  it('renders default description', () => {
    render(<BranchesEmptyState />)

    expect(
      screen.getByText('Create a worktree from a remote branch to get started.')
    ).toBeInTheDocument()
  })

  it('renders custom title', () => {
    render(<BranchesEmptyState title="Custom Title" />)

    expect(screen.getByText('Custom Title')).toBeInTheDocument()
  })

  it('renders custom description', () => {
    render(<BranchesEmptyState description="Custom description text." />)

    expect(screen.getByText('Custom description text.')).toBeInTheDocument()
  })

  it('renders branch icon', () => {
    const { container } = render(<BranchesEmptyState />)

    // Check for SVG (lucide icon)
    const svg = container.querySelector('svg')
    expect(svg).toBeInTheDocument()
  })
})
