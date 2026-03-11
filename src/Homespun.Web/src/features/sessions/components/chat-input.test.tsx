import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ChatInput } from './chat-input'

describe('ChatInput', () => {
  const mockOnSend = vi.fn()
  const mockOnModeChange = vi.fn()
  const mockOnModelChange = vi.fn()

  const defaultProps = {
    onSend: mockOnSend,
    sessionMode: 'Build' as const,
    sessionModel: 'opus' as const,
    onModeChange: mockOnModeChange,
    onModelChange: mockOnModelChange,
  }

  beforeEach(() => {
    vi.clearAllMocks()
  })

  describe('rendering', () => {
    it('renders the textarea input', () => {
      render(<ChatInput {...defaultProps} />)

      expect(screen.getByPlaceholderText(/message/i)).toBeInTheDocument()
    })

    it('renders the send button', () => {
      render(<ChatInput {...defaultProps} />)

      expect(screen.getByRole('button', { name: /send/i })).toBeInTheDocument()
    })

    it('renders the session mode toggle button', () => {
      render(<ChatInput {...defaultProps} />)

      expect(screen.getByRole('button', { name: /toggle session mode/i })).toBeInTheDocument()
      expect(screen.getByText('Build')).toBeInTheDocument()
    })

    it('renders the model selector', () => {
      render(<ChatInput {...defaultProps} />)

      expect(screen.getByRole('button', { name: /model/i })).toBeInTheDocument()
    })
  })

  describe('sending messages', () => {
    it('calls onSend with message when send button is clicked', async () => {
      const user = userEvent.setup()
      render(<ChatInput {...defaultProps} />)

      const input = screen.getByPlaceholderText(/message/i)
      await user.type(input, 'Hello Claude')
      await user.click(screen.getByRole('button', { name: /send/i }))

      expect(mockOnSend).toHaveBeenCalledWith('Hello Claude', 'Build', 'opus')
    })

    it('calls onSend with message when Enter is pressed', async () => {
      const user = userEvent.setup()
      render(<ChatInput {...defaultProps} />)

      const input = screen.getByPlaceholderText(/message/i)
      await user.type(input, 'Hello Claude{Enter}')

      expect(mockOnSend).toHaveBeenCalledWith('Hello Claude', 'Build', 'opus')
    })

    it('does not send when Shift+Enter is pressed (adds new line)', async () => {
      const user = userEvent.setup()
      render(<ChatInput {...defaultProps} />)

      const input = screen.getByPlaceholderText(/message/i)
      await user.type(input, 'Line 1{Shift>}{Enter}{/Shift}Line 2')

      expect(mockOnSend).not.toHaveBeenCalled()
      expect(input).toHaveValue('Line 1\nLine 2')
    })

    it('clears input after sending', async () => {
      const user = userEvent.setup()
      render(<ChatInput {...defaultProps} />)

      const input = screen.getByPlaceholderText(/message/i)
      await user.type(input, 'Hello Claude{Enter}')

      expect(input).toHaveValue('')
    })

    it('does not send empty messages', async () => {
      const user = userEvent.setup()
      render(<ChatInput {...defaultProps} />)

      await user.click(screen.getByRole('button', { name: /send/i }))

      expect(mockOnSend).not.toHaveBeenCalled()
    })

    it('does not send whitespace-only messages', async () => {
      const user = userEvent.setup()
      render(<ChatInput {...defaultProps} />)

      const input = screen.getByPlaceholderText(/message/i)
      await user.type(input, '   {Enter}')

      expect(mockOnSend).not.toHaveBeenCalled()
    })
  })

  describe('disabled state', () => {
    it('disables the textarea when disabled prop is true', () => {
      render(<ChatInput {...defaultProps} disabled />)

      expect(screen.getByPlaceholderText(/message/i)).toBeDisabled()
    })

    it('disables the send button when disabled', () => {
      render(<ChatInput {...defaultProps} disabled />)

      expect(screen.getByRole('button', { name: /send/i })).toBeDisabled()
    })

    it('does not send messages when disabled', async () => {
      const user = userEvent.setup()
      render(<ChatInput {...defaultProps} disabled />)

      const input = screen.getByPlaceholderText(/message/i)
      await user.type(input, 'Hello Claude', { skipClick: true })

      expect(mockOnSend).not.toHaveBeenCalled()
    })
  })

  describe('loading state', () => {
    it('shows loading indicator when isLoading is true', () => {
      render(<ChatInput {...defaultProps} isLoading />)

      expect(screen.getByTestId('send-loading')).toBeInTheDocument()
    })
  })

  describe('session mode toggle', () => {
    it('shows Build mode from props', () => {
      render(<ChatInput {...defaultProps} sessionMode="Build" />)

      const toggleButton = screen.getByRole('button', { name: /toggle session mode/i })
      expect(toggleButton).toHaveTextContent('Build')
    })

    it('shows Plan mode from props', () => {
      render(<ChatInput {...defaultProps} sessionMode="Plan" />)

      const toggleButton = screen.getByRole('button', { name: /toggle session mode/i })
      expect(toggleButton).toHaveTextContent('Plan')
    })

    it('toggles to Plan mode when clicked from Build', async () => {
      const user = userEvent.setup()
      render(<ChatInput {...defaultProps} sessionMode="Build" />)

      await user.click(screen.getByRole('button', { name: /toggle session mode/i }))

      expect(mockOnModeChange).toHaveBeenCalledWith('Plan')
    })

    it('toggles to Build mode when clicked from Plan', async () => {
      const user = userEvent.setup()
      render(<ChatInput {...defaultProps} sessionMode="Plan" />)

      await user.click(screen.getByRole('button', { name: /toggle session mode/i }))

      expect(mockOnModeChange).toHaveBeenCalledWith('Build')
    })

    it('sends message with current session mode from props', async () => {
      const user = userEvent.setup()
      render(<ChatInput {...defaultProps} sessionMode="Plan" />)

      const input = screen.getByPlaceholderText(/message/i)
      await user.type(input, 'Hello{Enter}')

      expect(mockOnSend).toHaveBeenCalledWith('Hello', 'Plan', 'opus')
    })
  })

  describe('keyboard shortcuts', () => {
    it('toggles from Build to Plan with Shift+Tab in textarea', async () => {
      const user = userEvent.setup()
      render(<ChatInput {...defaultProps} sessionMode="Build" />)

      const input = screen.getByPlaceholderText(/message/i)
      await user.click(input)
      await user.keyboard('{Shift>}{Tab}{/Shift}')

      expect(mockOnModeChange).toHaveBeenCalledWith('Plan')
    })

    it('toggles from Plan to Build with Shift+Tab in textarea', async () => {
      const user = userEvent.setup()
      render(<ChatInput {...defaultProps} sessionMode="Plan" />)

      const input = screen.getByPlaceholderText(/message/i)
      await user.click(input)
      await user.keyboard('{Shift>}{Tab}{/Shift}')

      expect(mockOnModeChange).toHaveBeenCalledWith('Build')
    })
  })

  describe('model selector', () => {
    it('displays opus model from props', () => {
      render(<ChatInput {...defaultProps} sessionModel="opus" />)

      expect(screen.getByRole('button', { name: /model/i })).toHaveTextContent(/opus/i)
    })

    it('displays sonnet model from props', () => {
      render(<ChatInput {...defaultProps} sessionModel="sonnet" />)

      expect(screen.getByRole('button', { name: /model/i })).toHaveTextContent(/sonnet/i)
    })

    it('displays haiku model from props', () => {
      render(<ChatInput {...defaultProps} sessionModel="haiku" />)

      expect(screen.getByRole('button', { name: /model/i })).toHaveTextContent(/haiku/i)
    })

    it('calls onModelChange when sonnet is selected', async () => {
      const user = userEvent.setup()
      render(<ChatInput {...defaultProps} sessionModel="opus" />)

      await user.click(screen.getByRole('button', { name: /model/i }))
      await user.click(screen.getByRole('menuitem', { name: /sonnet/i }))

      expect(mockOnModelChange).toHaveBeenCalledWith('sonnet')
    })

    it('calls onModelChange when haiku is selected', async () => {
      const user = userEvent.setup()
      render(<ChatInput {...defaultProps} sessionModel="opus" />)

      await user.click(screen.getByRole('button', { name: /model/i }))
      await user.click(screen.getByRole('menuitem', { name: /haiku/i }))

      expect(mockOnModelChange).toHaveBeenCalledWith('haiku')
    })

    it('sends message with current model from props', async () => {
      const user = userEvent.setup()
      render(<ChatInput {...defaultProps} sessionModel="sonnet" />)

      const input = screen.getByPlaceholderText(/message/i)
      await user.type(input, 'Hello{Enter}')

      expect(mockOnSend).toHaveBeenCalledWith('Hello', 'Build', 'sonnet')
    })
  })
})
