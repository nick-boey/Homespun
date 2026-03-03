import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { PlanApprovalPanel } from './plan-approval-panel'

describe('PlanApprovalPanel', () => {
  const defaultProps = {
    planContent: '# Implementation Plan\n\n1. Step one\n2. Step two',
    planFilePath: '/path/to/plan.md',
    onApproveClearContext: vi.fn(),
    onApproveKeepContext: vi.fn(),
    onReject: vi.fn(),
    isLoading: false,
  }

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders header with title and icon', () => {
    render(<PlanApprovalPanel {...defaultProps} />)

    expect(screen.getByText('Plan Ready for Implementation')).toBeInTheDocument()
  })

  it('renders description text', () => {
    render(<PlanApprovalPanel {...defaultProps} />)

    expect(screen.getByText('Choose how to proceed with the implementation:')).toBeInTheDocument()
  })

  it('renders three action buttons', () => {
    render(<PlanApprovalPanel {...defaultProps} />)

    expect(
      screen.getByRole('button', { name: /clear context & start implementation/i })
    ).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /continue with context/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /reject & modify/i })).toBeInTheDocument()
  })

  describe('Clear Context & Start Implementation button', () => {
    it('calls onApproveClearContext when clicked', async () => {
      const user = userEvent.setup()
      render(<PlanApprovalPanel {...defaultProps} />)

      await user.click(
        screen.getByRole('button', { name: /clear context & start implementation/i })
      )

      expect(defaultProps.onApproveClearContext).toHaveBeenCalledTimes(1)
    })

    it('is disabled when isLoading is true', () => {
      render(<PlanApprovalPanel {...defaultProps} isLoading={true} />)

      expect(
        screen.getByRole('button', { name: /clear context & start implementation/i })
      ).toBeDisabled()
    })
  })

  describe('Continue with Context button', () => {
    it('calls onApproveKeepContext when clicked', async () => {
      const user = userEvent.setup()
      render(<PlanApprovalPanel {...defaultProps} />)

      await user.click(screen.getByRole('button', { name: /continue with context/i }))

      expect(defaultProps.onApproveKeepContext).toHaveBeenCalledTimes(1)
    })

    it('is disabled when isLoading is true', () => {
      render(<PlanApprovalPanel {...defaultProps} isLoading={true} />)

      expect(screen.getByRole('button', { name: /continue with context/i })).toBeDisabled()
    })
  })

  describe('Reject & Modify button', () => {
    it('shows feedback textarea when clicked', async () => {
      const user = userEvent.setup()
      render(<PlanApprovalPanel {...defaultProps} />)

      // Initially no textarea
      expect(screen.queryByRole('textbox')).not.toBeInTheDocument()

      await user.click(screen.getByRole('button', { name: /reject & modify/i }))

      // Now textarea should be visible
      expect(screen.getByRole('textbox')).toBeInTheDocument()
      expect(screen.getByPlaceholderText(/describe what changes/i)).toBeInTheDocument()
    })

    it('hides feedback textarea when clicked again', async () => {
      const user = userEvent.setup()
      render(<PlanApprovalPanel {...defaultProps} />)

      // Show textarea
      await user.click(screen.getByRole('button', { name: /reject & modify/i }))
      expect(screen.getByRole('textbox')).toBeInTheDocument()

      // Hide textarea
      await user.click(screen.getByRole('button', { name: /reject & modify/i }))
      expect(screen.queryByRole('textbox')).not.toBeInTheDocument()
    })

    it('is disabled when isLoading is true', () => {
      render(<PlanApprovalPanel {...defaultProps} isLoading={true} />)

      expect(screen.getByRole('button', { name: /reject & modify/i })).toBeDisabled()
    })
  })

  describe('Feedback form', () => {
    it('calls onReject with feedback when Send Feedback is clicked', async () => {
      const user = userEvent.setup()
      render(<PlanApprovalPanel {...defaultProps} />)

      // Show feedback form
      await user.click(screen.getByRole('button', { name: /reject & modify/i }))

      // Type feedback
      const textarea = screen.getByRole('textbox')
      await user.type(textarea, 'Please add more detail to step 3')

      // Submit feedback
      await user.click(screen.getByRole('button', { name: /send feedback/i }))

      expect(defaultProps.onReject).toHaveBeenCalledWith('Please add more detail to step 3')
    })

    it('calls onReject with empty string when no feedback entered', async () => {
      const user = userEvent.setup()
      render(<PlanApprovalPanel {...defaultProps} />)

      // Show feedback form
      await user.click(screen.getByRole('button', { name: /reject & modify/i }))

      // Submit without typing
      await user.click(screen.getByRole('button', { name: /send feedback/i }))

      expect(defaultProps.onReject).toHaveBeenCalledWith('')
    })

    it('disables Send Feedback button when isLoading is true', async () => {
      const user = userEvent.setup()
      const { rerender } = render(<PlanApprovalPanel {...defaultProps} />)

      // Show feedback form first
      await user.click(screen.getByRole('button', { name: /reject & modify/i }))

      // Verify textarea is visible
      expect(screen.getByRole('textbox')).toBeInTheDocument()

      // Re-render with loading
      rerender(<PlanApprovalPanel {...defaultProps} isLoading={true} />)

      // The button in the feedback area should be disabled
      const sendButton = screen.getByRole('button', { name: /send feedback/i })
      expect(sendButton).toBeDisabled()
    })
  })

  describe('Plan content display', () => {
    it('renders collapsible section to view plan', async () => {
      const user = userEvent.setup()
      render(<PlanApprovalPanel {...defaultProps} />)

      // Find and click the expand button
      const expandButton = screen.getByRole('button', { name: /view plan/i })
      expect(expandButton).toBeInTheDocument()

      await user.click(expandButton)

      // Plan content should be visible
      await waitFor(() => {
        expect(screen.getByText(/Step one/)).toBeInTheDocument()
      })
    })

    it('shows file path when available', async () => {
      const user = userEvent.setup()
      render(<PlanApprovalPanel {...defaultProps} />)

      await user.click(screen.getByRole('button', { name: /view plan/i }))

      expect(screen.getByText(/plan\.md/)).toBeInTheDocument()
    })

    it('does not show file path section when planFilePath is undefined', async () => {
      const user = userEvent.setup()
      render(<PlanApprovalPanel {...defaultProps} planFilePath={undefined} />)

      await user.click(screen.getByRole('button', { name: /view plan/i }))

      // Should still show plan content but not file path
      await waitFor(() => {
        expect(screen.getByText(/Step one/)).toBeInTheDocument()
      })
      expect(screen.queryByText(/plan\.md/)).not.toBeInTheDocument()
    })
  })

  describe('error display', () => {
    it('shows error message when error prop is provided', () => {
      render(<PlanApprovalPanel {...defaultProps} error="Connection failed" />)

      expect(screen.getByText('Connection failed')).toBeInTheDocument()
    })

    it('does not show error section when error is undefined', () => {
      render(<PlanApprovalPanel {...defaultProps} error={undefined} />)

      expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    })
  })
})
