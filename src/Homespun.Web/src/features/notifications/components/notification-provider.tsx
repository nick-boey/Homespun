import { useNotificationEvents } from '../hooks/use-notification-events'

interface NotificationProviderProps {
  projectId?: string
  children: React.ReactNode
}

/**
 * Provider component that initializes notification event handlers.
 * Should be placed in the app layout to connect SignalR events to the notification store.
 */
export function NotificationProvider({ projectId, children }: NotificationProviderProps) {
  // Initialize SignalR event handlers
  useNotificationEvents({ projectId })

  return <>{children}</>
}
