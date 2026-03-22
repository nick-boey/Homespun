import { useEffect, useRef, useCallback } from 'react'
import { toast } from 'sonner'
import { useNotificationHub } from '@/providers/signalr-provider'
import { useNotificationStore } from '../stores/notification-store'
import type { NotificationDto, IssueChangeType, NotificationType } from '@/types/signalr'

interface UseNotificationEventsOptions {
  /** Project ID to join notifications group for */
  projectId?: string
}

/**
 * Hook that connects SignalR notification events to the notification store and toasts.
 * Handles:
 * - NotificationAdded events -> adds to store and shows toast
 * - NotificationDismissed events -> removes from store
 * - IssuesChanged events -> creates local notifications for issue changes
 */
export function useNotificationEvents(options: UseNotificationEventsOptions = {}) {
  const { projectId } = options
  const { connection, methods, isConnected } = useNotificationHub()

  const addNotification = useNotificationStore((state) => state.addNotification)
  const dismissNotification = useNotificationStore((state) => state.dismissNotification)
  const setNotifications = useNotificationStore((state) => state.setNotifications)
  const preferences = useNotificationStore((state) => state.preferences)

  // Use ref to access current preferences without re-registering handlers
  const preferencesRef = useRef(preferences)
  useEffect(() => {
    preferencesRef.current = preferences
  }, [preferences])

  // Show toast for notification based on type
  const showToast = useCallback((notification: NotificationDto) => {
    const { showToasts, autoDismissDuration } = preferencesRef.current
    if (!showToasts) return

    const toastOptions = {
      id: notification.id,
      description: notification.message,
      duration: autoDismissDuration,
      dismissible: notification.isDismissible,
    }

    const toastFn = getToastFunction(notification.type)
    toastFn(notification.title, toastOptions)
  }, [])

  // Handle NotificationAdded event
  const handleNotificationAdded = useCallback(
    (notification: NotificationDto) => {
      addNotification(notification)
      showToast(notification)
    },
    [addNotification, showToast]
  )

  // Handle NotificationDismissed event
  const handleNotificationDismissed = useCallback(
    (notificationId: string) => {
      dismissNotification(notificationId)
      toast.dismiss(notificationId)
    },
    [dismissNotification]
  )

  // Handle IssuesChanged event - create local notification
  const handleIssuesChanged = useCallback(
    (issueProjectId: string, changeType: IssueChangeType, issueId: string) => {
      const notification: NotificationDto = {
        id: `issue-${issueId}-${changeType}-${Date.now()}`,
        type: 'info',
        title: `Issue ${changeType}`,
        message: getIssueChangeMessage(changeType, issueId),
        projectId: issueProjectId,
        createdAt: new Date().toISOString(),
        isDismissible: true,
        deduplicationKey: `issue-${issueId}-${changeType}`,
      }

      addNotification(notification)
      showToast(notification)
    },
    [addNotification, showToast]
  )

  // Handle AgentStarting event - show info toast
  const handleAgentStarting = useCallback(
    (issueId: string, agentProjectId: string, branchName: string) => {
      const notification: NotificationDto = {
        id: `agent-starting-${issueId}-${Date.now()}`,
        type: 'info',
        title: 'Agent Starting',
        message: `Agent is starting on branch ${branchName}...`,
        projectId: agentProjectId,
        createdAt: new Date().toISOString(),
        isDismissible: true,
        deduplicationKey: `agent-starting-${issueId}`,
      }

      addNotification(notification)
      showToast(notification)
    },
    [addNotification, showToast]
  )

  // Handle AgentStartFailed event - show error toast
  const handleAgentStartFailed = useCallback(
    (issueId: string, agentProjectId: string, error: string) => {
      const notification: NotificationDto = {
        id: `agent-start-failed-${issueId}-${Date.now()}`,
        type: 'actionRequired',
        title: 'Agent Start Failed',
        message: error,
        projectId: agentProjectId,
        createdAt: new Date().toISOString(),
        isDismissible: true,
        deduplicationKey: `agent-start-failed-${issueId}`,
      }

      addNotification(notification)
      showToast(notification)
    },
    [addNotification, showToast]
  )

  // Register/unregister event handlers
  useEffect(() => {
    if (!connection || !isConnected) return

    connection.on('NotificationAdded', handleNotificationAdded)
    connection.on('NotificationDismissed', handleNotificationDismissed)
    connection.on('IssuesChanged', handleIssuesChanged)
    connection.on('AgentStarting', handleAgentStarting)
    connection.on('AgentStartFailed', handleAgentStartFailed)

    return () => {
      connection.off('NotificationAdded', handleNotificationAdded)
      connection.off('NotificationDismissed', handleNotificationDismissed)
      connection.off('IssuesChanged', handleIssuesChanged)
      connection.off('AgentStarting', handleAgentStarting)
      connection.off('AgentStartFailed', handleAgentStartFailed)
    }
  }, [
    connection,
    isConnected,
    handleNotificationAdded,
    handleNotificationDismissed,
    handleIssuesChanged,
    handleAgentStarting,
    handleAgentStartFailed,
  ])

  // Fetch active notifications on mount and join project group
  useEffect(() => {
    if (!methods || !isConnected) return

    // Fetch initial notifications
    methods.getActiveNotifications(projectId).then((notifications) => {
      if (notifications.length > 0) {
        setNotifications(notifications)
      }
    })
  }, [methods, isConnected, projectId, setNotifications])

  // Join/leave project group
  useEffect(() => {
    if (!methods || !isConnected || !projectId) return

    methods.joinProjectGroup(projectId)

    return () => {
      methods.leaveProjectGroup(projectId)
    }
  }, [methods, isConnected, projectId])

  return {
    dismissNotification: useCallback(
      async (id: string) => {
        dismissNotification(id)
        if (methods) {
          await methods.dismissNotification(id)
        }
      },
      [methods, dismissNotification]
    ),
  }
}

function getToastFunction(type: NotificationType) {
  switch (type) {
    case 'warning':
      return toast.warning
    case 'actionRequired':
      return toast.error
    case 'info':
    default:
      return toast.info
  }
}

function getIssueChangeMessage(changeType: IssueChangeType, issueId: string): string {
  switch (changeType) {
    case 'created':
      return `A new issue has been created (${issueId})`
    case 'updated':
      return `Issue ${issueId} has been updated`
    case 'deleted':
      return `Issue ${issueId} has been deleted`
    default:
      return `Issue ${issueId} changed`
  }
}
