import { describe, it, expect, vi } from 'vitest'
import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { NotificationItem } from './notification-item'
import type { AppNotification } from '../stores/notification-store'

describe('NotificationItem', () => {
  const baseNotification: AppNotification = {
    id: 'test-1',
    type: 'info',
    title: 'Test Notification',
    message: 'This is a test message',
    createdAt: new Date().toISOString(),
    isDismissible: true,
    isRead: false,
  }

  it('renders notification title and message', () => {
    render(<NotificationItem notification={baseNotification} />)

    expect(screen.getByText('Test Notification')).toBeInTheDocument()
    expect(screen.getByText('This is a test message')).toBeInTheDocument()
  })

  it('shows unread indicator when notification is unread', () => {
    render(<NotificationItem notification={baseNotification} />)

    const unreadIndicator = screen.getByTestId('unread-indicator')
    expect(unreadIndicator).toBeInTheDocument()
  })

  it('does not show unread indicator when notification is read', () => {
    const readNotification = { ...baseNotification, isRead: true }
    render(<NotificationItem notification={readNotification} />)

    expect(screen.queryByTestId('unread-indicator')).not.toBeInTheDocument()
  })

  it('calls onDismiss when dismiss button is clicked', async () => {
    const user = userEvent.setup()
    const onDismiss = vi.fn()

    render(<NotificationItem notification={baseNotification} onDismiss={onDismiss} />)

    const dismissButton = screen.getByRole('button', { name: /dismiss/i })
    await user.click(dismissButton)

    expect(onDismiss).toHaveBeenCalledWith('test-1')
  })

  it('does not show dismiss button when notification is not dismissible', () => {
    const nonDismissible = { ...baseNotification, isDismissible: false }
    render(<NotificationItem notification={nonDismissible} onDismiss={() => {}} />)

    expect(screen.queryByRole('button', { name: /dismiss/i })).not.toBeInTheDocument()
  })

  it('calls onMarkAsRead when clicking on unread notification', async () => {
    const user = userEvent.setup()
    const onMarkAsRead = vi.fn()

    render(<NotificationItem notification={baseNotification} onMarkAsRead={onMarkAsRead} />)

    const item = screen.getByRole('article')
    await user.click(item)

    expect(onMarkAsRead).toHaveBeenCalledWith('test-1')
  })

  it('does not call onMarkAsRead when clicking on read notification', async () => {
    const user = userEvent.setup()
    const onMarkAsRead = vi.fn()
    const readNotification = { ...baseNotification, isRead: true }

    render(<NotificationItem notification={readNotification} onMarkAsRead={onMarkAsRead} />)

    const item = screen.getByRole('article')
    await user.click(item)

    expect(onMarkAsRead).not.toHaveBeenCalled()
  })

  it('shows correct icon for Info type', () => {
    render(<NotificationItem notification={baseNotification} />)

    expect(screen.getByTestId('notification-icon-info')).toBeInTheDocument()
  })

  it('shows correct icon for Warning type', () => {
    const warningNotification = { ...baseNotification, type: 'warning' as const }
    render(<NotificationItem notification={warningNotification} />)

    expect(screen.getByTestId('notification-icon-warning')).toBeInTheDocument()
  })

  it('shows correct icon for ActionRequired type', () => {
    const actionNotification = { ...baseNotification, type: 'actionRequired' as const }
    render(<NotificationItem notification={actionNotification} />)

    expect(screen.getByTestId('notification-icon-action')).toBeInTheDocument()
  })

  it('displays relative time since notification was created', () => {
    // Create notification from 5 minutes ago
    const fiveMinutesAgo = new Date(Date.now() - 5 * 60 * 1000).toISOString()
    const notification = { ...baseNotification, createdAt: fiveMinutesAgo }

    render(<NotificationItem notification={notification} />)

    // Should show relative time
    expect(screen.getByText(/ago/i)).toBeInTheDocument()
  })

  it('renders action button when actionLabel is provided', () => {
    const notificationWithAction = {
      ...baseNotification,
      actionLabel: 'View Details',
    }

    render(<NotificationItem notification={notificationWithAction} />)

    expect(screen.getByRole('button', { name: 'View Details' })).toBeInTheDocument()
  })

  it('calls onAction when action button is clicked', async () => {
    const user = userEvent.setup()
    const onAction = vi.fn()
    const notificationWithAction = {
      ...baseNotification,
      actionLabel: 'View Details',
    }

    render(<NotificationItem notification={notificationWithAction} onAction={onAction} />)

    const actionButton = screen.getByRole('button', { name: 'View Details' })
    await user.click(actionButton)

    expect(onAction).toHaveBeenCalledWith('test-1')
  })
})
