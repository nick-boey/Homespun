import { Info, AlertTriangle, AlertCircle, X } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'
import type { AppNotification } from '../stores/notification-store'
import type { NotificationType } from '@/types/signalr'

interface NotificationItemProps {
  notification: AppNotification
  onDismiss?: (id: string) => void
  onMarkAsRead?: (id: string) => void
  onAction?: (id: string) => void
  className?: string
}

export function NotificationItem({
  notification,
  onDismiss,
  onMarkAsRead,
  onAction,
  className,
}: NotificationItemProps) {
  const handleClick = () => {
    if (!notification.isRead && onMarkAsRead) {
      onMarkAsRead(notification.id)
    }
  }

  const handleDismiss = (e: React.MouseEvent) => {
    e.stopPropagation()
    onDismiss?.(notification.id)
  }

  const handleAction = (e: React.MouseEvent) => {
    e.stopPropagation()
    onAction?.(notification.id)
  }

  return (
    <article
      onClick={handleClick}
      className={cn(
        'relative flex cursor-pointer gap-3 p-3 transition-colors',
        'hover:bg-muted/50',
        !notification.isRead && 'bg-muted/30',
        className
      )}
    >
      {/* Unread indicator */}
      {!notification.isRead && (
        <div
          data-testid="unread-indicator"
          className="bg-primary absolute top-1/2 left-1 h-2 w-2 -translate-y-1/2 rounded-full"
        />
      )}

      {/* Icon */}
      <div className="mt-0.5 flex-shrink-0">{getNotificationIcon(notification.type)}</div>

      {/* Content */}
      <div className="min-w-0 flex-1">
        <div className="flex items-start justify-between gap-2">
          <h4 className="truncate text-sm font-medium">{notification.title}</h4>
          <span className="text-muted-foreground text-xs whitespace-nowrap">
            {formatRelativeTime(notification.createdAt)}
          </span>
        </div>
        <p className="text-muted-foreground mt-0.5 line-clamp-2 text-sm">{notification.message}</p>

        {/* Action button */}
        {notification.actionLabel && (
          <Button variant="link" size="sm" className="mt-1 h-auto p-0" onClick={handleAction}>
            {notification.actionLabel}
          </Button>
        )}
      </div>

      {/* Dismiss button */}
      {notification.isDismissible && onDismiss && (
        <Button
          variant="ghost"
          size="icon"
          className="h-6 w-6 flex-shrink-0"
          onClick={handleDismiss}
          aria-label="Dismiss notification"
        >
          <X className="h-4 w-4" />
        </Button>
      )}
    </article>
  )
}

function getNotificationIcon(type: NotificationType) {
  switch (type) {
    case 'Warning':
      return (
        <AlertTriangle
          data-testid="notification-icon-warning"
          className="h-5 w-5 text-yellow-500"
        />
      )
    case 'ActionRequired':
      return <AlertCircle data-testid="notification-icon-action" className="h-5 w-5 text-red-500" />
    case 'Info':
    default:
      return <Info data-testid="notification-icon-info" className="h-5 w-5 text-blue-500" />
  }
}

function formatRelativeTime(isoString: string): string {
  const date = new Date(isoString)
  const now = new Date()
  const diffMs = now.getTime() - date.getTime()
  const diffSeconds = Math.floor(diffMs / 1000)
  const diffMinutes = Math.floor(diffSeconds / 60)
  const diffHours = Math.floor(diffMinutes / 60)
  const diffDays = Math.floor(diffHours / 24)

  if (diffSeconds < 60) {
    return 'just now'
  } else if (diffMinutes < 60) {
    return `${diffMinutes}m ago`
  } else if (diffHours < 24) {
    return `${diffHours}h ago`
  } else if (diffDays === 1) {
    return 'yesterday'
  } else if (diffDays < 7) {
    return `${diffDays}d ago`
  } else {
    return date.toLocaleDateString()
  }
}
