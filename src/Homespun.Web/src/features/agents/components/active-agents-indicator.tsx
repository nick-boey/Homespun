import { Link } from '@tanstack/react-router'
import { Circle } from 'lucide-react'
import { ClaudeSessionStatus } from '@/api'
import {
  getSessionStatusColorName,
  getSessionStatusTextColor,
  type SessionStatusColorName,
} from '@/features/sessions/utils/session-status-color'
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
  const allSessionsData = useAllSessionsCount()

  const sessionsUrl = '/sessions'

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

  type StatusIndicator = {
    count: number
    label: string
    colorName: SessionStatusColorName
    colorClass: string
    testId: string
    showPing: boolean
  }

  // Each category is keyed off a representative ClaudeSessionStatus so the
  // shared colour utility is the single source of truth.
  const indicatorBlueprints: Array<{
    count: number
    label: string
    representativeStatus: ClaudeSessionStatus
    testId: string
    showPing: boolean
  }> = [
    {
      count: workingCount,
      label: 'Working',
      representativeStatus: ClaudeSessionStatus.RUNNING,
      testId: 'status-working',
      showPing: true,
    },
    {
      count: waitingForInputCount,
      label: 'Waiting for input',
      representativeStatus: ClaudeSessionStatus.WAITING_FOR_INPUT,
      testId: 'status-waiting-input',
      showPing: false,
    },
    {
      count: waitingForAnswerCount,
      label: 'Waiting for answer',
      representativeStatus: ClaudeSessionStatus.WAITING_FOR_QUESTION_ANSWER,
      testId: 'status-waiting-answer',
      showPing: false,
    },
    {
      count: waitingForPlanCount,
      label: 'Waiting for plan',
      representativeStatus: ClaudeSessionStatus.WAITING_FOR_PLAN_EXECUTION,
      testId: 'status-waiting-plan',
      showPing: false,
    },
    {
      count: errorCount,
      label: 'Error',
      representativeStatus: ClaudeSessionStatus.ERROR,
      testId: 'status-error',
      showPing: false,
    },
  ]

  const statusIndicators: StatusIndicator[] = indicatorBlueprints
    .filter((status) => status.count > 0)
    .map((status) => {
      const colorName = getSessionStatusColorName(status.representativeStatus)!
      const colorClass = getSessionStatusTextColor(status.representativeStatus)!
      return {
        count: status.count,
        label: status.label,
        colorName,
        colorClass,
        testId: status.testId,
        showPing: status.showPing,
      }
    })

  if (!hasActive || statusIndicators.length === 0) {
    return null
  }

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
                  status.colorName === 'green' && 'bg-green-400'
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
