import { useEffect, useState } from 'react'
import { Clock, Coins } from 'lucide-react'
import { ThinkingBar } from '@/components/ui/thinking-bar'
import { Loader } from '@/components/ui/loader'
import { cn } from '@/lib/utils'
import { ClaudeSessionStatus } from '@/api'
import type { ClaudeSessionStatus as ClaudeSessionStatusType } from '@/api/generated/types.gen'

// Status labels
const STATUS_TEXT: Record<string, string> = {
  [ClaudeSessionStatus.STARTING]: 'Starting',
  [ClaudeSessionStatus.RUNNING_HOOKS]: 'Running hooks',
  [ClaudeSessionStatus.RUNNING]: 'Working',
  [ClaudeSessionStatus.WAITING_FOR_INPUT]: 'Waiting for input',
  [ClaudeSessionStatus.WAITING_FOR_QUESTION_ANSWER]: 'Paused',
  [ClaudeSessionStatus.WAITING_FOR_PLAN_EXECUTION]: 'Waiting for plan',
  [ClaudeSessionStatus.STOPPED]: 'Stopped',
  [ClaudeSessionStatus.ERROR]: 'Error',
}

/** Format duration from start time to now */
function formatDuration(startTime: Date): string {
  const now = new Date()
  const diffMs = now.getTime() - startTime.getTime()
  const seconds = Math.floor(diffMs / 1000)
  const minutes = Math.floor(seconds / 60)
  const hours = Math.floor(minutes / 60)

  if (hours > 0) {
    return `${hours}h ${minutes % 60}m`
  } else if (minutes > 0) {
    return `${minutes}m ${seconds % 60}s`
  } else {
    return `${seconds}s`
  }
}

interface AgentStatusIndicatorProps {
  status: ClaudeSessionStatusType
  isActive: boolean
  onStop?: () => void
  tokenCount?: number
  startTime?: Date
  className?: string
}

/**
 * Displays the current status of an agent session using PromptKit components.
 * Shows ThinkingBar for active processing, Loader for starting states,
 * and optional token count and duration.
 */
export function AgentStatusIndicator({
  status,
  isActive,
  onStop,
  tokenCount,
  startTime,
  className,
}: AgentStatusIndicatorProps) {
  // Use a tick counter to trigger duration recalculation
  const [, setTick] = useState(0)

  // Update tick every second when active to recalculate duration
  useEffect(() => {
    if (!startTime || !isActive) {
      return
    }

    const interval = setInterval(() => {
      setTick((t) => t + 1)
    }, 1000)
    return () => clearInterval(interval)
  }, [startTime, isActive])

  // Compute duration on each render (tick changes trigger re-render)
  const duration = startTime && isActive ? formatDuration(startTime) : ''

  // Don't render anything if not active
  if (!isActive) {
    return null
  }

  const statusText = STATUS_TEXT[status] ?? 'Unknown'
  const isWaiting =
    status === ClaudeSessionStatus.WAITING_FOR_INPUT ||
    status === ClaudeSessionStatus.WAITING_FOR_QUESTION_ANSWER

  return (
    <div className={cn('flex flex-col gap-2', className)}>
      {/* Main status display */}
      <div className="flex items-center gap-3">
        {/* Starting state - show Loader */}
        {status === ClaudeSessionStatus.STARTING && (
          <div className="flex items-center gap-2">
            <Loader variant="dots" size="sm" />
            <span className="text-muted-foreground text-sm">{statusText}</span>
          </div>
        )}

        {/* Running hooks state - show Loader */}
        {status === ClaudeSessionStatus.RUNNING_HOOKS && (
          <div className="flex items-center gap-2">
            <Loader variant="pulse" size="sm" />
            <span className="text-muted-foreground text-sm">{statusText}</span>
          </div>
        )}

        {/* Running state - show ThinkingBar */}
        {status === ClaudeSessionStatus.RUNNING && (
          <ThinkingBar text={statusText} onStop={onStop} stopLabel="Stop" className="flex-1" />
        )}

        {/* Waiting states - show pulsing indicator */}
        {isWaiting && (
          <div className="flex items-center gap-2">
            <Loader variant="pulse-dot" size="sm" />
            <span className="text-muted-foreground text-sm">{statusText}</span>
          </div>
        )}
      </div>

      {/* Stats row */}
      {(tokenCount !== undefined || duration) && (
        <div className="text-muted-foreground flex items-center gap-4 text-xs">
          {/* Token count */}
          {tokenCount !== undefined && (
            <div className="flex items-center gap-1">
              <Coins className="h-3 w-3" />
              <span>{tokenCount.toLocaleString()} tokens</span>
            </div>
          )}

          {/* Duration */}
          {duration && (
            <div className="flex items-center gap-1">
              <Clock className="h-3 w-3" />
              <span>{duration}</span>
            </div>
          )}
        </div>
      )}
    </div>
  )
}
