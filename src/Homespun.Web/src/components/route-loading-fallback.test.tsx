import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { RouteLoadingFallback } from './route-loading-fallback'

describe('RouteLoadingFallback', () => {
  it('renders with correct test id', () => {
    render(<RouteLoadingFallback />)

    expect(screen.getByTestId('route-loading-fallback')).toBeInTheDocument()
  })

  it('renders multiple skeleton elements', () => {
    render(<RouteLoadingFallback />)

    const skeletons = screen
      .getByTestId('route-loading-fallback')
      .querySelectorAll('[data-slot="skeleton"]')
    expect(skeletons.length).toBeGreaterThanOrEqual(10)
  })

  it('has fade-in animation class', () => {
    render(<RouteLoadingFallback />)

    const container = screen.getByTestId('route-loading-fallback')
    expect(container).toHaveClass('animate-in')
    expect(container).toHaveClass('fade-in-0')
  })

  it('applies custom className', () => {
    render(<RouteLoadingFallback className="custom-class" />)

    expect(screen.getByTestId('route-loading-fallback')).toHaveClass('custom-class')
  })

  it('renders header skeleton section', () => {
    render(<RouteLoadingFallback />)

    const container = screen.getByTestId('route-loading-fallback')
    // Check for header layout (flex with items-start justify-between)
    const headerSection = container.querySelector('.flex.items-start.justify-between')
    expect(headerSection).toBeInTheDocument()
  })

  it('renders tab navigation skeleton section', () => {
    render(<RouteLoadingFallback />)

    const container = screen.getByTestId('route-loading-fallback')
    // Check for tab bar (flex gap-1 border-b)
    const tabSection = container.querySelector('.border-b')
    expect(tabSection).toBeInTheDocument()
  })

  it('renders content cards skeleton section', () => {
    render(<RouteLoadingFallback />)

    const container = screen.getByTestId('route-loading-fallback')
    // Check for card items (rounded-lg border)
    const cards = container.querySelectorAll('.rounded-lg.border')
    expect(cards.length).toBe(3)
  })
})
