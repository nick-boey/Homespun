import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Sessions } from '@/api'
import { toast } from 'sonner'

// Import first to get the actual component
import './sessions.$sessionId'

// Mock all dependencies
vi.mock('@tanstack/react-router', () => ({
  createFileRoute: vi.fn((path: string) => {
    return (config: { component: React.ComponentType }) => ({
      path,
      component: config.component,
    })
  }),
  useParams: () => ({ sessionId: 'test-session-id' }),
  Link: ({ children, to }: { children: React.ReactNode; to: string }) => (
    <a href={to}>{children}</a>
  ),
}))

vi.mock('@/api', () => ({
  Sessions: {
    postApiSessionsByIdMessages: vi.fn(),
  },
}))

vi.mock('sonner', () => ({
  toast: {
    error: vi.fn(),
  },
}))

vi.mock('@/providers/signalr-provider', () => ({
  useClaudeCodeHub: vi.fn(() => ({
    isConnected: true,
    methods: null,
    connectionStatus: 'connected',
    error: null,
  })),
}))

vi.mock('@/features/sessions', () => ({
  useSession: vi.fn(),
  useSessionMessages: vi.fn(() => ({ messages: [], addUserMessage: vi.fn() })),
  MessageList: vi.fn(() => <div data-testid="message-list" />),
  ChatInput: vi.fn(),
  usePlanApproval: vi.fn(() => ({ hasPendingPlan: false })),
  useApprovePlan: vi.fn(() => ({
    approveClearContext: vi.fn(),
    approveKeepContext: vi.fn(),
    reject: vi.fn(),
  })),
  PlanApprovalPanel: vi.fn(() => null),
}))

vi.mock('@/features/questions', () => ({
  useAnswerQuestion: vi.fn(() => ({ answerQuestion: vi.fn(), isSubmittingAnswer: false })),
}))

vi.mock('@/hooks/use-breadcrumbs', () => ({
  useBreadcrumbSetter: vi.fn(),
}))

vi.mock('@/components/ui/scroll-to-bottom', () => ({
  ScrollToBottom: vi.fn(() => null),
}))

// Import the component directly
import { Route } from './sessions.$sessionId'

// Extract the component from the route
// eslint-disable-next-line @typescript-eslint/no-explicit-any
const SessionChat = (Route as any).component as React.ComponentType

