import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { IssuesEmptyState } from './issues-empty-state'

describe('IssuesEmptyState', () => {
  it('renders the empty state message and button', () => {
    render(<IssuesEmptyState onCreateIssue={vi.fn()} />)

    expect(screen.getByText('No issues are currently open')).toBeInTheDocument()
    expect(screen.getByText('Get started by creating your first issue.')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Create an issue' })).toBeInTheDocument()
  })

  it('calls onCreateIssue when the button is clicked', () => {
    const onCreateIssue = vi.fn()
    render(<IssuesEmptyState onCreateIssue={onCreateIssue} />)

    fireEvent.click(screen.getByRole('button', { name: 'Create an issue' }))
    expect(onCreateIssue).toHaveBeenCalledTimes(1)
  })

  it('disables the button and shows loading text when isCreating is true', () => {
    render(<IssuesEmptyState onCreateIssue={vi.fn()} isCreating />)

    const button = screen.getByRole('button', { name: 'Creating...' })
    expect(button).toBeDisabled()
  })
})
