import { cn } from '@/lib/utils'
import { ClaudeSessionStatus } from '@/api'

interface StatusIndicatorProps {
  status: ClaudeSessionStatus | undefined
  size?: 'sm' | 'md'
  className?: string
}

function getStatusColor(status: ClaudeSessionStatus | undefined): string {
  switch (status) {
    case ClaudeSessionStatus.STARTING:
      return 'bg-yellow-500'
    case ClaudeSessionStatus.RUNNING_HOOKS:
    case ClaudeSessionStatus.RUNNING:
      return 'bg-green-500'
    case ClaudeSessionStatus.WAITING_FOR_INPUT:
      return 'bg-yellow-500'
    case ClaudeSessionStatus.WAITING_FOR_QUESTION_ANSWER:
      return 'bg-purple-500'
    case ClaudeSessionStatus.WAITING_FOR_PLAN_EXECUTION:
      return 'bg-orange-500'
    case ClaudeSessionStatus.ERROR:
      return 'bg-red-500'
    case ClaudeSessionStatus.STOPPED:
    default:
      return 'bg-gray-400'
  }
}

function isActiveStatus(status: ClaudeSessionStatus | undefined): boolean {
  return (
    status === ClaudeSessionStatus.STARTING ||
    status === ClaudeSessionStatus.RUNNING_HOOKS ||
    status === ClaudeSessionStatus.RUNNING ||
    status === ClaudeSessionStatus.WAITING_FOR_INPUT ||
    status === ClaudeSessionStatus.WAITING_FOR_QUESTION_ANSWER ||
    status === ClaudeSessionStatus.WAITING_FOR_PLAN_EXECUTION
  )
}

export function StatusIndicator({ status, size = 'md', className }: StatusIndicatorProps) {
  const isActive = isActiveStatus(status)
  const color = getStatusColor(status)
  const sizeClasses = size === 'sm' ? 'h-1.5 w-1.5' : 'h-2 w-2'

  return (
    <span
      className={cn('relative inline-flex items-center justify-center', className)}
      data-testid="status-indicator"
    >
      {isActive && (
        <span
          className={cn(
            'absolute inline-flex h-full w-full animate-pulse rounded-full opacity-75',
            color,
            sizeClasses
          )}
        />
      )}
      <span className={cn('relative inline-flex rounded-full', color, sizeClasses)} />
    </span>
  )
}
