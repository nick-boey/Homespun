import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { ChatInput } from './chat-input'
import { useChatInputStore } from '@/stores/chat-input-store'

describe('ChatInput', () => {
  const mockOnSend = vi.fn()

  beforeEach(() => {
    vi.clearAllMocks()
    // Reset the store to defaults
    useChatInputStore.setState({
      sessionMode: 'Build',
      model: 'opus',
    })
  })

  describe('rendering', () => {
    it('renders the textarea input', () => {
      render(<ChatInput onSend={mockOnSend} />)

      expect(screen.getByPlaceholderText(/message/i)).toBeInTheDocument()
    })

    it('renders the send button', () => {
      render(<ChatInput onSend={mockOnSend} />)

      expect(screen.getByRole('button', { name: /send/i })).toBeInTheDocument()
    })

    it('renders the session mode selector', () => {
      render(<ChatInput onSend={mockOnSend} />)

      expect(screen.getByRole('button', { name: /session mode/i })).toBeInTheDocument()
    })

    it('renders the model selector', () => {
      render(<ChatInput onSend={mockOnSend} />)

      expect(screen.getByRole('button', { name: /model/i })).toBeInTheDocument()
    })
  })

  describe('sending messages', () => {
    it('calls onSend with message when send button is clicked', async () => {
      const user = userEvent.setup()
      render(<ChatInput onSend={mockOnSend} />)

      const input = screen.getByPlaceholderText(/message/i)
      await user.type(input, 'Hello Claude')
      await user.click(screen.getByRole('button', { name: /send/i }))

      expect(mockOnSend).toHaveBeenCalledWith('Hello Claude', 'Build', 'opus')
    })

    it('calls onSend with message when Enter is pressed', async () => {
      const user = userEvent.setup()
      render(<ChatInput onSend={mockOnSend} />)

      const input = screen.getByPlaceholderText(/message/i)
      await user.type(input, 'Hello Claude{Enter}')

      expect(mockOnSend).toHaveBeenCalledWith('Hello Claude', 'Build', 'opus')
    })

    it('does not send when Shift+Enter is pressed (adds new line)', async () => {
      const user = userEvent.setup()
      render(<ChatInput onSend={mockOnSend} />)

      const input = screen.getByPlaceholderText(/message/i)
      await user.type(input, 'Line 1{Shift>}{Enter}{/Shift}Line 2')

      expect(mockOnSend).not.toHaveBeenCalled()
      expect(input).toHaveValue('Line 1\nLine 2')
    })

    it('clears input after sending', async () => {
      const user = userEvent.setup()
      render(<ChatInput onSend={mockOnSend} />)

      const input = screen.getByPlaceholderText(/message/i)
      await user.type(input, 'Hello Claude{Enter}')

      expect(input).toHaveValue('')
    })

    it('does not send empty messages', async () => {
      const user = userEvent.setup()
      render(<ChatInput onSend={mockOnSend} />)

      await user.click(screen.getByRole('button', { name: /send/i }))

      expect(mockOnSend).not.toHaveBeenCalled()
    })

    it('does not send whitespace-only messages', async () => {
      const user = userEvent.setup()
      render(<ChatInput onSend={mockOnSend} />)

      const input = screen.getByPlaceholderText(/message/i)
      await user.type(input, '   {Enter}')

      expect(mockOnSend).not.toHaveBeenCalled()
    })
  })

  describe('disabled state', () => {
    it('disables the textarea when disabled prop is true', () => {
      render(<ChatInput onSend={mockOnSend} disabled />)

      expect(screen.getByPlaceholderText(/message/i)).toBeDisabled()
    })

    it('disables the send button when disabled', () => {
      render(<ChatInput onSend={mockOnSend} disabled />)

      expect(screen.getByRole('button', { name: /send/i })).toBeDisabled()
    })

    it('does not send messages when disabled', async () => {
      const user = userEvent.setup()
      render(<ChatInput onSend={mockOnSend} disabled />)

      const input = screen.getByPlaceholderText(/message/i)
      await user.type(input, 'Hello Claude', { skipClick: true })

      expect(mockOnSend).not.toHaveBeenCalled()
    })
  })

  describe('loading state', () => {
    it('shows loading indicator when isLoading is true', () => {
      render(<ChatInput onSend={mockOnSend} isLoading />)

      expect(screen.getByTestId('send-loading')).toBeInTheDocument()
    })
  })

  describe('session mode selector', () => {
    it('displays Build mode initially', () => {
      render(<ChatInput onSend={mockOnSend} />)

      expect(screen.getByRole('button', { name: /session mode/i })).toHaveTextContent(/build/i)
    })

    it('can select Plan mode', async () => {
      const user = userEvent.setup()
      render(<ChatInput onSend={mockOnSend} />)

      await user.click(screen.getByRole('button', { name: /session mode/i }))
      await user.click(screen.getByRole('menuitem', { name: /plan mode/i }))

      expect(useChatInputStore.getState().sessionMode).toBe('Plan')
    })

    it('can select Build mode from Plan mode', async () => {
      const user = userEvent.setup()
      useChatInputStore.setState({ sessionMode: 'Plan' })
      render(<ChatInput onSend={mockOnSend} />)

      await user.click(screen.getByRole('button', { name: /session mode/i }))
      await user.click(screen.getByRole('menuitem', { name: /build mode/i }))

      expect(useChatInputStore.getState().sessionMode).toBe('Build')
    })

    it('sends message with selected session mode', async () => {
      const user = userEvent.setup()
      render(<ChatInput onSend={mockOnSend} />)

      // Select Plan mode
      await user.click(screen.getByRole('button', { name: /session mode/i }))
      await user.click(screen.getByRole('menuitem', { name: /plan mode/i }))

      // Send message
      const input = screen.getByPlaceholderText(/message/i)
      await user.type(input, 'Hello{Enter}')

      expect(mockOnSend).toHaveBeenCalledWith('Hello', 'Plan', 'opus')
    })
  })

  describe('model selector', () => {
    it('displays opus model initially', () => {
      render(<ChatInput onSend={mockOnSend} />)

      expect(screen.getByRole('button', { name: /model/i })).toHaveTextContent(/opus/i)
    })

    it('can select sonnet model', async () => {
      const user = userEvent.setup()
      render(<ChatInput onSend={mockOnSend} />)

      await user.click(screen.getByRole('button', { name: /model/i }))
      await user.click(screen.getByRole('menuitem', { name: /sonnet/i }))

      expect(useChatInputStore.getState().model).toBe('sonnet')
    })

    it('can select haiku model', async () => {
      const user = userEvent.setup()
      render(<ChatInput onSend={mockOnSend} />)

      await user.click(screen.getByRole('button', { name: /model/i }))
      await user.click(screen.getByRole('menuitem', { name: /haiku/i }))

      expect(useChatInputStore.getState().model).toBe('haiku')
    })

    it('sends message with selected model', async () => {
      const user = userEvent.setup()
      render(<ChatInput onSend={mockOnSend} />)

      // Select sonnet model
      await user.click(screen.getByRole('button', { name: /model/i }))
      await user.click(screen.getByRole('menuitem', { name: /sonnet/i }))

      // Send message
      const input = screen.getByPlaceholderText(/message/i)
      await user.type(input, 'Hello{Enter}')

      expect(mockOnSend).toHaveBeenCalledWith('Hello', 'Build', 'sonnet')
    })
  })

  describe('persisted state', () => {
    it('uses persisted session mode from store', () => {
      useChatInputStore.setState({ sessionMode: 'Plan' })
      render(<ChatInput onSend={mockOnSend} />)

      expect(screen.getByRole('button', { name: /session mode/i })).toHaveTextContent(/plan/i)
    })

    it('uses persisted model from store', () => {
      useChatInputStore.setState({ model: 'haiku' })
      render(<ChatInput onSend={mockOnSend} />)

      expect(screen.getByRole('button', { name: /model/i })).toHaveTextContent(/haiku/i)
    })
  })
})
