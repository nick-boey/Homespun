import { History, Clock } from 'lucide-react'
import type { ClaudeSession } from '@/types/signalr'
import type { SessionCacheSummary } from '@/api/generated'
import { useSessionHistory } from '@/features/sessions/hooks/use-session-history'
import { Skeleton } from '@/components/ui/skeleton'
import { Badge } from '@/components/ui/badge'
import { cn } from '@/lib/utils'

interface SessionHistoryTabProps {
  session: ClaudeSession
  currentSessionId?: string
  viewingHistoricalSessionId?: string | null
  onSelectSession?: (sessionId: string) => void
}

function formatRelativeTime(dateString?: string): string {
  if (!dateString) return 'Unknown'
  const date = new Date(dateString)
  const now = new Date()
  const diffMs = now.getTime() - date.getTime()
  const diffMins = Math.floor(diffMs / (1000 * 60))
  const diffHours = Math.floor(diffMs / (1000 * 60 * 60))
  const diffDays = Math.floor(diffMs / (1000 * 60 * 60 * 24))

  if (diffMins < 1) return 'Just now'
  if (diffMins < 60) return `${diffMins}m ago`
  if (diffHours < 24) return `${diffHours}h ago`
  if (diffDays < 7) return `${diffDays}d ago`
  return date.toLocaleDateString()
}

function getModeColor(mode?: string): string {
  switch (mode?.toLowerCase()) {
    case 'plan':
      return 'bg-blue-500/10 text-blue-600 border-blue-500/20'
    case 'build':
      return 'bg-green-500/10 text-green-600 border-green-500/20'
    default:
      return 'bg-gray-500/10 text-gray-600 border-gray-500/20'
  }
}

interface SessionItemProps {
  session: SessionCacheSummary
  isActive: boolean
  isViewing: boolean
  onClick?: () => void
}

function SessionItem({ session, isActive, isViewing, onClick }: SessionItemProps) {
  return (
    <div
      className={cn(
        'cursor-pointer rounded-lg border p-3 transition-colors',
        'hover:bg-accent/50',
        isViewing && 'border-primary bg-primary/5',
        isActive && 'border-green-500/50'
      )}
      onClick={onClick}
      role="button"
      tabIndex={0}
      onKeyDown={(e) => {
        if ((e.key === 'Enter' || e.key === ' ') && onClick) {
          e.preventDefault()
          onClick()
        }
      }}
    >
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0 flex-1">
          <div className="flex items-center gap-2">
            <Clock className="text-muted-foreground h-3 w-3 shrink-0" />
            <span className="text-sm font-medium">{formatRelativeTime(session.createdAt)}</span>
            {isActive && (
              <Badge variant="outline" className="border-green-500/50 text-xs text-green-600">
                Active
              </Badge>
            )}
          </div>
          <div className="mt-1.5 flex flex-wrap items-center gap-2">
            <Badge
              variant="outline"
              className={cn('text-xs capitalize', getModeColor(session.mode))}
            >
              {session.mode || 'unknown'}
            </Badge>
            {session.messageCount !== undefined && session.messageCount > 0 && (
              <span className="text-muted-foreground text-xs">
                {session.messageCount} message{session.messageCount !== 1 ? 's' : ''}
              </span>
            )}
          </div>
        </div>
      </div>
    </div>
  )
}

export function SessionHistoryTab({
  session,
  currentSessionId,
  viewingHistoricalSessionId,
  onSelectSession,
}: SessionHistoryTabProps) {
  const {
    data: sessions,
    isLoading,
    error,
  } = useSessionHistory(session.projectId, session.entityId)
  const activeSessionId = currentSessionId ?? session.id

  if (isLoading) {
    return (
      <div className="space-y-2">
        {[1, 2, 3].map((i) => (
          <div key={i} className="rounded-lg border p-3">
            <div className="space-y-2">
              <Skeleton className="h-4 w-32" />
              <Skeleton className="h-3 w-48" />
            </div>
          </div>
        ))}
      </div>
    )
  }

  if (error) {
    return (
      <div className="text-muted-foreground flex flex-col items-center justify-center py-8">
        <History className="mb-3 h-12 w-12 opacity-50" />
        <p>Failed to load session history</p>
      </div>
    )
  }

  if (!sessions || sessions.length === 0) {
    return (
      <div className="text-muted-foreground flex flex-col items-center justify-center py-8">
        <History className="mb-3 h-12 w-12 opacity-50" />
        <p>No session history</p>
      </div>
    )
  }

  return (
    <div className="space-y-4">
      {/* Summary */}
      <div className="text-muted-foreground text-sm">
        {sessions.length} session{sessions.length !== 1 ? 's' : ''}
      </div>

      {/* Session list */}
      <div className="space-y-2">
        {sessions.map((sessionItem) => (
          <SessionItem
            key={sessionItem.sessionId}
            session={sessionItem}
            isActive={sessionItem.sessionId === activeSessionId}
            isViewing={sessionItem.sessionId === viewingHistoricalSessionId}
            onClick={() => {
              if (sessionItem.sessionId && onSelectSession) {
                onSelectSession(sessionItem.sessionId)
              }
            }}
          />
        ))}
      </div>
    </div>
  )
}
