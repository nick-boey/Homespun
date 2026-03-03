/**
 * Connection status indicator component.
 * Shows the current SignalR connection state with visual feedback.
 */

import { useSignalRContext } from '@/providers/signalr-provider'
import type { ConnectionStatus } from '@/types/signalr'
import { cn } from '@/lib/utils'

// ============================================================================
// Status Configuration
// ============================================================================

interface StatusConfig {
  label: string
  dotColor: string
  textColor: string
  animate?: boolean
}

const STATUS_CONFIG: Record<ConnectionStatus, StatusConfig> = {
  connected: {
    label: 'Connected',
    dotColor: 'bg-green-500',
    textColor: 'text-green-600 dark:text-green-400',
    animate: false,
  },
  connecting: {
    label: 'Connecting...',
    dotColor: 'bg-yellow-500',
    textColor: 'text-yellow-600 dark:text-yellow-400',
    animate: true,
  },
  reconnecting: {
    label: 'Reconnecting...',
    dotColor: 'bg-yellow-500',
    textColor: 'text-yellow-600 dark:text-yellow-400',
    animate: true,
  },
  disconnected: {
    label: 'Disconnected',
    dotColor: 'bg-red-500',
    textColor: 'text-red-600 dark:text-red-400',
    animate: false,
  },
}

// ============================================================================
// Component Props
// ============================================================================

export interface ConnectionStatusProps {
  /** Additional CSS classes */
  className?: string
  /** Whether to show the text label */
  showLabel?: boolean
  /** Whether to show detailed status for each hub */
  showDetails?: boolean
  /** Size variant */
  size?: 'sm' | 'md' | 'lg'
}

// ============================================================================
// Status Dot Component
// ============================================================================

interface StatusDotProps {
  status: ConnectionStatus
  size: 'sm' | 'md' | 'lg'
}

function StatusDot({ status, size }: StatusDotProps) {
  const config = STATUS_CONFIG[status]
  const sizeClasses = {
    sm: 'h-2 w-2',
    md: 'h-2.5 w-2.5',
    lg: 'h-3 w-3',
  }

  return (
    <span className="relative flex">
      <span
        className={cn(
          'rounded-full',
          sizeClasses[size],
          config.dotColor,
          config.animate && 'animate-pulse'
        )}
      />
      {config.animate && (
        <span
          className={cn(
            'absolute inline-flex h-full w-full animate-ping rounded-full opacity-75',
            config.dotColor
          )}
        />
      )}
    </span>
  )
}

// ============================================================================
// Main Component
// ============================================================================

/**
 * Displays the current SignalR connection status.
 *
 * @example
 * ```tsx
 * // Simple indicator
 * <ConnectionStatus />
 *
 * // With label
 * <ConnectionStatus showLabel />
 *
 * // Detailed view showing both hubs
 * <ConnectionStatus showDetails showLabel />
 * ```
 */
export function ConnectionStatus({
  className,
  showLabel = false,
  showDetails = false,
  size = 'md',
}: ConnectionStatusProps) {
  const { claudeCodeStatus, notificationStatus, isConnected, isReconnecting, isConnecting } =
    useSignalRContext()

  // Determine overall status
  const overallStatus: ConnectionStatus = isConnected
    ? 'connected'
    : isReconnecting
      ? 'reconnecting'
      : isConnecting
        ? 'connecting'
        : 'disconnected'

  const config = STATUS_CONFIG[overallStatus]

  const textSizeClasses = {
    sm: 'text-xs',
    md: 'text-sm',
    lg: 'text-base',
  }

  if (showDetails) {
    return (
      <div className={cn('flex flex-col gap-1', className)}>
        <div className="flex items-center gap-2">
          <StatusDot status={claudeCodeStatus} size={size} />
          <span className={cn(textSizeClasses[size], STATUS_CONFIG[claudeCodeStatus].textColor)}>
            Claude Code: {STATUS_CONFIG[claudeCodeStatus].label}
          </span>
        </div>
        <div className="flex items-center gap-2">
          <StatusDot status={notificationStatus} size={size} />
          <span className={cn(textSizeClasses[size], STATUS_CONFIG[notificationStatus].textColor)}>
            Notifications: {STATUS_CONFIG[notificationStatus].label}
          </span>
        </div>
      </div>
    )
  }

  return (
    <div className={cn('flex items-center gap-2', className)}>
      <StatusDot status={overallStatus} size={size} />
      {showLabel && (
        <span className={cn(textSizeClasses[size], config.textColor)}>{config.label}</span>
      )}
    </div>
  )
}

// ============================================================================
// Compact Status Badge
// ============================================================================

export interface ConnectionStatusBadgeProps {
  className?: string
}

/**
 * A compact badge showing connection status.
 * Useful for header/footer bars.
 */
export function ConnectionStatusBadge({ className }: ConnectionStatusBadgeProps) {
  const { isConnected, isReconnecting, isConnecting } = useSignalRContext()

  const status: ConnectionStatus = isConnected
    ? 'connected'
    : isReconnecting
      ? 'reconnecting'
      : isConnecting
        ? 'connecting'
        : 'disconnected'

  const config = STATUS_CONFIG[status]

  const badgeClasses = {
    connected: 'bg-green-100 dark:bg-green-900/30 border-green-200 dark:border-green-800',
    connecting: 'bg-yellow-100 dark:bg-yellow-900/30 border-yellow-200 dark:border-yellow-800',
    reconnecting: 'bg-yellow-100 dark:bg-yellow-900/30 border-yellow-200 dark:border-yellow-800',
    disconnected: 'bg-red-100 dark:bg-red-900/30 border-red-200 dark:border-red-800',
  }

  return (
    <div
      className={cn(
        'inline-flex items-center gap-1.5 rounded-full border px-2.5 py-0.5',
        badgeClasses[status],
        className
      )}
    >
      <StatusDot status={status} size="sm" />
      <span className={cn('text-xs font-medium', config.textColor)}>{config.label}</span>
    </div>
  )
}

// ============================================================================
// Reconnection Banner
// ============================================================================

export interface ReconnectionBannerProps {
  className?: string
}

/**
 * A banner that appears when reconnecting or disconnected.
 * Can be placed at the top of the page to notify users.
 */
export function ReconnectionBanner({ className }: ReconnectionBannerProps) {
  const { isConnected, isReconnecting, claudeCodeError, notificationError, connect } =
    useSignalRContext()

  // Don't show if connected
  if (isConnected) {
    return null
  }

  const error = claudeCodeError || notificationError

  return (
    <div
      className={cn(
        'flex items-center justify-between gap-4 px-4 py-2',
        isReconnecting
          ? 'bg-yellow-50 text-yellow-800 dark:bg-yellow-900/20 dark:text-yellow-200'
          : 'bg-red-50 text-red-800 dark:bg-red-900/20 dark:text-red-200',
        className
      )}
    >
      <div className="flex items-center gap-2">
        <StatusDot status={isReconnecting ? 'reconnecting' : 'disconnected'} size="sm" />
        <span className="text-sm font-medium">
          {isReconnecting
            ? 'Connection lost. Attempting to reconnect...'
            : 'Disconnected from server'}
        </span>
        {error && <span className="text-xs opacity-75">({error})</span>}
      </div>
      {!isReconnecting && (
        <button
          onClick={() => connect()}
          className={cn(
            'rounded px-3 py-1 text-sm font-medium transition-colors',
            'bg-red-100 hover:bg-red-200 dark:bg-red-800 dark:hover:bg-red-700'
          )}
        >
          Retry
        </button>
      )}
    </div>
  )
}
