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

  // Tool result should appear on agent side (grouped with tool call)
  describe('tool result grouping', () => {
    it('renders tool result messages with assistant styling even when role is User', () => {
      const messages: ClaudeMessage[] = [
        createMessage({
          id: 'msg-1',
          role: 'Assistant',
          content: [
            {
              type: 'ToolUse',
              toolName: 'read_file',
              toolUseId: 'tool-1',
              isStreaming: false,
              index: 0,
            },
          ],
        }),
        createMessage({
          id: 'msg-2',
          role: 'User', // Tool results come with User role from backend
          content: [
            {
              type: 'ToolResult',
              toolResult: 'File contents',
              toolUseId: 'tool-1',
              isStreaming: false,
              index: 0,
            },
          ],
        }),
      ]

      render(<MessageList messages={messages} />)

      // The tool result message should be left-aligned (assistant side)
      const toolResultMessage = screen.getByTestId('message-msg-2')
      expect(toolResultMessage).toHaveClass('justify-start')
    })

    it('renders tool result messages with secondary background color', () => {
      const messages: ClaudeMessage[] = [
        createMessage({
          id: 'msg-1',
          role: 'User',
          content: [
            {
              type: 'ToolResult',
              toolResult: 'Success',
              toolUseId: 'tool-1',
              isStreaming: false,
              index: 0,
            },
          ],
        }),
      ]

      render(<MessageList messages={messages} />)

      const contentElement = screen.getByTestId('message-content-msg-1')
      expect(contentElement).toHaveClass('bg-secondary')
    })
  })

  // Tests for markdown rendering in user messages
  describe('user message markdown', () => {
    it('renders markdown in user messages', () => {
      const messages: ClaudeMessage[] = [
        createMessage({
          id: 'msg-1',
          role: 'User',
          content: [
            { type: 'Text', text: '# Heading\n**Bold** text', isStreaming: false, index: 0 },
          ],
        }),
      ]

      render(<MessageList messages={messages} />)

      // Markdown component should be used for user text messages
      expect(screen.getByTestId('markdown')).toBeInTheDocument()
    })
  })

  // Tests for responsive behavior
  describe('responsive behavior', () => {
    it('applies responsive prose classes to markdown content', () => {
      const messages: ClaudeMessage[] = [
        createMessage({
          id: 'msg-1',
          role: 'Assistant',
          content: [{ type: 'Text', text: '# Responsive heading', isStreaming: false, index: 0 }],
        }),
      ]

      render(<MessageList messages={messages} />)

      const markdownElement = screen.getByTestId('markdown')
      // The markdown component receives the responsive prose class from ContentBlock
      // For desktop tests (default), it should have 'prose' class
      expect(markdownElement).toHaveClass('prose')
      expect(markdownElement).toHaveClass('max-w-none')
      expect(markdownElement).toHaveClass('break-words')
    })

    it('does not apply prose-invert to user messages (colors inherit via currentColor)', () => {
      const messages: ClaudeMessage[] = [
        createMessage({
          id: 'msg-1',
          role: 'User',
          content: [
            {
              type: 'Text',
              text: 'User message with inherited prose colors',
              isStreaming: false,
              index: 0,
            },
          ],
        }),
      ]

      render(<MessageList messages={messages} />)

      const markdownElement = screen.getByTestId('markdown')
      expect(markdownElement).not.toHaveClass('prose-invert')
      expect(markdownElement).toHaveClass('prose')
    })

    it('uses responsive width classes for message bubbles', () => {
      const messages: ClaudeMessage[] = [
        createMessage({
          id: 'msg-1',
          role: 'User',
          content: [{ type: 'Text', text: 'Test message', isStreaming: false, index: 0 }],
        }),
      ]

      render(<MessageList messages={messages} />)

      // Find the div that contains the max-width classes
      const messageElement = screen.getByTestId('message-msg-1')
      const bubbleContainer = messageElement.querySelector('div[class*="max-w-"]')

      expect(bubbleContainer).toBeInTheDocument()
      // Check for responsive width classes
      expect(bubbleContainer).toHaveClass('max-w-[90%]')
      expect(bubbleContainer).toHaveClass('md:max-w-[80%]')
    })
  })

  // Tests for handling numeric enum values from backend
  describe('numeric enum handling', () => {
    it('renders text content when type is numeric 0 (Text)', () => {
      const messages: ClaudeMessage[] = [
        createMessage({
          id: 'msg-1',
          role: 'Assistant',
          content: [
            // @ts-expect-error - testing numeric enum from backend
            { type: 0, text: 'Message with numeric type', isStreaming: false, index: 0 },
          ],
        }),
      ]

      render(<MessageList messages={messages} />)

      expect(screen.getByText('Message with numeric type')).toBeInTheDocument()
    })

    it('renders thinking content when type is numeric 1 (Thinking)', () => {
      const messages: ClaudeMessage[] = [
        createMessage({
          id: 'msg-1',
          role: 'Assistant',
          content: [
            // @ts-expect-error - testing numeric enum from backend
            { type: 1, thinking: 'Thinking about something', isStreaming: false, index: 0 },
          ],
        }),
      ]

      render(<MessageList messages={messages} />)

      expect(screen.getByText('Thinking about something')).toBeInTheDocument()
    })

    it('renders tool use content when type is numeric 2 (ToolUse)', () => {
      const messages: ClaudeMessage[] = [
        createMessage({
          id: 'msg-1',
          role: 'Assistant',
          content: [
            // @ts-expect-error - testing numeric enum from backend
            { type: 2, toolName: 'write_file', isStreaming: false, index: 0 },
          ],
        }),
      ]

      render(<MessageList messages={messages} />)

      expect(screen.getByText(/write_file/)).toBeInTheDocument()
    })

    it('renders tool result content when type is numeric 3 (ToolResult)', () => {
      const messages: ClaudeMessage[] = [
        createMessage({
          id: 'msg-1',
          role: 'Assistant',
          content: [
            // @ts-expect-error - testing numeric enum from backend
            { type: 3, toolResult: 'Success', isStreaming: false, index: 0 },
          ],
        }),
      ]

      render(<MessageList messages={messages} />)

      expect(screen.getByText(/Tool result/)).toBeInTheDocument()
    })

    it('renders user message correctly when role is numeric 0 (User)', () => {
      const messages: ClaudeMessage[] = [
        createMessage({
          id: 'msg-1',
          // @ts-expect-error - testing numeric enum from backend
          role: 0,
          content: [
            { type: 'Text', text: 'User message with numeric role', isStreaming: false, index: 0 },
          ],
        }),
      ]

      render(<MessageList messages={messages} />)

      const messageElement = screen.getByTestId('message-msg-1')
      expect(messageElement).toHaveClass('justify-end') // User messages are right-aligned
      expect(screen.getByText('User message with numeric role')).toBeInTheDocument()
    })

    it('renders assistant message correctly when role is numeric 1 (Assistant)', () => {
      const messages: ClaudeMessage[] = [
        createMessage({
          id: 'msg-1',
          // @ts-expect-error - testing numeric enum from backend
          role: 1,
          content: [
            {
              type: 'Text',
              text: 'Assistant message with numeric role',
              isStreaming: false,
              index: 0,
            },
          ],
        }),
      ]

      render(<MessageList messages={messages} />)

      const messageElement = screen.getByTestId('message-msg-1')
      expect(messageElement).toHaveClass('justify-start') // Assistant messages are left-aligned
    })

    it('handles mixed numeric and string enum values', () => {
      const messages: ClaudeMessage[] = [
        createMessage({
          id: 'msg-1',
          // @ts-expect-error - testing numeric enum from backend
          role: 1, // numeric Assistant
          content: [
            // @ts-expect-error - testing numeric enum from backend
            { type: 0, text: 'First part', isStreaming: false, index: 0 }, // numeric Text
            { type: 'ToolUse', toolName: 'test_tool', isStreaming: false, index: 1 }, // string ToolUse
          ],
        }),
      ]

      render(<MessageList messages={messages} />)

      expect(screen.getByText('First part')).toBeInTheDocument()
      expect(screen.getByText(/test_tool/)).toBeInTheDocument()
    })
  })
})
