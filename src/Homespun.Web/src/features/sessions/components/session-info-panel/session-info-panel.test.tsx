import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { SessionInfoPanel } from './session-info-panel'
import type { ClaudeSession } from '@/types/signalr'

// Mock hooks
import { useMobile } from '@/hooks/use-mobile'
vi.mock('@/hooks/use-mobile', () => ({
  useMobile: vi.fn(() => false), // Default to desktop
}))

// Mock bottom sheet
vi.mock('../bottom-sheet', () => ({
  BottomSheet: ({
    children,
    ...props
  }: {
    children?: React.ReactNode
    open?: boolean
    onOpenChange?: (open: boolean) => void
    title?: string
  }) => (
    <div data-testid="bottom-sheet" {...props}>
      {children}
    </div>
  ),
}))

// Mock child components
vi.mock('./session-issue-tab', () => ({
  SessionIssueTab: ({ session }: { session: ClaudeSession }) => (
    <div data-testid="session-issue-tab">Issue Tab - {session.entityId}</div>
  ),
}))

vi.mock('./session-pr-tab', () => ({
  SessionPrTab: ({ session }: { session: ClaudeSession }) => (
    <div data-testid="session-pr-tab">PR Tab - {session.entityId}</div>
  ),
}))

vi.mock('./session-todos-tab', () => ({
  SessionTodosTab: ({ messages }: { messages?: unknown[] }) => (
    <div data-testid="session-todos-tab">Todos Tab - {messages?.length ?? 0} messages</div>
  ),
}))

vi.mock('./session-files-tab', () => ({
  SessionFilesTab: ({ session }: { session: ClaudeSession }) => (
    <div data-testid="session-files-tab">Files Tab - {session.workingDirectory}</div>
  ),
}))

vi.mock('./session-plans-tab', () => ({
  SessionPlansTab: ({ session }: { session: ClaudeSession }) => (
    <div data-testid="session-plans-tab">Plans Tab - {session.workingDirectory}</div>
  ),
}))

vi.mock('./session-branch-tab', () => ({
  SessionBranchTab: ({ session }: { session: ClaudeSession }) => (
    <div data-testid="session-branch-tab">Branch Tab - {session.workingDirectory}</div>
  ),
}))

vi.mock('./session-history-tab', () => ({
  SessionHistoryTab: ({ session }: { session: ClaudeSession }) => (
    <div data-testid="session-history-tab">Sessions Tab - {session.entityId}</div>
  ),
}))

// Mock UI components
vi.mock('@/components/ui/tabs', () => ({
  Tabs: ({
    children,
    defaultValue,
    ...props
  }: {
    children?: React.ReactNode
    defaultValue?: string
  }) => (
    <div data-testid="tabs" data-default-value={defaultValue} {...props}>
      {children}
    </div>
  ),
  TabsList: ({
    children,
    className,
  }: {
    children?: React.ReactNode
    className?: string
    variant?: string
  }) => (
    <div data-testid="tabs-list" className={className}>
      {children}
    </div>
  ),
  TabsTrigger: ({
    children,
    value,
    onClick,
    className,
  }: {
    children?: React.ReactNode
    value: string
    onClick?: () => void
    className?: string
  }) => (
    <button data-testid={`tab-trigger-${value}`} onClick={onClick} className={className}>
      {children}
    </button>
  ),
  TabsContent: ({ children, value }: { children?: React.ReactNode; value: string }) => (
    <div data-testid={`tab-content-${value}`} role="tabpanel">
      {children}
    </div>
  ),
}))

// Mock localStorage
const mockLocalStorage = {
  getItem: vi.fn(),
  setItem: vi.fn(),
  removeItem: vi.fn(),
  clear: vi.fn(),
}
Object.defineProperty(window, 'localStorage', { value: mockLocalStorage })

