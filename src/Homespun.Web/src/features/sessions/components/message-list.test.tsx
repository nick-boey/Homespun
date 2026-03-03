import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MessageList } from './message-list'
import type { ClaudeMessage } from '@/types/signalr'

// Mock the markdown component to avoid shiki async issues in tests
vi.mock('@/components/ui/markdown', () => ({
  Markdown: ({ children, className }: { children: string; className?: string }) => (
    <div data-testid="markdown" className={className}>
      {children}
    </div>
  ),
}))

const createMessage = (
  overrides: Partial<ClaudeMessage> & { id: string; role: ClaudeMessage['role'] }
): ClaudeMessage => ({
  sessionId: 'session-123',
  content: [{ type: 'Text', text: 'Test message', isStreaming: false, index: 0 }],
  createdAt: '2024-01-01T12:00:00Z',
  isStreaming: false,
  ...overrides,
})

describe('MessageList', () => {
  it('renders empty state when no messages', () => {
    render(<MessageList messages={[]} />)

    expect(screen.getByText(/no messages/i)).toBeInTheDocument()
  })

  it('renders user messages with right alignment', () => {
    const messages: ClaudeMessage[] = [
      createMessage({
        id: 'msg-1',
        role: 'User',
        content: [{ type: 'Text', text: 'Hello, Claude!', isStreaming: false, index: 0 }],
      }),
    ]

    render(<MessageList messages={messages} />)

    const messageElement = screen.getByTestId('message-msg-1')
    expect(messageElement).toHaveClass('justify-end')
    expect(screen.getByText('Hello, Claude!')).toBeInTheDocument()
  })

  it('renders assistant messages with left alignment', () => {
    const messages: ClaudeMessage[] = [
      createMessage({
        id: 'msg-1',
        role: 'Assistant',
        content: [{ type: 'Text', text: 'Hello! How can I help?', isStreaming: false, index: 0 }],
      }),
    ]

    render(<MessageList messages={messages} />)

    const messageElement = screen.getByTestId('message-msg-1')
    expect(messageElement).toHaveClass('justify-start')
  })

  it('renders markdown in assistant messages', () => {
    const messages: ClaudeMessage[] = [
      createMessage({
        id: 'msg-1',
        role: 'Assistant',
        content: [
          { type: 'Text', text: '**Bold** and *italic* text', isStreaming: false, index: 0 },
        ],
      }),
    ]

    render(<MessageList messages={messages} />)

    expect(screen.getByTestId('markdown')).toBeInTheDocument()
    expect(screen.getByText('**Bold** and *italic* text')).toBeInTheDocument()
  })

  it('renders multiple messages in order', () => {
    const messages: ClaudeMessage[] = [
      createMessage({
        id: 'msg-1',
        role: 'User',
        content: [{ type: 'Text', text: 'First message', isStreaming: false, index: 0 }],
      }),
      createMessage({
        id: 'msg-2',
        role: 'Assistant',
        content: [{ type: 'Text', text: 'Second message', isStreaming: false, index: 0 }],
      }),
      createMessage({
        id: 'msg-3',
        role: 'User',
        content: [{ type: 'Text', text: 'Third message', isStreaming: false, index: 0 }],
      }),
    ]

    render(<MessageList messages={messages} />)

    const renderedMessages = screen.getAllByTestId(/^message-msg-\d+$/)
    expect(renderedMessages).toHaveLength(3)
    expect(screen.getByText('First message')).toBeInTheDocument()
    expect(screen.getByText('Second message')).toBeInTheDocument()
    expect(screen.getByText('Third message')).toBeInTheDocument()
  })

  it('shows streaming indicator for streaming messages', () => {
    const messages: ClaudeMessage[] = [
      createMessage({
        id: 'msg-1',
        role: 'Assistant',
        content: [{ type: 'Text', text: 'Typing...', isStreaming: true, index: 0 }],
        isStreaming: true,
      }),
    ]

    render(<MessageList messages={messages} />)

    expect(screen.getByTestId('streaming-indicator')).toBeInTheDocument()
  })

  it('shows timestamp on hover', async () => {
    const user = userEvent.setup()
    const messages: ClaudeMessage[] = [
      createMessage({
        id: 'msg-1',
        role: 'User',
        content: [{ type: 'Text', text: 'Hello', isStreaming: false, index: 0 }],
        createdAt: '2024-01-01T12:00:00Z',
      }),
    ]

    render(<MessageList messages={messages} />)

    const messageElement = screen.getByTestId('message-msg-1')
    await user.hover(messageElement)

    // Timestamp should be visible on hover
    expect(screen.getByTestId('timestamp-msg-1')).toBeInTheDocument()
  })

  it('renders messages with multiple content blocks', () => {
    const messages: ClaudeMessage[] = [
      createMessage({
        id: 'msg-1',
        role: 'Assistant',
        content: [
          { type: 'Text', text: 'Here is some text', isStreaming: false, index: 0 },
          { type: 'Text', text: 'And more text', isStreaming: false, index: 1 },
        ],
      }),
    ]

    render(<MessageList messages={messages} />)

    expect(screen.getByText('Here is some text')).toBeInTheDocument()
    expect(screen.getByText('And more text')).toBeInTheDocument()
  })

  it('handles tool use content type gracefully', () => {
    const messages: ClaudeMessage[] = [
      createMessage({
        id: 'msg-1',
        role: 'Assistant',
        content: [
          { type: 'ToolUse', toolName: 'read_file', isStreaming: false, index: 0 },
          { type: 'Text', text: 'I read the file.', isStreaming: false, index: 1 },
        ],
      }),
    ]

    render(<MessageList messages={messages} />)

    // Tool use should be rendered (in some form)
    expect(screen.getByText(/read_file/i)).toBeInTheDocument()
    expect(screen.getByText('I read the file.')).toBeInTheDocument()
  })

  it('applies user message styling (primary color)', () => {
    const messages: ClaudeMessage[] = [
      createMessage({
        id: 'msg-1',
        role: 'User',
        content: [{ type: 'Text', text: 'User message', isStreaming: false, index: 0 }],
      }),
    ]

    render(<MessageList messages={messages} />)

    const contentElement = screen.getByTestId('message-content-msg-1')
    expect(contentElement).toHaveClass('bg-primary')
  })

  it('applies assistant message styling (neutral)', () => {
    const messages: ClaudeMessage[] = [
      createMessage({
        id: 'msg-1',
        role: 'Assistant',
        content: [{ type: 'Text', text: 'Assistant message', isStreaming: false, index: 0 }],
      }),
    ]

    render(<MessageList messages={messages} />)

    const contentElement = screen.getByTestId('message-content-msg-1')
    expect(contentElement).toHaveClass('bg-secondary')
  })

  it('shows loading skeleton when isLoading is true', () => {
    render(<MessageList messages={[]} isLoading />)

    expect(screen.getByTestId('message-list-loading')).toBeInTheDocument()
  })
})
