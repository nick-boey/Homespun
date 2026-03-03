import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { AgentStatusIndicator } from './agent-status-indicator'

describe('AgentStatusIndicator', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders ThinkingBar when agent is running', () => {
    render(
      <AgentStatusIndicator
        status={2} // Running
        isActive={true}
      />
    )

    expect(screen.getByText(/working/i)).toBeInTheDocument()
  })

  it('shows Loader when agent is starting', () => {
    render(
      <AgentStatusIndicator
        status={0} // Starting
        isActive={true}
      />
    )

    expect(screen.getByText(/starting/i)).toBeInTheDocument()
  })

  it('shows waiting state when agent is waiting for input', () => {
    render(
      <AgentStatusIndicator
        status={3} // WaitingForInput
        isActive={true}
      />
    )

    expect(screen.getByText(/waiting/i)).toBeInTheDocument()
  })

  it('calls onStop when stop button is clicked', async () => {
    const user = userEvent.setup()
    const onStop = vi.fn()

    render(
      <AgentStatusIndicator
        status={2} // Running
        isActive={true}
        onStop={onStop}
      />
    )

    const stopButton = screen.getByRole('button', { name: /stop/i })
    await user.click(stopButton)

    expect(onStop).toHaveBeenCalled()
  })

  it('displays token count when provided', () => {
    render(<AgentStatusIndicator status={2} isActive={true} tokenCount={1500} />)

    expect(screen.getByText(/1,500/)).toBeInTheDocument()
  })

  it('displays duration when provided', () => {
    render(
      <AgentStatusIndicator
        status={2}
        isActive={true}
        startTime={new Date(Date.now() - 65000)} // 65 seconds ago
      />
    )

    // Should show approximately 1m
    expect(screen.getByText(/1m/)).toBeInTheDocument()
  })

  it('renders nothing when not active', () => {
    const { container } = render(
      <AgentStatusIndicator
        status={5} // Stopped
        isActive={false}
      />
    )

    expect(container.firstChild).toBeNull()
  })
})
