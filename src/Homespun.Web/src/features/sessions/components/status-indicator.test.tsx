import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { StatusIndicator } from './status-indicator'
import { ClaudeSessionStatus } from '@/api/generated/types.gen'

describe('StatusIndicator', () => {
  it('renders with correct color for each status', () => {
    const statusTests = [
      { status: ClaudeSessionStatus.STARTING, expectedColor: 'bg-yellow-500' },
      { status: ClaudeSessionStatus.RUNNING_HOOKS, expectedColor: 'bg-green-500' },
      { status: ClaudeSessionStatus.RUNNING, expectedColor: 'bg-green-500' },
      { status: ClaudeSessionStatus.WAITING_FOR_INPUT, expectedColor: 'bg-yellow-500' },
      { status: ClaudeSessionStatus.WAITING_FOR_QUESTION_ANSWER, expectedColor: 'bg-purple-500' },
      { status: ClaudeSessionStatus.WAITING_FOR_PLAN_EXECUTION, expectedColor: 'bg-orange-500' },
      { status: ClaudeSessionStatus.STOPPED, expectedColor: 'bg-gray-400' },
      { status: ClaudeSessionStatus.ERROR, expectedColor: 'bg-red-500' },
    ]

    statusTests.forEach(({ status, expectedColor }) => {
      const { container, rerender } = render(<StatusIndicator status={status} />)

      const indicator = container.querySelector('.rounded-full')
      expect(indicator).toHaveClass(expectedColor)

      rerender(<div />)
    })
  })

  it('shows animation for active statuses', () => {
    const activeStatuses = [
      ClaudeSessionStatus.STARTING,
      ClaudeSessionStatus.RUNNING_HOOKS,
      ClaudeSessionStatus.RUNNING,
      ClaudeSessionStatus.WAITING_FOR_INPUT,
      ClaudeSessionStatus.WAITING_FOR_QUESTION_ANSWER,
      ClaudeSessionStatus.WAITING_FOR_PLAN_EXECUTION,
    ]

    activeStatuses.forEach((status) => {
      const { container, rerender } = render(<StatusIndicator status={status} />)

      const pulseElement = container.querySelector('.animate-pulse')
      expect(pulseElement).toBeInTheDocument()

      rerender(<div />)
    })
  })

  it('does not show animation for inactive statuses', () => {
    const inactiveStatuses = [ClaudeSessionStatus.STOPPED, ClaudeSessionStatus.ERROR]

    inactiveStatuses.forEach((status) => {
      const { container, rerender } = render(<StatusIndicator status={status} />)

      const pulseElement = container.querySelector('.animate-pulse')
      expect(pulseElement).not.toBeInTheDocument()

      rerender(<div />)
    })
  })

  it('renders with default size', () => {
    const { container } = render(<StatusIndicator status={ClaudeSessionStatus.RUNNING} />)

    const indicator = container.querySelector('.rounded-full')
    expect(indicator).toHaveClass('h-2', 'w-2')
  })

  it('renders with custom size', () => {
    const { container } = render(<StatusIndicator status={ClaudeSessionStatus.RUNNING} size="sm" />)

    const indicator = container.querySelector('.rounded-full')
    expect(indicator).toHaveClass('h-1.5', 'w-1.5')
  })

  it('handles undefined status', () => {
    const { container } = render(<StatusIndicator status={undefined} />)

    const indicator = container.querySelector('.rounded-full')
    expect(indicator).toHaveClass('bg-gray-400')
    expect(container.querySelector('.animate-pulse')).not.toBeInTheDocument()
  })

  it('renders with custom className', () => {
    render(<StatusIndicator status={ClaudeSessionStatus.RUNNING} className="custom-class" />)

    const wrapper = screen.getByTestId('status-indicator')
    expect(wrapper).toHaveClass('custom-class')
  })

  it('has different animation speeds for different statuses', () => {
    const statusAnimations = [
      { status: ClaudeSessionStatus.STARTING, animationClass: 'animate-pulse' },
      { status: ClaudeSessionStatus.RUNNING, animationClass: 'animate-pulse' },
      { status: ClaudeSessionStatus.WAITING_FOR_QUESTION_ANSWER, animationClass: 'animate-pulse' },
    ]

    statusAnimations.forEach(({ status, animationClass }) => {
      const { container, rerender } = render(<StatusIndicator status={status} />)

      const animatedElement = container.querySelector(`.${animationClass}`)
      expect(animatedElement).toBeInTheDocument()

      rerender(<div />)
    })
  })

  it('maintains consistent structure for accessibility', () => {
    const { container } = render(<StatusIndicator status={ClaudeSessionStatus.RUNNING} />)

    // Should have a relative container
    const relativeContainer = container.querySelector('.relative')
    expect(relativeContainer).toBeInTheDocument()

    // Should have the actual indicator dot
    const indicatorDot = container.querySelector('.rounded-full.bg-green-500')
    expect(indicatorDot).toBeInTheDocument()

    // For active status, should have animation wrapper
    const animationWrapper = container.querySelector('.absolute')
    expect(animationWrapper).toBeInTheDocument()
  })
})
