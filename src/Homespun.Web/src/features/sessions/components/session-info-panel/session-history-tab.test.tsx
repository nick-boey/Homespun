import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen, fireEvent } from '@testing-library/react'
import { SessionHistoryTab } from './session-history-tab'
import { useSessionHistory } from '@/features/sessions/hooks/use-session-history'
import type { SessionCacheSummary } from '@/api/generated'
import { SessionMode } from '@/api/generated'
import { createMockSession } from '@/test/test-utils'

vi.mock('@/features/sessions/hooks/use-session-history', () => ({
  useSessionHistory: vi.fn(),
}))

const mockSessions: SessionCacheSummary[] = [
  {
    sessionId: 'session-1',
    entityId: 'issue-1',
    projectId: 'project-1',
    model: 'sonnet',
    mode: SessionMode.BUILD,
    createdAt: new Date().toISOString(),
    lastMessageAt: new Date().toISOString(),
    messageCount: 15,
  },
  {
    sessionId: 'session-2',
    entityId: 'issue-1',
    projectId: 'project-1',
    model: 'opus',
    mode: SessionMode.PLAN,
    createdAt: new Date(Date.now() - 3600 * 1000).toISOString(), // 1 hour ago
    lastMessageAt: new Date(Date.now() - 3600 * 1000).toISOString(),
    messageCount: 5,
  },
]

describe('SessionHistoryTab', () => {
  const mockSession = createMockSession({
    id: 'session-1',
    projectId: 'project-1',
    entityId: 'issue-1',
  })

  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('shows loading state', () => {
    vi.mocked(useSessionHistory).mockReturnValue({
      data: undefined,
      isLoading: true,
      error: null,
    })

    const { container } = render(<SessionHistoryTab session={mockSession} />)

    expect(container.querySelectorAll('[data-slot="skeleton"]').length).toBeGreaterThan(0)
  })

  it('shows error state', () => {
    vi.mocked(useSessionHistory).mockReturnValue({
      data: undefined,
      isLoading: false,
      error: new Error('Failed to load'),
    })

    render(<SessionHistoryTab session={mockSession} />)

    expect(screen.getByText('Failed to load session history')).toBeInTheDocument()
  })

  it('shows empty state when no sessions', () => {
    vi.mocked(useSessionHistory).mockReturnValue({
      data: [],
      isLoading: false,
      error: null,
    })

    render(<SessionHistoryTab session={mockSession} />)

    expect(screen.getByText('No session history')).toBeInTheDocument()
  })

  it('shows session list', () => {
    vi.mocked(useSessionHistory).mockReturnValue({
      data: mockSessions,
      isLoading: false,
      error: null,
    })

    render(<SessionHistoryTab session={mockSession} />)

    // Summary
    expect(screen.getByText('2 sessions')).toBeInTheDocument()

    // Mode badges
    expect(screen.getByText('build')).toBeInTheDocument()
    expect(screen.getByText('plan')).toBeInTheDocument()

    // Message counts
    expect(screen.getByText('15 messages')).toBeInTheDocument()
    expect(screen.getByText('5 messages')).toBeInTheDocument()
  })

  it('marks active session', () => {
    vi.mocked(useSessionHistory).mockReturnValue({
      data: mockSessions,
      isLoading: false,
      error: null,
    })

    render(<SessionHistoryTab session={mockSession} currentSessionId="session-1" />)

    expect(screen.getByText('Active')).toBeInTheDocument()
  })

  it('calls onSelectSession when clicking a session', () => {
    vi.mocked(useSessionHistory).mockReturnValue({
      data: mockSessions,
      isLoading: false,
      error: null,
    })

    const onSelectSession = vi.fn()

    render(<SessionHistoryTab session={mockSession} onSelectSession={onSelectSession} />)

    // Click the second session
    const sessions = screen.getAllByRole('button')
    fireEvent.click(sessions[1])

    expect(onSelectSession).toHaveBeenCalledWith('session-2')
  })

  it('highlights viewing session', () => {
    vi.mocked(useSessionHistory).mockReturnValue({
      data: mockSessions,
      isLoading: false,
      error: null,
    })

    const { container } = render(
      <SessionHistoryTab session={mockSession} viewingHistoricalSessionId="session-2" />
    )

    // The second session should have the viewing highlight
    const sessionItems = container.querySelectorAll('[role="button"]')
    expect(sessionItems[1]).toHaveClass('border-primary')
  })

  it('formats relative time correctly', () => {
    vi.mocked(useSessionHistory).mockReturnValue({
      data: mockSessions,
      isLoading: false,
      error: null,
    })

    render(<SessionHistoryTab session={mockSession} />)

    // First session is "Just now"
    expect(screen.getByText('Just now')).toBeInTheDocument()
    // Second session is "1h ago"
    expect(screen.getByText('1h ago')).toBeInTheDocument()
  })

  it('handles keyboard navigation', () => {
    vi.mocked(useSessionHistory).mockReturnValue({
      data: mockSessions,
      isLoading: false,
      error: null,
    })

    const onSelectSession = vi.fn()

    render(<SessionHistoryTab session={mockSession} onSelectSession={onSelectSession} />)

    const sessions = screen.getAllByRole('button')
    fireEvent.keyDown(sessions[0], { key: 'Enter' })

    expect(onSelectSession).toHaveBeenCalledWith('session-1')
  })

  it('handles space key navigation', () => {
    vi.mocked(useSessionHistory).mockReturnValue({
      data: mockSessions,
      isLoading: false,
      error: null,
    })

    const onSelectSession = vi.fn()

    render(<SessionHistoryTab session={mockSession} onSelectSession={onSelectSession} />)

    const sessions = screen.getAllByRole('button')
    fireEvent.keyDown(sessions[0], { key: ' ' })

    expect(onSelectSession).toHaveBeenCalledWith('session-1')
  })
})
