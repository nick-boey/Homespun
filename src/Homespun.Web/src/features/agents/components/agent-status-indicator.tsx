import { useEffect, useState } from 'react'
import { Clock, Coins } from 'lucide-react'
import { ThinkingBar } from '@/components/ui/thinking-bar'
import { Loader } from '@/components/ui/loader'
import { cn } from '@/lib/utils'
import type { ClaudeSessionStatus } from '@/api/generated/types.gen'

// Status labels
const STATUS_TEXT: Record<number, string> = {
  0: 'Starting',
  1: 'Running hooks',
  2: 'Working',
  3: 'Waiting for input',
  4: 'Paused',
  5: 'Stopped',
  6: 'Error',
  7: 'Completed',
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
  status: ClaudeSessionStatus
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
  const isWaiting = status === 3 || status === 4

  return (
    <div className={cn('flex flex-col gap-2', className)}>
      {/* Main status display */}
      <div className="flex items-center gap-3">
        {/* Starting state - show Loader */}
        {status === 0 && (
          <div className="flex items-center gap-2">
            <Loader variant="dots" size="sm" />
            <span className="text-muted-foreground text-sm">{statusText}</span>
          </div>
        )}

        {/* Running hooks state - show Loader */}
        {status === 1 && (
          <div className="flex items-center gap-2">
            <Loader variant="pulse" size="sm" />
            <span className="text-muted-foreground text-sm">{statusText}</span>
          </div>
        )}

        {/* Running state - show ThinkingBar */}
        {status === 2 && (
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
