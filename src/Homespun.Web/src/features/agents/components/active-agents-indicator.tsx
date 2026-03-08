import { Link } from '@tanstack/react-router'
import { Circle } from 'lucide-react'
import { useAllSessionsCount } from '../hooks'
import { cn } from '@/lib/utils'

interface ActiveAgentsIndicatorProps {
  projectId?: string
  className?: string
}

/**
 * Header indicator showing the count of active agents.
 * Clicking navigates to the sessions list.
 */
export function ActiveAgentsIndicator({ className }: ActiveAgentsIndicatorProps) {
  // Always call both hooks to satisfy React rules
  const allSessionsData = useAllSessionsCount()

  // Build the sessions URL - global or project-specific
  const sessionsUrl = '/sessions'

  // Loading state
  const isLoading = allSessionsData.isLoading
  if (isLoading) {
    return (
      <div className={cn('flex items-center gap-2', className)}>
        <Circle data-testid="status-indicator" className="h-2 w-2 fill-current text-gray-400" />
        <span className="text-muted-foreground text-sm">Loading...</span>
      </div>
    )
  }

  const {
    workingCount,
    waitingForInputCount,
    waitingForAnswerCount,
    waitingForPlanCount,
    errorCount,
    hasActive,
  } = allSessionsData

  // Build array of status indicators to display
  type StatusIndicator = {
    count: number
    label: string
    colorClass: string
    testId: string
    showPing: boolean
  }

  const statusIndicators: StatusIndicator[] = [
    {
      count: workingCount,
      label: 'Working',
      colorClass: 'text-green-500',
      testId: 'status-working',
      showPing: true,
    },
    {
      count: waitingForInputCount,
      label: 'Waiting for input',
      colorClass: 'text-yellow-500',
      testId: 'status-waiting-input',
      showPing: false,
    },
    {
      count: waitingForAnswerCount,
      label: 'Waiting for answer',
      colorClass: 'text-purple-500',
      testId: 'status-waiting-answer',
      showPing: false,
    },
    {
      count: waitingForPlanCount,
      label: 'Waiting for plan',
      colorClass: 'text-orange-500',
      testId: 'status-waiting-plan',
      showPing: false,
    },
    {
      count: errorCount,
      label: 'Error',
      colorClass: 'text-red-500',
      testId: 'status-error',
      showPing: false,
    },
  ].filter((status) => status.count > 0)

  // Don't render anything if no active sessions
  if (!hasActive || statusIndicators.length === 0) {
    return null
  }

  // Render status indicators
  return (
    <Link
      to={sessionsUrl}
      data-testid="status-indicator"
      className={cn('flex items-center gap-3 transition-colors hover:opacity-80', className)}
    >
      {statusIndicators.map((status) => (
        <div key={status.testId} className="flex items-center gap-1">
          {status.showPing ? (
            <span className="relative flex h-2 w-2" data-testid={status.testId}>
              <span
                className={cn(
                  'absolute inline-flex h-full w-full animate-ping rounded-full opacity-75',
                  status.colorClass === 'text-green-500' && 'bg-green-400'
                )}
              />
              <Circle
                className={cn('relative h-2 w-2 animate-pulse fill-current', status.colorClass)}
              />
            </span>
          ) : (
            <Circle
              data-testid={status.testId}
              className={cn('h-2 w-2 animate-pulse fill-current', status.colorClass)}
            />
          )}
          <span
            data-testid={`${status.testId}-count`}
            className={cn('text-xs font-medium', status.colorClass)}
          >
            {status.count}
          </span>
        </div>
      ))}
    </Link>
  )
}
