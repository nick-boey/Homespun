import { useMutation, useQueryClient } from '@tanstack/react-query'
import { Square, Pause, Play, DollarSign, Clock } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Loader } from '@/components/ui/loader'
import { ThinkingBar } from '@/components/ui/thinking-bar'
import { cn } from '@/lib/utils'
import { Sessions } from '@/api'
import { sessionsQueryKey } from '@/features/sessions/hooks/use-sessions'
import { projectSessionsQueryKey } from '../hooks'
import type { ClaudeSession, ClaudeSessionStatus } from '@/api/generated/types.gen'

const MODE_LABELS: Record<number, string> = {
  0: 'Plan',
  1: 'Build',
}

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

// Active statuses where controls should be shown
const ACTIVE_STATUSES = [0, 1, 2, 3, 4]

interface AgentControlPanelProps {
  session: ClaudeSession
  onStop?: () => void
  onPause?: () => void
  onResume?: () => void
  className?: string
}

/**
 * Control panel for managing an active agent session.
 * Displays session info, status, and provides stop/pause/resume controls.
 */
export function AgentControlPanel({
  session,
  onStop,
  onPause,
  onResume,
  className,
}: AgentControlPanelProps) {
  const queryClient = useQueryClient()

  const stopMutation = useMutation({
    mutationFn: async () => {
      await Sessions.deleteApiSessionsById({
        path: { id: session.id! },
      })
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: sessionsQueryKey })
      if (session.projectId) {
        queryClient.invalidateQueries({ queryKey: projectSessionsQueryKey(session.projectId) })
      }
      onStop?.()
    },
  })

  const pauseMutation = useMutation({
    mutationFn: async () => {
      await Sessions.postApiSessionsByIdInterrupt({
        path: { id: session.id! },
      })
    },
    onSuccess: () => {
      onPause?.()
    },
  })

  const status = session.status as ClaudeSessionStatus
  const isActive = ACTIVE_STATUSES.includes(status as number)
  const isRunning = status === 2
  const isPaused = status === 4
  const isWaiting = status === 3

  // Format duration
  const formatDuration = (ms?: number) => {
    if (!ms) return '0s'
    const seconds = Math.floor(ms / 1000)
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

  // Format cost
  const formatCost = (cost?: number) => {
    if (!cost) return '$0.00'
    return `$${cost.toFixed(2)}`
  }

  // Get model short name
  const getModelName = (model?: string | null) => {
    if (!model) return 'Unknown'
    if (model.includes('opus')) return 'Opus'
    if (model.includes('sonnet')) return 'Sonnet'
    if (model.includes('haiku')) return 'Haiku'
    return model
  }

  const handleStop = () => {
    stopMutation.mutate()
  }

  const handlePause = () => {
    pauseMutation.mutate()
  }

  return (
    <div className={cn('space-y-4', className)}>
      {/* Session info */}
      <div className="flex items-center gap-2 text-sm">
        <span className="bg-muted rounded px-2 py-0.5 font-medium">
          {getModelName(session.model)}
        </span>
        <span className="bg-muted rounded px-2 py-0.5">
          {MODE_LABELS[session.mode as number] ?? 'Unknown'}
        </span>
      </div>

      {/* Status display */}
      <div className="flex items-center gap-3">
        {/* Starting state */}
        {status === 0 && (
          <div className="flex items-center gap-2">
            <Loader variant="dots" size="sm" />
            <span className="text-muted-foreground text-sm">{STATUS_TEXT[0]}</span>
          </div>
        )}

        {/* Running hooks */}
        {status === 1 && (
          <div className="flex items-center gap-2">
            <Loader variant="pulse" size="sm" />
            <span className="text-muted-foreground text-sm">{STATUS_TEXT[1]}</span>
          </div>
        )}

        {/* Running - show ThinkingBar */}
        {isRunning && (
          <ThinkingBar
            text={STATUS_TEXT[2]}
            onStop={handleStop}
            stopLabel="Stop"
            className="flex-1"
          />
        )}

        {/* Waiting for input */}
        {isWaiting && (
          <div className="flex items-center gap-2">
            <Loader variant="pulse-dot" size="sm" />
            <span className="text-muted-foreground text-sm">{STATUS_TEXT[3]}</span>
          </div>
        )}

        {/* Paused */}
        {isPaused && (
          <div className="flex items-center gap-2">
            <Pause className="text-muted-foreground h-4 w-4" />
            <span className="text-muted-foreground text-sm">{STATUS_TEXT[4]}</span>
          </div>
        )}
      </div>

      {/* Control buttons */}
      {isActive && (
        <div className="flex items-center gap-2">
          {/* Stop button */}
          <Button
            variant="destructive"
            size="sm"
            onClick={handleStop}
            disabled={stopMutation.isPending}
            className="gap-1.5"
          >
            {stopMutation.isPending ? (
              <Loader variant="circular" size="sm" />
            ) : (
              <Square className="h-3.5 w-3.5" />
            )}
            Stop
          </Button>

          {/* Pause/Resume button */}
          {isRunning && (
            <Button
              variant="outline"
              size="sm"
              onClick={handlePause}
              disabled={pauseMutation.isPending}
              className="gap-1.5"
            >
              {pauseMutation.isPending ? (
                <Loader variant="circular" size="sm" />
              ) : (
                <Pause className="h-3.5 w-3.5" />
              )}
              Pause
            </Button>
          )}

          {isPaused && (
            <Button variant="outline" size="sm" onClick={onResume} className="gap-1.5">
              <Play className="h-3.5 w-3.5" />
              Resume
            </Button>
          )}
        </div>
      )}

      {/* Statistics */}
      <div className="text-muted-foreground flex items-center gap-4 text-xs">
        {/* Cost */}
        <div className="flex items-center gap-1">
          <DollarSign className="h-3 w-3" />
          <span>{formatCost(session.totalCostUsd)}</span>
        </div>

        {/* Duration */}
        <div className="flex items-center gap-1">
          <Clock className="h-3 w-3" />
          <span>{formatDuration(session.totalDurationMs)}</span>
        </div>
      </div>
    </div>
  )
}
