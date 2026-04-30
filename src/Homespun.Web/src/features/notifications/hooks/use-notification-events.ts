import { useEffect, useCallback } from 'react'
import { toast } from 'sonner'
import { useNotificationHub } from '@/providers/signalr-provider'
import { useNotificationStore } from '../stores/notification-store'
import type { NotificationDto, IssueChangeType } from '@/types/signalr'

interface UseNotificationEventsOptions {
  /** Project ID to join notifications group for */
  projectId?: string
}

/**
 * Hook that connects SignalR notification events to the notification store.
 * Handles:
 * - NotificationAdded events -> adds to store
 * - NotificationDismissed events -> removes from store
 * - IssuesChanged events -> creates local notifications for issue changes
 */
export function useNotificationEvents(options: UseNotificationEventsOptions = {}) {
  const { projectId } = options
  const { connection, methods, isConnected } = useNotificationHub()

  const addNotification = useNotificationStore((state) => state.addNotification)
  const dismissNotification = useNotificationStore((state) => state.dismissNotification)
  const setNotifications = useNotificationStore((state) => state.setNotifications)

  // Handle NotificationAdded event
  const handleNotificationAdded = useCallback(
    (notification: NotificationDto) => {
      addNotification(notification)
    },
    [addNotification]
  )

  // Handle NotificationDismissed event
  const handleNotificationDismissed = useCallback(
    (notificationId: string) => {
      dismissNotification(notificationId)
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
    },
    [addNotification]
  )

  // Handle AgentStarting event
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
    },
    [addNotification]
  )

  // Handle AgentStartFailed event.
  //
  // The dropdown is project-scoped (filters by current projectId), so a user who
  // dispatched an agent from /projects/foo and then navigated to /sessions or
  // /projects/bar would never see the failure surface in the bell. Pair the
  // store entry with a route-agnostic sonner toast so the failure is always
  // visible — `toast.error` renders inside the global <Toaster /> in main.tsx.
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

      toast.error('Agent Start Failed', {
        id: `agent-start-failed-${issueId}`,
        description: error,
        duration: 10_000,
      })
    },
    [addNotification]
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
