import { describe, it, expect, vi } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { ExecutionModeToggle } from './execution-mode-toggle'
import { ExecutionMode } from '@/api'

describe('ExecutionModeToggle', () => {
  it('renders GitCommitVertical icon for Series mode', () => {
    const onToggle = vi.fn()
    render(<ExecutionModeToggle executionMode={ExecutionMode.SERIES} onToggle={onToggle} />)

    const button = screen.getByRole('button')
    expect(button).toHaveAttribute('aria-label', 'Series execution mode')
    expect(button).toHaveAttribute('title', 'Children execute in series (click to toggle)')
  })

  it('renders GitFork icon for Parallel mode', () => {
    const onToggle = vi.fn()
    render(<ExecutionModeToggle executionMode={ExecutionMode.PARALLEL} onToggle={onToggle} />)

    const button = screen.getByRole('button')
    expect(button).toHaveAttribute('aria-label', 'Parallel execution mode')
    expect(button).toHaveAttribute('title', 'Children execute in parallel (click to toggle)')
  })

  it('calls onToggle when clicked', () => {
    const onToggle = vi.fn()
    render(<ExecutionModeToggle executionMode={ExecutionMode.SERIES} onToggle={onToggle} />)

    const button = screen.getByRole('button')
    fireEvent.click(button)

    expect(onToggle).toHaveBeenCalledTimes(1)
  })

  it('prevents event propagation when clicked', () => {
    const onToggle = vi.fn()
    const onParentClick = vi.fn()

    render(
      <div onClick={onParentClick}>
        <ExecutionModeToggle executionMode={ExecutionMode.SERIES} onToggle={onToggle} />
      </div>
    )

    const button = screen.getByRole('button')
    fireEvent.click(button)

    expect(onToggle).toHaveBeenCalledTimes(1)
    expect(onParentClick).not.toHaveBeenCalled()
  })

  it('is disabled when disabled prop is true', () => {
    const onToggle = vi.fn()
    render(
      <ExecutionModeToggle executionMode={ExecutionMode.SERIES} onToggle={onToggle} disabled />
    )

    const button = screen.getByRole('button')
    expect(button).toBeDisabled()

    fireEvent.click(button)
    expect(onToggle).not.toHaveBeenCalled()
  })

  it('defaults to enabled when disabled prop is not provided', () => {
    const onToggle = vi.fn()
    render(<ExecutionModeToggle executionMode={ExecutionMode.SERIES} onToggle={onToggle} />)

    const button = screen.getByRole('button')
    expect(button).not.toBeDisabled()
  })
})
