import { Link } from '@tanstack/react-router'
import { Circle } from 'lucide-react'
import { TextShimmer } from '@/components/ui/text-shimmer'
import { Badge } from '@/components/ui/badge'
import { useActiveSessionCount } from '../hooks'
import { cn } from '@/lib/utils'

interface ActiveAgentsIndicatorProps {
  projectId: string
  className?: string
}

/**
 * Header indicator showing the count of active agents.
 * Shows TextShimmer effect when agents are actively processing.
 * Clicking navigates to the sessions list.
 */
export function ActiveAgentsIndicator({ projectId, className }: ActiveAgentsIndicatorProps) {
  const { count, hasActive, isProcessing, isLoading } = useActiveSessionCount(projectId)

  // Build the sessions URL
  const sessionsUrl = `/projects/${projectId}/sessions`

  if (isLoading) {
    return (
      <div className={cn('flex items-center gap-2', className)}>
        <Circle data-testid="status-indicator" className="h-2 w-2 fill-current text-gray-400" />
        <span className="text-muted-foreground text-sm">Loading...</span>
      </div>
    )
  }

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
