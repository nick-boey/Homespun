import { describe, it, expect, vi, beforeEach, Mock } from 'vitest'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { Sessions } from '@/api'
import { toast } from 'sonner'

// Import first to get the actual component
import './sessions.$sessionId'

// Mock all dependencies
vi.mock('@tanstack/react-router', () => ({
  createFileRoute: vi.fn((path: string) => {
    return (config: any) => ({
      path,
      component: config.component,
    })
  }),
  useParams: () => ({ sessionId: 'test-session-id' }),
  Link: ({ children, to }: any) => <a href={to}>{children}</a>,
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
  useSessionMessages: vi.fn(() => ({ messages: [] })),
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
const SessionChat = Route.component as any

describe('SessionChat - Message Sending', () => {
  const mockSessionsAPI = vi.mocked(Sessions)
  const mockToast = vi.mocked(toast)
  const mockSession = {
    id: 'test-session-id',
    entityId: 'entity-123',
    projectId: 'project-123',
    workingDirectory: '/test/dir',
    model: 'claude-3-5-sonnet',
    mode: 'Build',
    status: 'WaitingForInput',
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

    const { useSession, ChatInput } = vi.mocked(await import('@/features/sessions'))

    // Mock useSession to return a valid session
    useSession.mockReturnValue({
      session: mockSession,
      isLoading: false,
      isNotFound: false,
      error: null,
      refetch: vi.fn(),
    })

    // Mock ChatInput to capture onSend callback
    ChatInput.mockImplementation(({ onSend, disabled, placeholder, isLoading }) => (
      <div data-testid="chat-input">
        <button
          data-testid="send-button"
          onClick={() => onSend('Test message', 'Build', 'claude-3-5-sonnet')}
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
      error: null,
    })

    render(<SessionChat />)

    const sendButton = screen.getByTestId('send-button')
    await user.click(sendButton)

    // Verify API was called with correct parameters
    await waitFor(() => {
      expect(mockSessionsAPI.postApiSessionsByIdMessages).toHaveBeenCalledWith({
        path: { id: 'test-session-id' },
        body: { message: 'Test message', mode: 'Build' },
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
    mockSessionsAPI.postApiSessionsByIdMessages = vi.fn().mockRejectedValueOnce(
      new Error('Network error')
    )

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
      connectionStatus: 'disconnected',
      error: null,
    })

    render(<SessionChat />)

    const sendButton = screen.getByTestId('send-button')
    expect(sendButton).toBeDisabled()
  })

  it('should disable send button when session is processing', async () => {
    const { useSession } = vi.mocked(await import('@/features/sessions'))
    useSession.mockReturnValue({
      session: { ...mockSession, status: 'Running' },
      isLoading: false,
      isNotFound: false,
      error: null,
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

    // Initially enabled
    expect(sendButton).not.toBeDisabled()

    await user.click(sendButton)

    // Should be disabled while sending
    expect(sendButton).toBeDisabled()

    // Resolve the promise
    resolvePromise!()

    await waitFor(() => {
      expect(sendButton).not.toBeDisabled()
    })
  })

  it('should send messages with Plan mode correctly', async () => {
    const user = userEvent.setup()
    mockSessionsAPI.postApiSessionsByIdMessages = vi.fn().mockResolvedValueOnce({
      data: {},
      error: null,
    })

    // Re-mock ChatInput for this specific test to use Plan mode
    const { ChatInput } = vi.mocked(await import('@/features/sessions'))
    ChatInput.mockImplementation(({ onSend }) => (
      <button
        data-testid="send-plan-button"
        onClick={() => onSend('Plan message', 'Plan', 'claude-3-5-sonnet')}
      >
        Send Plan
      </button>
    ))

    render(<SessionChat />)

    const sendButton = await screen.findByTestId('send-plan-button')
    await user.click(sendButton)

    await waitFor(() => {
      expect(mockSessionsAPI.postApiSessionsByIdMessages).toHaveBeenCalledWith({
        path: { id: 'test-session-id' },
        body: { message: 'Plan message', mode: 'Plan' },
      })
    })
  })

  it('should show appropriate placeholder text when not connected', async () => {
    const { useClaudeCodeHub } = vi.mocked(await import('@/providers/signalr-provider'))
    useClaudeCodeHub.mockReturnValue({
      isConnected: false,
      methods: null,
      connectionStatus: 'connecting',
      error: null,
    })

    render(<SessionChat />)

    const placeholder = screen.getByTestId('placeholder')
    expect(placeholder).toHaveTextContent('Connecting...')
  })

  it('should show appropriate placeholder text when processing', async () => {
    const { useSession } = vi.mocked(await import('@/features/sessions'))
    useSession.mockReturnValue({
      session: { ...mockSession, status: 'Running' },
      isLoading: false,
      isNotFound: false,
      error: null,
      refetch: vi.fn(),
    })

    render(<SessionChat />)

    const placeholder = screen.getByTestId('placeholder')
    expect(placeholder).toHaveTextContent('Processing...')
  })
})