describe('SessionInfoPanel', () => {
  const mockSession: ClaudeSession = {
    id: 'session-123',
    entityId: 'issue-456',
    projectId: 'proj-789',
    workingDirectory: '/workdir',
    model: 'opus',
    mode: 'build',
    status: 'running',
    createdAt: new Date().toISOString(),
    lastActivityAt: new Date().toISOString(),
    totalCostUsd: 0,
    totalDurationMs: 0,
    hasPendingPlanApproval: false,
    contextClearMarkers: [],
    messages: [
      {
        id: 'msg-1',
        sessionId: 'session-123',
        role: 'user',
        content: [{ type: 'text', text: 'Hello', isStreaming: false, index: 0 }],
        createdAt: new Date().toISOString(),
        isStreaming: false,
      },
    ],
  }

  beforeEach(() => {
    vi.clearAllMocks()
    mockLocalStorage.getItem.mockReturnValue(null)
  })

  describe('Desktop Layout', () => {
    it('renders as a side panel on desktop', () => {
      render(<SessionInfoPanel session={mockSession} isOpen={true} onOpenChange={() => {}} />)

      const panel = screen.getByTestId('session-info-panel-desktop')
      expect(panel).toBeInTheDocument()
      expect(panel).toHaveClass('w-80') // 320px width
    })

    it('hides panel when closed on desktop', () => {
      render(<SessionInfoPanel session={mockSession} isOpen={false} onOpenChange={() => {}} />)

      const panel = screen.getByTestId('session-info-panel-desktop')
      expect(panel).toHaveClass('translate-x-full')
    })

    it('shows panel when open on desktop', () => {
      render(<SessionInfoPanel session={mockSession} isOpen={true} onOpenChange={() => {}} />)

      const panel = screen.getByTestId('session-info-panel-desktop')
      expect(panel).not.toHaveClass('translate-x-full')
    })
  })

  describe('Mobile Layout', () => {
    beforeEach(() => {
      vi.mocked(useMobile).mockReturnValue(true)
    })

    it('renders as a bottom sheet on mobile', () => {
      render(<SessionInfoPanel session={mockSession} isOpen={true} onOpenChange={() => {}} />)

      expect(screen.getByTestId('bottom-sheet')).toBeInTheDocument()
      expect(screen.queryByTestId('session-info-panel-desktop')).not.toBeInTheDocument()
    })
  })

  describe('Tab Navigation', () => {
    it('renders all 7 tabs', () => {
      render(<SessionInfoPanel session={mockSession} isOpen={true} onOpenChange={() => {}} />)

      expect(screen.getByTestId('tab-trigger-issue')).toBeInTheDocument()
      expect(screen.getByTestId('tab-trigger-pr')).toBeInTheDocument()
      expect(screen.getByTestId('tab-trigger-todos')).toBeInTheDocument()
      expect(screen.getByTestId('tab-trigger-files')).toBeInTheDocument()
      expect(screen.getByTestId('tab-trigger-plans')).toBeInTheDocument()
      expect(screen.getByTestId('tab-trigger-branch')).toBeInTheDocument()
      expect(screen.getByTestId('tab-trigger-sessions')).toBeInTheDocument()
    })

    it('has horizontal scroll enabled on tabs list', () => {
      render(<SessionInfoPanel session={mockSession} isOpen={true} onOpenChange={() => {}} />)

      const tabsList = screen.getByTestId('tabs-list')
      expect(tabsList).toHaveClass('overflow-x-auto')
      expect(tabsList).toHaveClass('scrollbar-thin')
    })

    it('prevents tab triggers from shrinking', () => {
      render(<SessionInfoPanel session={mockSession} isOpen={true} onOpenChange={() => {}} />)

      const tabTriggers = [
        screen.getByTestId('tab-trigger-issue'),
        screen.getByTestId('tab-trigger-pr'),
        screen.getByTestId('tab-trigger-todos'),
        screen.getByTestId('tab-trigger-files'),
        screen.getByTestId('tab-trigger-plans'),
        screen.getByTestId('tab-trigger-branch'),
        screen.getByTestId('tab-trigger-sessions'),
      ]

      tabTriggers.forEach((trigger) => {
        expect(trigger).toHaveClass('shrink-0')
      })
    })

    it('shows issue tab by default', () => {
      render(<SessionInfoPanel session={mockSession} isOpen={true} onOpenChange={() => {}} />)

      expect(screen.getByTestId('tabs')).toHaveAttribute('data-default-value', 'issue')
    })

    it('renders correct tab content', async () => {
      const user = userEvent.setup()
      render(<SessionInfoPanel session={mockSession} isOpen={true} onOpenChange={() => {}} />)

      // Check each tab
      await user.click(screen.getByTestId('tab-trigger-pr'))
      expect(screen.getByTestId('session-pr-tab')).toBeInTheDocument()

      await user.click(screen.getByTestId('tab-trigger-todos'))
      expect(screen.getByTestId('session-todos-tab')).toBeInTheDocument()

      await user.click(screen.getByTestId('tab-trigger-files'))
      expect(screen.getByTestId('session-files-tab')).toBeInTheDocument()

      await user.click(screen.getByTestId('tab-trigger-plans'))
      expect(screen.getByTestId('session-plans-tab')).toBeInTheDocument()
    })
  })

  describe('State Persistence', () => {
    it('saves open state to localStorage when changed', () => {
      const onOpenChange = vi.fn()
      const { rerender } = render(
        <SessionInfoPanel session={mockSession} isOpen={false} onOpenChange={onOpenChange} />
      )

      // Change to open
      rerender(<SessionInfoPanel session={mockSession} isOpen={true} onOpenChange={onOpenChange} />)

      expect(mockLocalStorage.setItem).toHaveBeenCalledWith('sessionInfoPanelOpen', 'true')
    })

    it('saves closed state to localStorage when changed', () => {
      const onOpenChange = vi.fn()
      const { rerender } = render(
        <SessionInfoPanel session={mockSession} isOpen={true} onOpenChange={onOpenChange} />
      )

      // Change to closed
      rerender(
        <SessionInfoPanel session={mockSession} isOpen={false} onOpenChange={onOpenChange} />
      )

      expect(mockLocalStorage.setItem).toHaveBeenCalledWith('sessionInfoPanelOpen', 'false')
    })

    it('initializes from localStorage on mount', () => {
      mockLocalStorage.getItem.mockReturnValue('true')
      const onOpenChange = vi.fn()

      render(<SessionInfoPanel session={mockSession} isOpen={false} onOpenChange={onOpenChange} />)

      expect(onOpenChange).toHaveBeenCalledWith(true)
    })
  })

  describe('Close Button', () => {
    beforeEach(() => {
      vi.mocked(useMobile).mockReturnValue(false) // Ensure desktop
    })

    it('calls onOpenChange when close button is clicked on desktop', async () => {
      const onOpenChange = vi.fn()
      const user = userEvent.setup()

      render(<SessionInfoPanel session={mockSession} isOpen={true} onOpenChange={onOpenChange} />)

      const closeButton = screen.getByLabelText('Close panel')
      await user.click(closeButton)

      expect(onOpenChange).toHaveBeenCalledWith(false)
    })
  })

  describe('Session Data', () => {
    it('passes session data to all tabs', () => {
      render(
        <SessionInfoPanel
          session={mockSession}
          messages={[
            // @ts-expect-error — the SessionTodosTab mock only reads `.length`, not the shape.
            {},
            // @ts-expect-error — see above.
            {},
          ]}
          isOpen={true}
          onOpenChange={() => {}}
        />
      )

      expect(screen.getByText('Issue Tab - issue-456')).toBeInTheDocument()
      expect(screen.getByText('Todos Tab - 2 messages')).toBeInTheDocument()
    })
  })

  describe('Hidden by Default', () => {
    beforeEach(() => {
      vi.mocked(useMobile).mockReturnValue(false) // Ensure desktop
    })

    it('is hidden by default when no localStorage value exists', () => {
      mockLocalStorage.getItem.mockReturnValue(null)
      const onOpenChange = vi.fn()

      render(
        <SessionInfoPanel
          session={mockSession}
          isOpen={false} // Start closed
          onOpenChange={onOpenChange}
          defaultOpen={false}
        />
      )

      // Should respect isOpen prop and be hidden
      const panel = screen.getByTestId('session-info-panel-desktop')
      expect(panel).toHaveClass('translate-x-full')

      // Should not call onOpenChange when default matches current state
      expect(onOpenChange).not.toHaveBeenCalled()
    })
  })
})