describe('SessionChat - Message Sending', () => {
  const mockSessionsAPI = vi.mocked(Sessions)
  const mockToast = vi.mocked(toast)
  const mockAddUserMessage = vi.fn()
  const mockSession = {
    id: 'test-session-id',
    entityId: 'entity-123',
    projectId: 'project-123',
    workingDirectory: '/test/dir',
    model: 'claude-3-5-sonnet',
    mode: 'Build' as const,
    status: 'WaitingForInput' as const,
    createdAt: '2024-01-01T00:00:00Z',
    lastActivityAt: '2024-01-01T00:00:00Z',
    messages: [],
    totalCostUsd: 0,
    totalDurationMs: 0,
    hasPendingPlanApproval: false,
    contextClearMarkers: [],
  }

  beforeEach(async () => {
    vi.clearAllMocks()
    mockAddUserMessage.mockClear()

    // Set up connected state by default
    const { useClaudeCodeHub } = vi.mocked(await import('@/providers/signalr-provider'))
    useClaudeCodeHub.mockReturnValue({
      isConnected: true,
      methods: null,
      status: 'connected',
      error: undefined,
      connection: null,
      isReconnecting: false,
    })

    const { useSession, ChatInput, useSessionMessages } = vi.mocked(
      await import('@/features/sessions')
    )

    // Mock useSessionMessages to return addUserMessage
    useSessionMessages.mockReturnValue({
      messages: [],
      addUserMessage: mockAddUserMessage,
    })

    // Mock useSession to return a valid session
    useSession.mockReturnValue({
      session: mockSession,
      isLoading: false,
      isNotFound: false,
      error: undefined,
      refetch: vi.fn(),
    })

    // Mock ChatInput to capture onSend callback
    ChatInput.mockImplementation(({ onSend, disabled, placeholder, isLoading }) => (
      <div data-testid="chat-input">
        <button
          data-testid="send-button"
          onClick={() => onSend('Test message', 'Build', 'opus')}
          disabled={disabled || isLoading}
        >
          Send
        </button>
        <span data-testid="placeholder">{placeholder}</span>
        {isLoading && <span data-testid="loading">Loading...</span>}
      </div>
    ))
  })

  it('should send message successfully via HTTP API', async () => {
    const user = userEvent.setup()
    mockSessionsAPI.postApiSessionsByIdMessages = vi.fn().mockResolvedValueOnce({
      data: {},
      error: undefined,
    })

    render(<SessionChat />)

    const sendButton = screen.getByTestId('send-button')
    await user.click(sendButton)

    // Verify API was called with correct parameters
    await waitFor(() => {
      expect(mockSessionsAPI.postApiSessionsByIdMessages).toHaveBeenCalledWith({
        path: { id: 'test-session-id' },
        body: { message: 'Test message', mode: 1 }, // Build mode = 1
      })
    })

    // Verify no error was shown
    expect(mockToast.error).not.toHaveBeenCalled()
  })

  it('should show error toast when session not found (404)', async () => {
    const user = userEvent.setup()
    mockSessionsAPI.postApiSessionsByIdMessages = vi.fn().mockRejectedValueOnce({
      status: 404,
      error: 'Not Found',
    })

    render(<SessionChat />)

    const sendButton = screen.getByTestId('send-button')
    await user.click(sendButton)

    await waitFor(() => {
      expect(mockToast.error).toHaveBeenCalledWith('Session not found')
    })
  })

  it('should show generic error toast for network errors', async () => {
    const user = userEvent.setup()
    mockSessionsAPI.postApiSessionsByIdMessages = vi
      .fn()
      .mockRejectedValueOnce(new Error('Network error'))

    render(<SessionChat />)

    const sendButton = screen.getByTestId('send-button')
    await user.click(sendButton)

    await waitFor(() => {
      expect(mockToast.error).toHaveBeenCalledWith('Failed to send message')
    })
  })

  it('should disable send button when not connected', async () => {
    const { useClaudeCodeHub } = vi.mocked(await import('@/providers/signalr-provider'))
    useClaudeCodeHub.mockReturnValue({
      isConnected: false,
      methods: null,
      status: 'disconnected',
      error: undefined,
      connection: null,
      isReconnecting: false,
    })

    render(<SessionChat />)

    const sendButton = screen.getByTestId('send-button')
    expect(sendButton).toBeDisabled()
  })

  it('should disable send button when session is processing', async () => {
    const { useSession } = vi.mocked(await import('@/features/sessions'))
    useSession.mockReturnValue({
      session: { ...mockSession, status: 'Running', mode: 'Build' as const },
      isLoading: false,
      isNotFound: false,
      error: undefined,
      refetch: vi.fn(),
    })

    render(<SessionChat />)

    const sendButton = screen.getByTestId('send-button')
    expect(sendButton).toBeDisabled()
  })

  it('should disable send during message sending', async () => {
    const user = userEvent.setup()
    let resolvePromise: () => void
    const sendPromise = new Promise<void>((resolve) => {
      resolvePromise = resolve
    })

    mockSessionsAPI.postApiSessionsByIdMessages = vi.fn().mockReturnValueOnce(sendPromise)

    render(<SessionChat />)

    const sendButton = screen.getByTestId('send-button')

    // Initially enabled (connected)
    expect(sendButton).not.toBeDisabled()

    await user.click(sendButton)

    // Should show loading state
    await waitFor(() => {
      expect(screen.queryByTestId('loading')).toBeInTheDocument()
    })

    // Button should be disabled while sending
    expect(sendButton).toBeDisabled()

    // Resolve the promise
    resolvePromise!()

    await waitFor(() => {
      expect(screen.queryByTestId('loading')).not.toBeInTheDocument()
      expect(sendButton).not.toBeDisabled()
    })
  })

  it('should send Plan mode as 0', async () => {
    const user = userEvent.setup()
    mockSessionsAPI.postApiSessionsByIdMessages = vi.fn().mockResolvedValueOnce({
      data: {},
      error: undefined,
    })

    // Mock ChatInput to send Plan mode
    const { ChatInput } = vi.mocked(await import('@/features/sessions'))
    ChatInput.mockImplementation(({ onSend, disabled, placeholder, isLoading }) => (
      <div data-testid="chat-input">
        <button
          data-testid="send-button"
          onClick={() => onSend('Test message', 'Plan', 'opus')}
          disabled={disabled || isLoading}
        >
          Send
        </button>
        <span data-testid="placeholder">{placeholder}</span>
        {isLoading && <span data-testid="loading">Loading...</span>}
      </div>
    ))

    render(<SessionChat />)

    const sendButton = screen.getByTestId('send-button')
    await user.click(sendButton)

    // Verify API was called with Plan mode as 0
    await waitFor(() => {
      expect(mockSessionsAPI.postApiSessionsByIdMessages).toHaveBeenCalledWith({
        path: { id: 'test-session-id' },
        body: { message: 'Test message', mode: 0 },
      })
    })

    expect(mockToast.error).not.toHaveBeenCalled()
  })

  it('should send Build mode as 1', async () => {
    const user = userEvent.setup()
    mockSessionsAPI.postApiSessionsByIdMessages = vi.fn().mockResolvedValueOnce({
      data: {},
      error: undefined,
    })

    // Mock ChatInput to send Build mode
    const { ChatInput } = vi.mocked(await import('@/features/sessions'))
    ChatInput.mockImplementation(({ onSend, disabled, placeholder, isLoading }) => (
      <div data-testid="chat-input">
        <button
          data-testid="send-button"
          onClick={() => onSend('Test message', 'Build', 'opus')}
          disabled={disabled || isLoading}
        >
          Send
        </button>
        <span data-testid="placeholder">{placeholder}</span>
        {isLoading && <span data-testid="loading">Loading...</span>}
      </div>
    ))

    render(<SessionChat />)

    const sendButton = screen.getByTestId('send-button')
    await user.click(sendButton)

    // Verify API was called with Build mode as 1
    await waitFor(() => {
      expect(mockSessionsAPI.postApiSessionsByIdMessages).toHaveBeenCalledWith({
        path: { id: 'test-session-id' },
        body: { message: 'Test message', mode: 1 },
      })
    })

    expect(mockToast.error).not.toHaveBeenCalled()
  })

  it('should show appropriate placeholder text when not connected', async () => {
    const { useClaudeCodeHub } = vi.mocked(await import('@/providers/signalr-provider'))
    useClaudeCodeHub.mockReturnValue({
      isConnected: false,
      methods: null,
      status: 'connecting',
      error: undefined,
      connection: null,
      isReconnecting: false,
    })

    render(<SessionChat />)

    const placeholder = screen.getByTestId('placeholder')
    expect(placeholder).toHaveTextContent('Connecting...')
  })

  it('should show appropriate placeholder text when processing', async () => {
    const { useSession } = vi.mocked(await import('@/features/sessions'))
    const { useClaudeCodeHub } = vi.mocked(await import('@/providers/signalr-provider'))

    // Ensure we're connected
    useClaudeCodeHub.mockReturnValue({
      isConnected: true,
      methods: null,
      status: 'connected',
      error: undefined,
      connection: null,
      isReconnecting: false,
    })

    // Set session as processing
    useSession.mockReturnValue({
      session: { ...mockSession, status: 'Running', mode: 'Build' as const },
      isLoading: false,
      isNotFound: false,
      error: undefined,
      refetch: vi.fn(),
    })

    render(<SessionChat />)

    const placeholder = screen.getByTestId('placeholder')
    expect(placeholder).toHaveTextContent('Processing...')
  })

  it('should call addUserMessage optimistically when sending a message', async () => {
    const user = userEvent.setup()
    mockSessionsAPI.postApiSessionsByIdMessages = vi.fn().mockResolvedValueOnce({
      data: {},
      error: undefined,
    })

    render(<SessionChat />)

    const sendButton = screen.getByTestId('send-button')
    await user.click(sendButton)

    // Verify addUserMessage was called with the message text
    expect(mockAddUserMessage).toHaveBeenCalledWith('Test message')
    expect(mockAddUserMessage).toHaveBeenCalledTimes(1)

    // Verify API was also called
    await waitFor(() => {
      expect(mockSessionsAPI.postApiSessionsByIdMessages).toHaveBeenCalledWith({
        path: { id: 'test-session-id' },
        body: { message: 'Test message', mode: 1 }, // Build mode = 1
      })
    })
  })

  it('should add user message before API call for better UX', async () => {
    const user = userEvent.setup()
    const callOrder: string[] = []

    // Track order of calls
    mockAddUserMessage.mockImplementation(() => {
      callOrder.push('addUserMessage')
    })

    mockSessionsAPI.postApiSessionsByIdMessages = vi.fn().mockImplementation(() => {
      callOrder.push('apiCall')
      return Promise.resolve({ data: {}, error: undefined })
    })

    render(<SessionChat />)

    const sendButton = screen.getByTestId('send-button')
    await user.click(sendButton)

    // Verify addUserMessage was called before the API
    expect(callOrder).toEqual(['addUserMessage', 'apiCall'])
  })

  it('should keep user message visible even if API call fails', async () => {
    const user = userEvent.setup()
    mockSessionsAPI.postApiSessionsByIdMessages = vi
      .fn()
      .mockRejectedValueOnce(new Error('Network error'))

    render(<SessionChat />)

    const sendButton = screen.getByTestId('send-button')
    await user.click(sendButton)

    // Verify addUserMessage was called
    expect(mockAddUserMessage).toHaveBeenCalledWith('Test message')

    // Wait for error to be shown
    await waitFor(() => {
      expect(mockToast.error).toHaveBeenCalledWith('Failed to send message')
    })

    // Verify addUserMessage was still called (message stays visible)
    expect(mockAddUserMessage).toHaveBeenCalledTimes(1)
  })

  it('should handle multiple rapid messages with unique optimistic updates', async () => {
    const user = userEvent.setup()
    let messageCount = 0

    // Mock ChatInput to allow different messages
    const { ChatInput } = vi.mocked(await import('@/features/sessions'))
    ChatInput.mockImplementation(({ onSend, disabled, placeholder, isLoading }) => (
      <div data-testid="chat-input">
        <button
          data-testid="send-button"
          onClick={() => {
            messageCount++
            onSend(`Message ${messageCount}`, 'Build', 'opus')
          }}
          disabled={disabled || isLoading}
        >
          Send
        </button>
        <span data-testid="placeholder">{placeholder}</span>
        {isLoading && <span data-testid="loading">Loading...</span>}
      </div>
    ))

    mockSessionsAPI.postApiSessionsByIdMessages = vi.fn().mockResolvedValue({
      data: {},
      error: undefined,
    })

    render(<SessionChat />)

    const sendButton = screen.getByTestId('send-button')

    // Send multiple messages rapidly
    await user.click(sendButton)
    await user.click(sendButton)
    await user.click(sendButton)

    // Verify addUserMessage was called for each message
    expect(mockAddUserMessage).toHaveBeenCalledWith('Message 1')
    expect(mockAddUserMessage).toHaveBeenCalledWith('Message 2')
    expect(mockAddUserMessage).toHaveBeenCalledWith('Message 3')
    expect(mockAddUserMessage).toHaveBeenCalledTimes(3)

    // Verify all API calls were made
    await waitFor(() => {
      expect(mockSessionsAPI.postApiSessionsByIdMessages).toHaveBeenCalledTimes(3)
    })
  })
})
