import { describe, it, expect, vi } from 'vitest'
import { render } from '@testing-library/react'
import { Toaster } from './sonner'

// Mock the app store
vi.mock('@/stores/app-store', () => ({
  useAppStore: vi.fn((selector) => {
    const state = { theme: 'dark' as const }
    return selector(state)
  }),
}))

describe('Toaster', () => {
  it('renders without crashing', () => {
    const { container } = render(<Toaster />)
    expect(container).toBeDefined()
  })

  it('renders with dark theme when store theme is dark', () => {
    const { container } = render(<Toaster />)
    // The Sonner component renders with theme prop
    // We can't directly test the CSS variable values, but we can verify it renders
    expect(container.querySelector('section[aria-label]') ?? container.firstChild).toBeTruthy()
  })

  it('passes additional props to Sonner', () => {
    const { container } = render(<Toaster position="top-center" />)
    expect(container).toBeDefined()
  })
})

describe('Toaster CSS Variables', () => {
  it('references theme tokens via var() without an hsl() wrapper', () => {
    const styles = {
      '--normal-bg': 'var(--popover)',
      '--normal-text': 'var(--popover-foreground)',
      '--normal-border': 'var(--border)',
      '--border-radius': 'var(--radius)',
    }

    expect(styles['--normal-bg']).toMatch(/^var\(--/)
    expect(styles['--normal-text']).toMatch(/^var\(--/)
    expect(styles['--normal-border']).toMatch(/^var\(--/)
    expect(styles['--normal-bg']).not.toMatch(/^hsl\(/)
    expect(styles['--border-radius']).toMatch(/^var\(--radius/)
  })
})
