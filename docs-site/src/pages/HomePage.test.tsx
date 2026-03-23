import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { HomePage } from './HomePage'

describe('HomePage', () => {
  it('renders the page title', () => {
    render(
      <MemoryRouter>
        <HomePage />
      </MemoryRouter>
    )
    expect(screen.getByText('Homespun Documentation')).toBeInTheDocument()
  })

  it('renders cards for all documentation pages', () => {
    render(
      <MemoryRouter>
        <HomePage />
      </MemoryRouter>
    )
    expect(screen.getByText('Installation')).toBeInTheDocument()
    expect(screen.getByText('Usage Guide')).toBeInTheDocument()
    expect(screen.getByText('Multi-User Setup')).toBeInTheDocument()
    expect(screen.getByText('Troubleshooting')).toBeInTheDocument()
  })

  it('links to documentation pages', () => {
    render(
      <MemoryRouter>
        <HomePage />
      </MemoryRouter>
    )
    const installLink = screen.getByText('Installation').closest('a')
    expect(installLink?.getAttribute('href')).toBe('/docs/installation')
  })
})
