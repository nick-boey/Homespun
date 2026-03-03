import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { renderHook, waitFor } from '@testing-library/react'
import { useNotificationEvents } from './use-notification-events'
import { useNotificationStore } from '../stores/notification-store'
import type { NotificationDto, IssueChangeType } from '@/types/signalr'

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

// Mock sonner toast
vi.mock('sonner', () => ({
  toast: {
    info: vi.fn(),
    warning: vi.fn(),
    error: vi.fn(),
    dismiss: vi.fn(),
  },
}))

import { toast } from 'sonner'

describe('useNotificationEvents', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    useNotificationStore.setState({
      notifications: [],
      unreadCount: 0,
      preferences: {
        showToasts: true,
        autoDismissDuration: 5000,
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
    expect(mockConnection.on).toHaveBeenCalledWith('IssuesChanged', expect.any(Function))
  })

  it('fetches active notifications on mount when connected', async () => {
    const notifications: NotificationDto[] = [
      {
        id: 'test-1',
        type: 'Info',
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
      type: 'Warning',
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

  it('shows toast when notification is added and showToasts is enabled', async () => {
    let notificationAddedHandler: (notification: NotificationDto) => void = () => {}

    mockConnection.on.mockImplementation((event: string, handler: unknown) => {
      if (event === 'NotificationAdded') {
        notificationAddedHandler = handler as (notification: NotificationDto) => void
      }
    })

    renderHook(() => useNotificationEvents())

    const notification: NotificationDto = {
      id: 'toast-test',
      type: 'Info',
      title: 'Toast Test',
      message: 'This should show a toast',
      createdAt: new Date().toISOString(),
      isDismissible: true,
    }

    notificationAddedHandler(notification)

    await waitFor(() => {
      expect(toast.info).toHaveBeenCalled()
    })
  })

  it('does not show toast when showToasts is disabled', async () => {
    useNotificationStore.setState({
      notifications: [],
      unreadCount: 0,
      preferences: {
        showToasts: false,
        autoDismissDuration: 5000,
        soundEnabled: false,
      },
    })

    let notificationAddedHandler: (notification: NotificationDto) => void = () => {}

    mockConnection.on.mockImplementation((event: string, handler: unknown) => {
      if (event === 'NotificationAdded') {
        notificationAddedHandler = handler as (notification: NotificationDto) => void
      }
    })

    renderHook(() => useNotificationEvents())

    const notification: NotificationDto = {
      id: 'no-toast',
      type: 'Info',
      title: 'No Toast',
      message: 'This should not show a toast',
      createdAt: new Date().toISOString(),
      isDismissible: true,
    }

    notificationAddedHandler(notification)

    await waitFor(() => {
      expect(toast.info).not.toHaveBeenCalled()
    })
  })

  it('removes notification from store when NotificationDismissed event fires', async () => {
    // Add notification first
    useNotificationStore.getState().addNotification({
      id: 'to-dismiss',
      type: 'Info',
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
      changeType: IssueChangeType,
      issueId: string
    ) => void = () => {}

    mockConnection.on.mockImplementation((event: string, handler: unknown) => {
      if (event === 'IssuesChanged') {
        issueChangedHandler = handler as (
          projectId: string,
          changeType: IssueChangeType,
          issueId: string
        ) => void
      }
    })

    renderHook(() => useNotificationEvents())

    issueChangedHandler('project-1', 'Created', 'issue-123')

    await waitFor(() => {
      const notifications = useNotificationStore.getState().notifications
      expect(notifications.length).toBeGreaterThan(0)
      expect(notifications[0].title).toContain('Issue')
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
