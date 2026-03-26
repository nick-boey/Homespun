import { describe, it, expect, vi, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { NotificationDropdown } from './notification-dropdown'
import { useNotificationStore } from '../stores/notification-store'

// Mock the SignalR provider hooks
vi.mock('@/providers/signalr-provider', () => ({
  useNotificationHub: () => ({
    connection: null,
    status: 'disconnected',
    methods: null,
    isConnected: false,
    isReconnecting: false,
  }),
}))

describe('NotificationDropdown', () => {
  beforeEach(() => {
    useNotificationStore.setState({
      notifications: [],
      unreadCount: 0,
      preferences: {
        soundEnabled: false,
      },
    })
  })

  it('renders bell icon button', () => {
    render(<NotificationDropdown />)

    expect(screen.getByRole('button', { name: /notifications/i })).toBeInTheDocument()
  })

  it('shows badge with unread count when there are unread notifications', () => {
    useNotificationStore.getState().addNotification({
      id: 'test-1',
      type: 'info',
      title: 'Test',
      message: 'Test message',
      createdAt: new Date().toISOString(),
      isDismissible: true,
    })

    render(<NotificationDropdown />)

    expect(screen.getByText('1')).toBeInTheDocument()
  })

  it('does not show badge when there are no unread notifications', () => {
    render(<NotificationDropdown />)

    expect(screen.queryByTestId('unread-badge')).not.toBeInTheDocument()
  })

  it('opens dropdown when clicking the bell button', async () => {
    const user = userEvent.setup()
    render(<NotificationDropdown />)

    await user.click(screen.getByRole('button', { name: /notifications/i }))

    expect(screen.getByRole('menu')).toBeInTheDocument()
  })

  it('shows empty state when there are no notifications', async () => {
    const user = userEvent.setup()
    render(<NotificationDropdown />)

    await user.click(screen.getByRole('button', { name: /notifications/i }))

    expect(screen.getByText(/no notifications/i)).toBeInTheDocument()
  })

  it('displays notifications list when there are notifications', async () => {
    const user = userEvent.setup()
    useNotificationStore.getState().addNotification({
      id: 'test-1',
      type: 'info',
      title: 'First Notification',
      message: 'First message',
      createdAt: new Date().toISOString(),
      isDismissible: true,
    })
    useNotificationStore.getState().addNotification({
      id: 'test-2',
      type: 'warning',
      title: 'Second Notification',
      message: 'Second message',
      createdAt: new Date().toISOString(),
      isDismissible: true,
    })

    render(<NotificationDropdown />)

    await user.click(screen.getByRole('button', { name: /notifications/i }))

    expect(screen.getByText('First Notification')).toBeInTheDocument()
    expect(screen.getByText('Second Notification')).toBeInTheDocument()
  })

  it('calls markAllAsRead when clicking mark all as read button', async () => {
    const user = userEvent.setup()
    useNotificationStore.getState().addNotification({
      id: 'test-1',
      type: 'info',
      title: 'Test',
      message: 'Test message',
      createdAt: new Date().toISOString(),
      isDismissible: true,
    })

    render(<NotificationDropdown />)

    await user.click(screen.getByRole('button', { name: /notifications/i }))
    await user.click(screen.getByRole('button', { name: /mark all as read/i }))

    expect(useNotificationStore.getState().unreadCount).toBe(0)
  })

  it('calls clearAll when clicking clear all button', async () => {
    const user = userEvent.setup()
    useNotificationStore.getState().addNotification({
      id: 'test-1',
      type: 'info',
      title: 'Test',
      message: 'Test message',
      createdAt: new Date().toISOString(),
      isDismissible: true,
    })

    render(<NotificationDropdown />)

    await user.click(screen.getByRole('button', { name: /notifications/i }))
    await user.click(screen.getByRole('button', { name: /clear all/i }))

    expect(useNotificationStore.getState().notifications).toHaveLength(0)
  })

  it('dismisses notification when clicking dismiss on notification item', async () => {
    const user = userEvent.setup()
    useNotificationStore.getState().addNotification({
      id: 'test-1',
      type: 'info',
      title: 'Test',
      message: 'Test message',
      createdAt: new Date().toISOString(),
      isDismissible: true,
    })

    render(<NotificationDropdown />)

    await user.click(screen.getByRole('button', { name: /notifications/i }))

    const dismissButton = screen.getByRole('button', { name: /dismiss notification/i })
    await user.click(dismissButton)

    expect(useNotificationStore.getState().notifications).toHaveLength(0)
  })

  it('shows badge count capped at 9+', () => {
    // Add 15 notifications
    for (let i = 0; i < 15; i++) {
      useNotificationStore.getState().addNotification({
        id: `test-${i}`,
        type: 'info',
        title: `Test ${i}`,
        message: `Message ${i}`,
        createdAt: new Date().toISOString(),
        isDismissible: true,
      })
    }

    render(<NotificationDropdown />)

    expect(screen.getByText('9+')).toBeInTheDocument()
  })

  it('filters notifications by projectId when provided', async () => {
    const user = userEvent.setup()
    useNotificationStore.getState().addNotification({
      id: 'project-notification',
      type: 'info',
      title: 'Project Specific',
      message: 'For project-1',
      projectId: 'project-1',
      createdAt: new Date().toISOString(),
      isDismissible: true,
    })
    useNotificationStore.getState().addNotification({
      id: 'other-notification',
      type: 'info',
      title: 'Other Project',
      message: 'For project-2',
      projectId: 'project-2',
      createdAt: new Date().toISOString(),
      isDismissible: true,
    })
    useNotificationStore.getState().addNotification({
      id: 'global-notification',
      type: 'info',
      title: 'Global',
      message: 'No project',
      createdAt: new Date().toISOString(),
      isDismissible: true,
    })

    render(<NotificationDropdown projectId="project-1" />)

    await user.click(screen.getByRole('button', { name: /notifications/i }))

    // Should show project-1 and global notifications, but not project-2
    expect(screen.getByText('Project Specific')).toBeInTheDocument()
    expect(screen.getByText('Global')).toBeInTheDocument()
    expect(screen.queryByText('Other Project')).not.toBeInTheDocument()
  })
})
