import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import { ProjectsEmptyState } from './projects-empty-state'

// Mock TanStack Router Link
vi.mock('@tanstack/react-router', () => ({
  Link: ({
    children,
    to,
    ...props
  }: {
    children: React.ReactNode
    to: string
    [key: string]: unknown
  }) => (
    <a href={to} {...props}>
      {children}
    </a>
  ),
}))

describe('ProjectsEmptyState', () => {
  it('renders empty state message', () => {
    render(<ProjectsEmptyState />)

    expect(screen.getByText(/no projects/i)).toBeInTheDocument()
  })

  it('renders a link to create a new project', () => {
    render(<ProjectsEmptyState />)

    const createLink = screen.getByRole('link', { name: /create.*project/i })
    expect(createLink).toBeInTheDocument()
    expect(createLink).toHaveAttribute('href', '/projects/new')
  })

  it('displays encouraging message to get started', () => {
    render(<ProjectsEmptyState />)

    expect(screen.getByText(/get started/i)).toBeInTheDocument()
  })
})
