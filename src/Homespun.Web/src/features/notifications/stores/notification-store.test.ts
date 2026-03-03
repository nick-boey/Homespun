import { describe, it, expect, beforeEach } from 'vitest'
import { useNotificationStore } from './notification-store'
import type { NotificationDto } from '@/types/signalr'

// Reset store before each test
beforeEach(() => {
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

describe('NotificationStore', () => {
  describe('addNotification', () => {
    it('adds a notification to the store', () => {
      const notification: NotificationDto = {
        id: 'test-1',
        type: 'Info',
        title: 'Test Notification',
        message: 'This is a test message',
        createdAt: new Date().toISOString(),
        isDismissible: true,
      }

      useNotificationStore.getState().addNotification(notification)

      const state = useNotificationStore.getState()
      expect(state.notifications).toHaveLength(1)
      expect(state.notifications[0].id).toBe('test-1')
      expect(state.notifications[0].isRead).toBe(false)
    })

    it('increments unread count when adding notification', () => {
      const notification: NotificationDto = {
        id: 'test-1',
        type: 'Info',
        title: 'Test',
        message: 'Test message',
        createdAt: new Date().toISOString(),
        isDismissible: true,
      }

      useNotificationStore.getState().addNotification(notification)

      expect(useNotificationStore.getState().unreadCount).toBe(1)
    })

    it('adds notifications at the beginning (reverse chronological)', () => {
      const notification1: NotificationDto = {
        id: 'test-1',
        type: 'Info',
        title: 'First',
        message: 'First message',
        createdAt: '2024-01-01T00:00:00Z',
        isDismissible: true,
      }
      const notification2: NotificationDto = {
        id: 'test-2',
        type: 'Warning',
        title: 'Second',
        message: 'Second message',
        createdAt: '2024-01-02T00:00:00Z',
        isDismissible: true,
      }

      const store = useNotificationStore.getState()
      store.addNotification(notification1)
      store.addNotification(notification2)

      const notifications = useNotificationStore.getState().notifications
      expect(notifications[0].id).toBe('test-2')
      expect(notifications[1].id).toBe('test-1')
    })

    it('prevents duplicates with same deduplicationKey', () => {
      const notification: NotificationDto = {
        id: 'test-1',
        type: 'Info',
        title: 'Test',
        message: 'Test message',
        createdAt: new Date().toISOString(),
        isDismissible: true,
        deduplicationKey: 'unique-key',
      }

      const store = useNotificationStore.getState()
      store.addNotification(notification)
      store.addNotification({ ...notification, id: 'test-2' })

      expect(useNotificationStore.getState().notifications).toHaveLength(1)
    })

    it('limits notifications to maxNotifications', () => {
      const store = useNotificationStore.getState()

      // Add 55 notifications (max is 50)
      for (let i = 0; i < 55; i++) {
        store.addNotification({
          id: `test-${i}`,
          type: 'Info',
          title: `Notification ${i}`,
          message: `Message ${i}`,
          createdAt: new Date().toISOString(),
          isDismissible: true,
        })
      }

      expect(useNotificationStore.getState().notifications).toHaveLength(50)
    })
  })

  describe('dismissNotification', () => {
    it('removes a notification by id', () => {
      const notification: NotificationDto = {
        id: 'test-1',
        type: 'Info',
        title: 'Test',
        message: 'Test message',
        createdAt: new Date().toISOString(),
        isDismissible: true,
      }

      const store = useNotificationStore.getState()
      store.addNotification(notification)
      store.dismissNotification('test-1')

      expect(useNotificationStore.getState().notifications).toHaveLength(0)
    })

    it('decrements unread count when dismissing unread notification', () => {
      const notification: NotificationDto = {
        id: 'test-1',
        type: 'Info',
        title: 'Test',
        message: 'Test message',
        createdAt: new Date().toISOString(),
        isDismissible: true,
      }

      const store = useNotificationStore.getState()
      store.addNotification(notification)
      expect(useNotificationStore.getState().unreadCount).toBe(1)

      store.dismissNotification('test-1')
      expect(useNotificationStore.getState().unreadCount).toBe(0)
    })

    it('does not decrement unread count when dismissing read notification', () => {
      const notification: NotificationDto = {
        id: 'test-1',
        type: 'Info',
        title: 'Test',
        message: 'Test message',
        createdAt: new Date().toISOString(),
        isDismissible: true,
      }

      const store = useNotificationStore.getState()
      store.addNotification(notification)
      store.markAsRead('test-1')
      expect(useNotificationStore.getState().unreadCount).toBe(0)

      store.dismissNotification('test-1')
      expect(useNotificationStore.getState().unreadCount).toBe(0)
    })
  })

  describe('markAsRead', () => {
    it('marks a notification as read', () => {
      const notification: NotificationDto = {
        id: 'test-1',
        type: 'Info',
        title: 'Test',
        message: 'Test message',
        createdAt: new Date().toISOString(),
        isDismissible: true,
      }

      const store = useNotificationStore.getState()
      store.addNotification(notification)
      store.markAsRead('test-1')

      expect(useNotificationStore.getState().notifications[0].isRead).toBe(true)
    })

    it('decrements unread count when marking as read', () => {
      const notification: NotificationDto = {
        id: 'test-1',
        type: 'Info',
        title: 'Test',
        message: 'Test message',
        createdAt: new Date().toISOString(),
        isDismissible: true,
      }

      const store = useNotificationStore.getState()
      store.addNotification(notification)
      expect(useNotificationStore.getState().unreadCount).toBe(1)

      store.markAsRead('test-1')
      expect(useNotificationStore.getState().unreadCount).toBe(0)
    })

    it('does not decrement unread count when already read', () => {
      const notification: NotificationDto = {
        id: 'test-1',
        type: 'Info',
        title: 'Test',
        message: 'Test message',
        createdAt: new Date().toISOString(),
        isDismissible: true,
      }

      const store = useNotificationStore.getState()
      store.addNotification(notification)
      store.markAsRead('test-1')
      store.markAsRead('test-1') // Mark again

      expect(useNotificationStore.getState().unreadCount).toBe(0)
    })
  })

  describe('markAllAsRead', () => {
    it('marks all notifications as read', () => {
      const store = useNotificationStore.getState()
      store.addNotification({
        id: 'test-1',
        type: 'Info',
        title: 'Test 1',
        message: 'Message 1',
        createdAt: new Date().toISOString(),
        isDismissible: true,
      })
      store.addNotification({
        id: 'test-2',
        type: 'Warning',
        title: 'Test 2',
        message: 'Message 2',
        createdAt: new Date().toISOString(),
        isDismissible: true,
      })

      store.markAllAsRead()

      const notifications = useNotificationStore.getState().notifications
      expect(notifications.every((n) => n.isRead)).toBe(true)
      expect(useNotificationStore.getState().unreadCount).toBe(0)
    })
  })

  describe('clearAll', () => {
    it('removes all notifications', () => {
      const store = useNotificationStore.getState()
      store.addNotification({
        id: 'test-1',
        type: 'Info',
        title: 'Test 1',
        message: 'Message 1',
        createdAt: new Date().toISOString(),
        isDismissible: true,
      })
      store.addNotification({
        id: 'test-2',
        type: 'Warning',
        title: 'Test 2',
        message: 'Message 2',
        createdAt: new Date().toISOString(),
        isDismissible: true,
      })

      store.clearAll()

      expect(useNotificationStore.getState().notifications).toHaveLength(0)
      expect(useNotificationStore.getState().unreadCount).toBe(0)
    })
  })

  describe('setNotifications', () => {
    it('replaces all notifications', () => {
      const notifications: NotificationDto[] = [
        {
          id: 'test-1',
          type: 'Info',
          title: 'Test 1',
          message: 'Message 1',
          createdAt: new Date().toISOString(),
          isDismissible: true,
        },
        {
          id: 'test-2',
          type: 'Warning',
          title: 'Test 2',
          message: 'Message 2',
          createdAt: new Date().toISOString(),
          isDismissible: true,
        },
      ]

      useNotificationStore.getState().setNotifications(notifications)

      const state = useNotificationStore.getState()
      expect(state.notifications).toHaveLength(2)
      expect(state.unreadCount).toBe(2)
    })
  })

  describe('preferences', () => {
    it('updates showToasts preference', () => {
      useNotificationStore.getState().setPreference('showToasts', false)

      expect(useNotificationStore.getState().preferences.showToasts).toBe(false)
    })

    it('updates autoDismissDuration preference', () => {
      useNotificationStore.getState().setPreference('autoDismissDuration', 10000)

      expect(useNotificationStore.getState().preferences.autoDismissDuration).toBe(10000)
    })

    it('updates soundEnabled preference', () => {
      useNotificationStore.getState().setPreference('soundEnabled', true)

      expect(useNotificationStore.getState().preferences.soundEnabled).toBe(true)
    })
  })

  describe('getNotificationsByProject', () => {
    it('returns notifications filtered by projectId', () => {
      const store = useNotificationStore.getState()
      store.addNotification({
        id: 'test-1',
        type: 'Info',
        title: 'Project 1 Notification',
        message: 'Message 1',
        projectId: 'project-1',
        createdAt: new Date().toISOString(),
        isDismissible: true,
      })
      store.addNotification({
        id: 'test-2',
        type: 'Warning',
        title: 'Project 2 Notification',
        message: 'Message 2',
        projectId: 'project-2',
        createdAt: new Date().toISOString(),
        isDismissible: true,
      })
      store.addNotification({
        id: 'test-3',
        type: 'Info',
        title: 'Global Notification',
        message: 'Message 3',
        createdAt: new Date().toISOString(),
        isDismissible: true,
      })

      const project1Notifications = useNotificationStore
        .getState()
        .getNotificationsByProject('project-1')

      expect(project1Notifications).toHaveLength(2) // project-1 + global
      expect(project1Notifications.map((n) => n.id)).toContain('test-1')
      expect(project1Notifications.map((n) => n.id)).toContain('test-3')
    })

    it('returns all notifications when no projectId specified', () => {
      const store = useNotificationStore.getState()
      store.addNotification({
        id: 'test-1',
        type: 'Info',
        title: 'Test 1',
        message: 'Message 1',
        projectId: 'project-1',
        createdAt: new Date().toISOString(),
        isDismissible: true,
      })
      store.addNotification({
        id: 'test-2',
        type: 'Warning',
        title: 'Test 2',
        message: 'Message 2',
        createdAt: new Date().toISOString(),
        isDismissible: true,
      })

      const notifications = useNotificationStore.getState().getNotificationsByProject()

      expect(notifications).toHaveLength(2)
    })
  })
})
