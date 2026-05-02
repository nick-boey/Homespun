import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { toast } from 'sonner'
import { useNotificationEvents } from './use-notification-events'
import { useNotificationStore } from '../stores/notification-store'
import type { NotificationDto, IssueChangeKind } from '@/types/signalr'

vi.mock('sonner', () => ({
  toast: {
    error: vi.fn(),
    info: vi.fn(),
    success: vi.fn(),
    warning: vi.fn(),
  },
}))

// Mock the SignalR provider hooks
const mockConnection = {
  on: vi.fn(),
  off: vi.fn(),
}

const mockMethods = {
  joinProjectGroup: vi.fn().mockResolvedValue(undefined),
  leaveProjectGroup: vi.fn().mockResolvedValue(undefined),
  getActiveNotifications: vi.fn().mockResolvedValue([]),
  dismissNotification: vi.fn().mockResolvedValue(undefined),
}

vi.mock('@/providers/signalr-provider', () => ({
  useNotificationHub: () => ({
    connection: mockConnection,
    status: 'connected',
    methods: mockMethods,
    isConnected: true,
    isReconnecting: false,
  }),
}))

describe('useNotificationEvents', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useNotificationStore.setState({
      notifications: [],
      unreadCount: 0,
      preferences: {
        soundEnabled: false,
      },
    })
  })

  afterEach(() => {
    vi.restoreAllMocks()
  })

  it('registers event handlers on mount when connected', () => {
    renderHook(() => useNotificationEvents())

    expect(mockConnection.on).toHaveBeenCalledWith('NotificationAdded', expect.any(Function))
    expect(mockConnection.on).toHaveBeenCalledWith('NotificationDismissed', expect.any(Function))
    expect(mockConnection.on).toHaveBeenCalledWith('IssueChanged', expect.any(Function))
  })

  it('fetches active notifications on mount when connected', async () => {
    const notifications: NotificationDto[] = [
      {
        id: 'test-1',
        type: 'info',
        title: 'Test',
        message: 'Test message',
        createdAt: new Date().toISOString(),
        isDismissible: true,
      },
    ]
    mockMethods.getActiveNotifications.mockResolvedValue(notifications)

    renderHook(() => useNotificationEvents())

    await waitFor(() => {
      expect(mockMethods.getActiveNotifications).toHaveBeenCalled()
    })
  })

  it('adds notification to store when NotificationAdded event fires', async () => {
    let notificationAddedHandler: (notification: NotificationDto) => void = () => {}

    mockConnection.on.mockImplementation((event: string, handler: unknown) => {
      if (event === 'NotificationAdded') {
        notificationAddedHandler = handler as (notification: NotificationDto) => void
      }
    })

    renderHook(() => useNotificationEvents())

    const notification: NotificationDto = {
      id: 'new-notification',
      type: 'warning',
      title: 'New Warning',
      message: 'Something needs attention',
      createdAt: new Date().toISOString(),
      isDismissible: true,
    }

    notificationAddedHandler(notification)

    await waitFor(() => {
      const state = useNotificationStore.getState()
      expect(state.notifications).toHaveLength(1)
      expect(state.notifications[0].id).toBe('new-notification')
    })
  })

  it('removes notification from store when NotificationDismissed event fires', async () => {
    // Add notification first
    useNotificationStore.getState().addNotification({
      id: 'to-dismiss',
      type: 'info',
      title: 'To Dismiss',
      message: 'This will be dismissed',
      createdAt: new Date().toISOString(),
      isDismissible: true,
    })

    let dismissHandler: (id: string) => void = () => {}

    mockConnection.on.mockImplementation((event: string, handler: unknown) => {
      if (event === 'NotificationDismissed') {
        dismissHandler = handler as (id: string) => void
      }
    })

    renderHook(() => useNotificationEvents())

    dismissHandler('to-dismiss')

    await waitFor(() => {
      expect(useNotificationStore.getState().notifications).toHaveLength(0)
    })
  })

  it('creates notification for issue changes', async () => {
    let issueChangedHandler: (
      projectId: string,
      kind: IssueChangeKind,
      issueId: string | null
    ) => void = () => {}

    mockConnection.on.mockImplementation((event: string, handler: unknown) => {
      if (event === 'IssueChanged') {
        issueChangedHandler = handler as (
          projectId: string,
          kind: IssueChangeKind,
          issueId: string | null
        ) => void
      }
    })

    renderHook(() => useNotificationEvents())

    issueChangedHandler('project-1', 'created', 'issue-123')

    await waitFor(() => {
      const notifications = useNotificationStore.getState().notifications
      expect(notifications.length).toBeGreaterThan(0)
      expect(notifications[0].title).toContain('Issue')
    })
  })

  it('surfaces AgentStartFailed via a sonner toast in addition to the store', async () => {
    let agentStartFailedHandler: (
      issueId: string,
      projectId: string,
      error: string
    ) => void = () => {}

    mockConnection.on.mockImplementation((event: string, handler: unknown) => {
      if (event === 'AgentStartFailed') {
        agentStartFailedHandler = handler as (
          issueId: string,
          projectId: string,
          error: string
        ) => void
      }
    })
    // Reassert the empty-active-notifications mock — earlier tests in this file
    // mutate the resolved value, and that bleeds across cases (it would replace
    // our just-added store entry once the mount-time getActiveNotifications
    // promise resolves).
    mockMethods.getActiveNotifications.mockResolvedValue([])

    // Mount on a *different* project — simulates the user having navigated away.
    renderHook(() => useNotificationEvents({ projectId: 'unrelated-project' }))

    agentStartFailedHandler('issue-42', 'origin-project', 'Base branch is blocked')

    // The toast surfaces the failure regardless of the current route's projectId
    // filter on the bell dropdown — that's the whole point of FI-2.
    await waitFor(() => {
      expect(toast.error).toHaveBeenCalledWith(
        'Agent Start Failed',
        expect.objectContaining({
          description: 'Base branch is blocked',
          id: 'agent-start-failed-issue-42',
        })
      )
    })

    // Store entry is also added so the bell-dropdown history is preserved
    // for the originating project.
    await waitFor(() => {
      const stored = useNotificationStore.getState().notifications
      expect(stored.some((n) => n.title === 'Agent Start Failed')).toBe(true)
    })
  })

  it('unregisters event handlers on unmount', () => {
    const { unmount } = renderHook(() => useNotificationEvents())

    unmount()

    expect(mockConnection.off).toHaveBeenCalled()
  })

  it('joins project group when projectId is provided', async () => {
    renderHook(() => useNotificationEvents({ projectId: 'project-123' }))

    await waitFor(() => {
      expect(mockMethods.joinProjectGroup).toHaveBeenCalledWith('project-123')
    })
  })

  it('leaves project group on unmount', async () => {
    const { unmount } = renderHook(() => useNotificationEvents({ projectId: 'project-123' }))

    await waitFor(() => {
      expect(mockMethods.joinProjectGroup).toHaveBeenCalledWith('project-123')
    })

    unmount()

    await waitFor(() => {
      expect(mockMethods.leaveProjectGroup).toHaveBeenCalledWith('project-123')
    })
  })
})
