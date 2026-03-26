import { create } from 'zustand'
import { devtools, persist } from 'zustand/middleware'
import type { NotificationDto } from '@/types/signalr'

/**
 * App notification extends the server DTO with client-side state.
 */
export interface AppNotification extends NotificationDto {
  isRead: boolean
}

/**
 * Notification preferences stored in local storage.
 */
export interface NotificationPreferences {
  soundEnabled: boolean
}

const MAX_NOTIFICATIONS = 50

interface NotificationState {
  notifications: AppNotification[]
  unreadCount: number
  preferences: NotificationPreferences

  // Actions
  addNotification: (notification: NotificationDto) => void
  dismissNotification: (id: string) => void
  markAsRead: (id: string) => void
  markAllAsRead: () => void
  clearAll: () => void
  setNotifications: (notifications: NotificationDto[]) => void
  setPreference: <K extends keyof NotificationPreferences>(
    key: K,
    value: NotificationPreferences[K]
  ) => void
  getNotificationsByProject: (projectId?: string) => AppNotification[]
}

export const useNotificationStore = create<NotificationState>()(
  devtools(
    persist(
      (set, get) => ({
        notifications: [],
        unreadCount: 0,
        preferences: {
          soundEnabled: false,
        },

        addNotification: (notification) => {
          set((state) => {
            // Check for duplicate by deduplication key
            if (notification.deduplicationKey) {
              const exists = state.notifications.some(
                (n) => n.deduplicationKey === notification.deduplicationKey
              )
              if (exists) {
                return state
              }
            }

            const appNotification: AppNotification = {
              ...notification,
              isRead: false,
            }

            // Add at beginning and limit total
            const newNotifications = [appNotification, ...state.notifications].slice(
              0,
              MAX_NOTIFICATIONS
            )

            return {
              notifications: newNotifications,
              unreadCount: state.unreadCount + 1,
            }
          })
        },

        dismissNotification: (id) => {
          set((state) => {
            const notification = state.notifications.find((n) => n.id === id)
            const newNotifications = state.notifications.filter((n) => n.id !== id)
            const unreadDecrement = notification && !notification.isRead ? 1 : 0

            return {
              notifications: newNotifications,
              unreadCount: Math.max(0, state.unreadCount - unreadDecrement),
            }
          })
        },

        markAsRead: (id) => {
          set((state) => {
            const notification = state.notifications.find((n) => n.id === id)
            if (!notification || notification.isRead) {
              return state
            }

            return {
              notifications: state.notifications.map((n) =>
                n.id === id ? { ...n, isRead: true } : n
              ),
              unreadCount: Math.max(0, state.unreadCount - 1),
            }
          })
        },

        markAllAsRead: () => {
          set((state) => ({
            notifications: state.notifications.map((n) => ({ ...n, isRead: true })),
            unreadCount: 0,
          }))
        },

        clearAll: () => {
          set({
            notifications: [],
            unreadCount: 0,
          })
        },

        setNotifications: (notifications) => {
          const appNotifications: AppNotification[] = notifications.map((n) => ({
            ...n,
            isRead: false,
          }))

          set({
            notifications: appNotifications.slice(0, MAX_NOTIFICATIONS),
            unreadCount: appNotifications.length,
          })
        },

        setPreference: (key, value) => {
          set((state) => ({
            preferences: {
              ...state.preferences,
              [key]: value,
            },
          }))
        },

        getNotificationsByProject: (projectId) => {
          const { notifications } = get()
          if (!projectId) {
            return notifications
          }
          // Return notifications for specific project OR global notifications (no projectId)
          return notifications.filter((n) => n.projectId === projectId || !n.projectId)
        },
      }),
      {
        name: 'homespun-notifications-storage',
        partialize: (state) => ({
          preferences: state.preferences,
        }),
      }
    )
  )
)
