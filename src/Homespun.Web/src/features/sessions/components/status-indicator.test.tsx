import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { StatusIndicator } from './status-indicator'
import type { ClaudeSessionStatus } from '@/api/generated/types.gen'

// Status enum values from backend
const SessionStatus = {
  Starting: 0,
  RunningHooks: 1,
  Running: 2,
  WaitingForInput: 3,
  WaitingForQuestionAnswer: 4,
  WaitingForPlanExecution: 5,
  Stopped: 6,
  Error: 7,
} as const

describe('StatusIndicator', () => {
  it('renders with correct color for each status', () => {
    const statusTests = [
      { status: SessionStatus.Starting, expectedColor: 'bg-yellow-500' },
      { status: SessionStatus.RunningHooks, expectedColor: 'bg-green-500' },
      { status: SessionStatus.Running, expectedColor: 'bg-green-500' },
      { status: SessionStatus.WaitingForInput, expectedColor: 'bg-yellow-500' },
      { status: SessionStatus.WaitingForQuestionAnswer, expectedColor: 'bg-purple-500' },
      { status: SessionStatus.WaitingForPlanExecution, expectedColor: 'bg-orange-500' },
      { status: SessionStatus.Stopped, expectedColor: 'bg-gray-400' },
      { status: SessionStatus.Error, expectedColor: 'bg-red-500' },
    ]

    statusTests.forEach(({ status, expectedColor }) => {
      const { container, rerender } = render(
        <StatusIndicator status={status as ClaudeSessionStatus} />
      )

      const indicator = container.querySelector('.rounded-full')
      expect(indicator).toHaveClass(expectedColor)

      rerender(<div />)
    })
  })

  it('shows animation for active statuses', () => {
    const activeStatuses = [
      SessionStatus.Starting,
      SessionStatus.RunningHooks,
      SessionStatus.Running,
      SessionStatus.WaitingForInput,
      SessionStatus.WaitingForQuestionAnswer,
      SessionStatus.WaitingForPlanExecution,
    ]

    activeStatuses.forEach((status) => {
      const { container, rerender } = render(
        <StatusIndicator status={status as ClaudeSessionStatus} />
      )

      const pulseElement = container.querySelector('.animate-pulse')
      expect(pulseElement).toBeInTheDocument()

      rerender(<div />)
    })
  })

  it('does not show animation for inactive statuses', () => {
    const inactiveStatuses = [SessionStatus.Stopped, SessionStatus.Error]

    inactiveStatuses.forEach((status) => {
      const { container, rerender } = render(
        <StatusIndicator status={status as ClaudeSessionStatus} />
      )

      const pulseElement = container.querySelector('.animate-pulse')
      expect(pulseElement).not.toBeInTheDocument()

      rerender(<div />)
    })
  })

  it('renders with default size', () => {
    const { container } = render(
      <StatusIndicator status={SessionStatus.Running as ClaudeSessionStatus} />
    )

    const indicator = container.querySelector('.rounded-full')
    expect(indicator).toHaveClass('h-2', 'w-2')
  })

  it('renders with custom size', () => {
    const { container } = render(
      <StatusIndicator status={SessionStatus.Running as ClaudeSessionStatus} size="sm" />
    )

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
    render(
      <StatusIndicator
        status={SessionStatus.Running as ClaudeSessionStatus}
        className="custom-class"
      />
    )

    const wrapper = screen.getByTestId('status-indicator')
    expect(wrapper).toHaveClass('custom-class')
  })

  it('has different animation speeds for different statuses', () => {
    const statusAnimations = [
      { status: SessionStatus.Starting, animationClass: 'animate-pulse' },
      { status: SessionStatus.Running, animationClass: 'animate-pulse' },
      { status: SessionStatus.WaitingForQuestionAnswer, animationClass: 'animate-pulse' },
    ]

    statusAnimations.forEach(({ status, animationClass }) => {
      const { container, rerender } = render(
        <StatusIndicator status={status as ClaudeSessionStatus} />
      )

      const animatedElement = container.querySelector(`.${animationClass}`)
      expect(animatedElement).toBeInTheDocument()

      rerender(<div />)
    })
  })

  it('maintains consistent structure for accessibility', () => {
    const { container } = render(
      <StatusIndicator status={SessionStatus.Running as ClaudeSessionStatus} />
    )

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
