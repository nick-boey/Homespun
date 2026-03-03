import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { SecretsEmptyState } from './secrets-empty-state'

describe('SecretsEmptyState', () => {
  it('renders default title', () => {
    render(<SecretsEmptyState />)

    expect(screen.getByText('No secrets configured')).toBeInTheDocument()
  })

  it('renders default description', () => {
    render(<SecretsEmptyState />)

    expect(
      screen.getByText('Add environment variables to securely configure your project.')
    ).toBeInTheDocument()
  })

  it('renders custom title', () => {
    render(<SecretsEmptyState title="Custom Title" />)

    expect(screen.getByText('Custom Title')).toBeInTheDocument()
  })

  it('renders custom description', () => {
    render(<SecretsEmptyState description="Custom description text." />)

    expect(screen.getByText('Custom description text.')).toBeInTheDocument()
  })

  it('renders key icon', () => {
    const { container } = render(<SecretsEmptyState />)

    const svg = container.querySelector('svg')
    expect(svg).toBeInTheDocument()
  })
})
