import { useState, useMemo } from 'react'
import { Bell, CheckCheck, Trash2 } from 'lucide-react'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { cn } from '@/lib/utils'
import { useNotificationStore } from '../stores/notification-store'
import { NotificationItem } from './notification-item'

interface NotificationDropdownProps {
  projectId?: string
  className?: string
}

export function NotificationDropdown({ projectId, className }: NotificationDropdownProps) {
  const [open, setOpen] = useState(false)

  const allNotifications = useNotificationStore((state) => state.notifications)
  const notifications = useMemo(() => {
    if (!projectId) return allNotifications
    return allNotifications.filter((n) => n.projectId === projectId || !n.projectId)
  }, [allNotifications, projectId])
  const unreadCount = useNotificationStore((state) => state.unreadCount)
  const dismissNotification = useNotificationStore((state) => state.dismissNotification)
  const markAsRead = useNotificationStore((state) => state.markAsRead)
  const markAllAsRead = useNotificationStore((state) => state.markAllAsRead)
  const clearAll = useNotificationStore((state) => state.clearAll)

  const displayCount = unreadCount > 9 ? '9+' : unreadCount.toString()

  return (
    <DropdownMenu open={open} onOpenChange={setOpen}>
      <DropdownMenuTrigger asChild>
        <Button
          variant="ghost"
          size="icon"
          className={cn('relative', className)}
          aria-label="Notifications"
        >
          <Bell className="h-5 w-5" />
          {unreadCount > 0 && (
            <span
              data-testid="unread-badge"
              className="bg-primary text-primary-foreground absolute -top-1 -right-1 flex h-5 min-w-5 items-center justify-center rounded-full px-1 text-[10px] font-medium"
            >
              {displayCount}
            </span>
          )}
        </Button>
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="w-80 p-0">
        {/* Header */}
        <div className="flex items-center justify-between p-3">
          <DropdownMenuLabel className="p-0 text-base font-semibold">
            Notifications
          </DropdownMenuLabel>
          <div className="flex items-center gap-1">
            {unreadCount > 0 && (
              <Button
                variant="ghost"
                size="sm"
                className="h-7 px-2 text-xs"
                onClick={(e) => {
                  e.preventDefault()
                  markAllAsRead()
                }}
                aria-label="Mark all as read"
              >
                <CheckCheck className="mr-1 h-3.5 w-3.5" />
                Mark all as read
              </Button>
            )}
          </div>
        </div>
        <DropdownMenuSeparator className="my-0" />

        {/* Notifications list */}
        <div className="max-h-96 overflow-y-auto">
          {notifications.length === 0 ? (
            <div className="text-muted-foreground flex flex-col items-center justify-center py-8">
              <Bell className="mb-2 h-8 w-8" />
              <p className="text-sm">No notifications</p>
            </div>
          ) : (
            <div className="divide-y">
              {notifications.map((notification) => (
                <NotificationItem
                  key={notification.id}
                  notification={notification}
                  onDismiss={dismissNotification}
                  onMarkAsRead={markAsRead}
                />
              ))}
            </div>
          )}
        </div>

        {/* Footer */}
        {notifications.length > 0 && (
          <>
            <DropdownMenuSeparator className="my-0" />
            <div className="p-2">
              <Button
                variant="ghost"
                size="sm"
                className="text-muted-foreground hover:text-destructive w-full justify-center"
                onClick={(e) => {
                  e.preventDefault()
                  clearAll()
                }}
                aria-label="Clear all"
              >
                <Trash2 className="mr-2 h-4 w-4" />
                Clear all
              </Button>
            </div>
          </>
        )}
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
