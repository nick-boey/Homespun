import { cn } from '@/lib/utils'
import type { ClaudeSessionStatus } from '@/api/generated/types.gen'

interface StatusIndicatorProps {
  status: ClaudeSessionStatus | undefined
  size?: 'sm' | 'md'
  className?: string
}

// Status enum values from backend
const SessionStatus = {
  Starting: 0,
  RunningHooks: 1,
  Running: 2,
  WaitingForInput: 3,
  WaitingForQuestionAnswer: 4,
  WaitingForPlanExecution: 5,
  Stopped: 6,
  Error: 7,
} as const

function getStatusColor(status: ClaudeSessionStatus | undefined): string {
  switch (status) {
    case SessionStatus.Starting:
      return 'bg-yellow-500'
    case SessionStatus.RunningHooks:
    case SessionStatus.Running:
      return 'bg-green-500'
    case SessionStatus.WaitingForInput:
      return 'bg-blue-500'
    case SessionStatus.WaitingForQuestionAnswer:
      return 'bg-purple-500'
    case SessionStatus.WaitingForPlanExecution:
      return 'bg-orange-500'
    case SessionStatus.Error:
      return 'bg-red-500'
    case SessionStatus.Stopped:
    default:
      return 'bg-gray-400'
  }
}

function isActiveStatus(status: ClaudeSessionStatus | undefined): boolean {
  return (
    status === SessionStatus.Starting ||
    status === SessionStatus.RunningHooks ||
    status === SessionStatus.Running ||
    status === SessionStatus.WaitingForInput ||
    status === SessionStatus.WaitingForQuestionAnswer ||
    status === SessionStatus.WaitingForPlanExecution
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
