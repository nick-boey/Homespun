import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { PromptsEmptyState } from './prompts-empty-state'

describe('PromptsEmptyState', () => {
  it('renders default title and description', () => {
    render(<PromptsEmptyState />)

    expect(screen.getByText('No prompts yet')).toBeInTheDocument()
    expect(
      screen.getByText('Create custom system prompts to customize how agents work on this project.')
    ).toBeInTheDocument()
  })

  it('renders custom title and description', () => {
    render(<PromptsEmptyState title="Custom Title" description="Custom description text" />)

    expect(screen.getByText('Custom Title')).toBeInTheDocument()
    expect(screen.getByText('Custom description text')).toBeInTheDocument()
  })
})
