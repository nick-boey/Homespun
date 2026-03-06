import { Link } from '@tanstack/react-router'
import { Circle } from 'lucide-react'
import { TextShimmer } from '@/components/ui/text-shimmer'
import { Badge } from '@/components/ui/badge'
import { useActiveSessionCount, useAllSessionsCount } from '../hooks'
import { cn } from '@/lib/utils'

interface ActiveAgentsIndicatorProps {
  projectId?: string
  className?: string
}

/**
 * Header indicator showing the count of active agents.
 * Shows TextShimmer effect when agents are actively processing.
 * Clicking navigates to the sessions list.
 * If no projectId is provided, shows counts across all projects.
 */
export function ActiveAgentsIndicator({ projectId, className }: ActiveAgentsIndicatorProps) {
  // Always call both hooks to satisfy React rules
  const projectSessionData = useActiveSessionCount(projectId || '')
  const allSessionsData = useAllSessionsCount()

  // Choose data based on projectId presence
  const isGlobal = !projectId

  // Build the sessions URL - global or project-specific
  const sessionsUrl = isGlobal ? '/sessions' : `/projects/${projectId}/sessions`

  // Loading state
  const isLoading = isGlobal ? allSessionsData.isLoading : projectSessionData.isLoading
  if (isLoading) {
    return (
      <div className={cn('flex items-center gap-2', className)}>
        <Circle data-testid="status-indicator" className="h-2 w-2 fill-current text-gray-400" />
        <span className="text-muted-foreground text-sm">Loading...</span>
      </div>
    )
  }

  // For global view, we need to handle the new status breakdown
  if (isGlobal) {
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
        colorClass: 'text-blue-500',
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
        colorClass: 'text-orange-500',
        testId: 'status-waiting-answer',
        showPing: false,
      },
      {
        count: waitingForPlanCount,
        label: 'Waiting for plan',
        colorClass: 'text-purple-500',
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

    // Show idle state if no active sessions
    if (!hasActive || statusIndicators.length === 0) {
      return (
        <Link
          to={sessionsUrl}
          className={cn('flex items-center gap-2', className)}
          data-testid="status-indicator"
        >
          <Circle data-testid="status-indicator" className="h-2 w-2 fill-current text-green-500" />
          <span className="text-muted-foreground hidden text-sm sm:inline">Agent idle</span>
        </Link>
      )
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
                    status.colorClass === 'text-blue-500' && 'bg-blue-400'
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

  // Original project-specific logic
  const { count, hasActive, isProcessing } = projectSessionData

  // Idle state
  if (!hasActive) {
    return (
      <Link to={sessionsUrl} className={cn('flex items-center gap-2', className)}>
        <Circle data-testid="status-indicator" className="h-2 w-2 fill-current text-green-500" />
        <span className="text-muted-foreground hidden text-sm sm:inline">Agent idle</span>
      </Link>
    )
  }

  // Active state with processing
  if (isProcessing) {
    return (
      <Link
        to={sessionsUrl}
        className={cn('flex items-center gap-2 transition-colors hover:opacity-80', className)}
      >
        <span className="relative flex h-2 w-2">
          <span className="absolute inline-flex h-full w-full animate-ping rounded-full bg-blue-400 opacity-75" />
          <Circle
            data-testid="status-indicator"
            className="relative h-2 w-2 animate-pulse fill-current text-blue-500"
          />
        </span>
        <TextShimmer duration={2} className="text-sm font-medium">
          {count} active
        </TextShimmer>
        <Badge variant="secondary" className="h-5 min-w-5 justify-center px-1.5 text-xs">
          {count}
        </Badge>
      </Link>
    )
  }

  // Active but waiting for input
  return (
    <Link
      to={sessionsUrl}
      className={cn('flex items-center gap-2 transition-colors hover:opacity-80', className)}
    >
      <Circle
        data-testid="status-indicator"
        className="h-2 w-2 animate-pulse fill-current text-yellow-500"
      />
      <span className="text-muted-foreground hidden text-sm sm:inline">{count} waiting</span>
      <Badge variant="secondary" className="h-5 min-w-5 justify-center px-1.5 text-xs">
        {count}
      </Badge>
    </Link>
  )
}
