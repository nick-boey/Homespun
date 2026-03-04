import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent, act } from '@testing-library/react'
import { ScrollToBottom } from './scroll-to-bottom'
import { createRef } from 'react'

describe('ScrollToBottom', () => {
  let mockContainer: HTMLDivElement
  let containerRef: React.RefObject<HTMLDivElement>

  beforeEach(() => {
    // Create a mock scrollable container
    mockContainer = document.createElement('div')
    Object.defineProperties(mockContainer, {
      scrollHeight: { value: 1000, configurable: true },
      scrollTop: { value: 0, configurable: true, writable: true },
      clientHeight: { value: 500, configurable: true },
    })

    containerRef = { current: mockContainer }
  })

  it('is hidden when container is at the bottom', () => {
    // At bottom: scrollHeight - scrollTop - clientHeight = 0
    Object.defineProperty(mockContainer, 'scrollTop', { value: 500, configurable: true })

    render(<ScrollToBottom scrollRef={containerRef} threshold={100} />)

    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('is visible when scrolled away from bottom', () => {
    // Not at bottom: scrollHeight - scrollTop - clientHeight > threshold
    Object.defineProperty(mockContainer, 'scrollTop', { value: 0, configurable: true })

    render(<ScrollToBottom scrollRef={containerRef} threshold={100} />)

    expect(screen.getByRole('button', { name: /scroll to bottom/i })).toBeInTheDocument()
  })

  it('scrolls to bottom when clicked', () => {
    Object.defineProperty(mockContainer, 'scrollTop', { value: 0, configurable: true })
    mockContainer.scrollTo = vi.fn()

    render(<ScrollToBottom scrollRef={containerRef} threshold={100} />)

    fireEvent.click(screen.getByRole('button', { name: /scroll to bottom/i }))

    expect(mockContainer.scrollTo).toHaveBeenCalledWith({
      top: 1000,
      behavior: 'smooth',
    })
  })

  it('updates visibility on scroll', () => {
    Object.defineProperty(mockContainer, 'scrollTop', {
      value: 0,
      configurable: true,
      writable: true,
    })

    render(<ScrollToBottom scrollRef={containerRef} threshold={100} />)

    // Initially visible (far from bottom)
    expect(screen.getByRole('button', { name: /scroll to bottom/i })).toBeInTheDocument()

    // Scroll to bottom
    act(() => {
      Object.defineProperty(mockContainer, 'scrollTop', { value: 500, configurable: true })
      mockContainer.dispatchEvent(new Event('scroll'))
    })

    // Should now be hidden
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })

  it('applies custom className', () => {
    Object.defineProperty(mockContainer, 'scrollTop', { value: 0, configurable: true })

    render(<ScrollToBottom scrollRef={containerRef} className="custom-class" />)

    expect(screen.getByRole('button')).toHaveClass('custom-class')
  })

  it('handles null ref gracefully', () => {
    const nullRef = createRef<HTMLDivElement>()

    // Should not throw
    render(<ScrollToBottom scrollRef={nullRef} />)

    // Should not render button when ref is null
    expect(screen.queryByRole('button')).not.toBeInTheDocument()
  })
})
