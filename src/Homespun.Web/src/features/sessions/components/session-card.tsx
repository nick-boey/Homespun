import { Card, CardContent, CardHeader } from '@/components/ui/card'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Square } from 'lucide-react'
import type { SessionSummary } from '@/api'
import { useIssueByEntityId } from '../hooks/use-issue-by-entity-id'
import { useIssuePrStatus } from '../hooks/use-issue-pr-status'
import { useStopSession } from '../hooks/use-sessions'
import { useNavigate } from '@tanstack/react-router'
import { SessionCardSkeleton } from './session-card-skeleton'
import { cn } from '@/lib/utils'
import type { ClaudeSessionStatus } from '@/api/generated'
import { getStatusLabel as getIssueStatusLabel, getTypeLabel } from '@/lib/issue-constants'

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

function getSessionStatusLabel(status: ClaudeSessionStatus | undefined): string {
  switch (status) {
    case SessionStatus.Starting:
      return 'Starting'
    case SessionStatus.RunningHooks:
      return 'Running Hooks'
    case SessionStatus.Running:
      return 'Running'
    case SessionStatus.WaitingForInput:
      return 'Waiting'
    case SessionStatus.WaitingForQuestionAnswer:
      return 'Question'
    case SessionStatus.WaitingForPlanExecution:
      return 'Plan Ready'
    case SessionStatus.Stopped:
      return 'Stopped'
    case SessionStatus.Error:
      return 'Error'
    default:
      return 'Unknown'
  }
}

function getAgentStatusVariant(status: ClaudeSessionStatus | undefined): string {
  switch (status) {
    case SessionStatus.Running:
    case SessionStatus.RunningHooks:
      return 'running'
    case SessionStatus.Starting:
    case SessionStatus.WaitingForInput:
    case SessionStatus.WaitingForQuestionAnswer:
    case SessionStatus.WaitingForPlanExecution:
      return 'idle'
    case SessionStatus.Stopped:
    case SessionStatus.Error:
      return 'stopped'
    default:
      return 'unknown'
  }
}

function isActiveStatus(status: ClaudeSessionStatus | undefined): boolean {
  return status !== SessionStatus.Stopped && status !== SessionStatus.Error
}

// Simple time ago formatter
function formatTimeAgo(date: string | Date): string {
  const now = new Date()
  const then = new Date(date)
  const seconds = Math.floor((now.getTime() - then.getTime()) / 1000)

  if (seconds < 60) return 'just now'
  const minutes = Math.floor(seconds / 60)
  if (minutes < 60) return `${minutes}m ago`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours}h ago`
  const days = Math.floor(hours / 24)
  if (days < 7) return `${days}d ago`
  const weeks = Math.floor(days / 7)
  if (weeks < 4) return `${weeks}w ago`
  const months = Math.floor(days / 30)
  return `${months}mo ago`
}

interface SessionCardProps {
  session: SessionSummary
}

export function SessionCard({ session }: SessionCardProps) {
  const navigate = useNavigate()
  const stopSession = useStopSession()

  // Fetch issue data based on entityId
  const {
    issue,
    isLoading: issueLoading,
    error: issueError,
  } = useIssueByEntityId(session.entityId || '', session.projectId || '')

  // Parse issue ID from entityId for PR status query
  const issueId = session.entityId?.split(':')[1] || ''
  const { prStatus, isLoading: prLoading } = useIssuePrStatus(session.projectId || '', issueId)

  // Show skeleton while loading
  if (issueLoading || prLoading) {
    return <SessionCardSkeleton />
  }

  const handleCardClick = () => {
    navigate({ to: `/projects/${session.projectId}/sessions/${session.id}` })
  }

  const handleStopClick = (e: React.MouseEvent) => {
    e.stopPropagation() // Prevent card navigation
    if (session.id) {
      stopSession.mutate(session.id)
    }
  }

  return (
    <Card
      data-testid="session-card"
      className="hover:bg-muted/50 cursor-pointer rounded-lg border p-4 transition-colors"
      onClick={handleCardClick}
    >
      <CardHeader className="space-y-2 p-0">
        {/* Title and stop button row */}
        <div className="flex items-start justify-between gap-2">
          <h3 className="truncate text-base leading-tight font-semibold">
            {issueError ? (
              <span className="text-destructive">Failed to load issue details</span>
            ) : issue ? (
              issue.title
            ) : (
              session.entityId
            )}
          </h3>
          {isActiveStatus(session.status) && (
            <Button
              size="icon"
              variant="ghost"
              onClick={handleStopClick}
              aria-label="Stop session"
              className="h-8 w-8 flex-shrink-0"
            >
              <Square className="h-4 w-4 fill-current" />
            </Button>
          )}
        </div>

        {/* Badges row */}
        <div className="flex items-center gap-2">
          {issue && (
            <>
              <Badge variant="secondary" className="capitalize">
                {getTypeLabel(issue.type)}
              </Badge>
              <Badge variant="outline">{getIssueStatusLabel(issue.status)}</Badge>
            </>
          )}
          {prStatus?.prNumber && <Badge variant="outline">PR #{prStatus.prNumber}</Badge>}
        </div>
      </CardHeader>

      <CardContent className="space-y-4 p-0">
        {/* Description */}
        {issue?.description && (
          <p
            data-testid="issue-description"
            className="text-muted-foreground line-clamp-2 pt-3 text-sm"
          >
            {issue.description.length > 150
              ? `${issue.description.substring(0, 150)}...`
              : issue.description}
          </p>
        )}

        {/* Divider */}
        <div className="border-t" />

        {/* Session info */}
        <div className="space-y-2">
          {/* Agent status row */}
          <div className="flex items-center gap-2 text-sm">
            <div
              data-testid="agent-status-indicator"
              data-status={getAgentStatusVariant(session.status)}
              className={cn(
                'h-3 w-3 rounded-full',
                (session.status === SessionStatus.Running ||
                  session.status === SessionStatus.RunningHooks) &&
                  'animate-pulse bg-green-500',
                (session.status === SessionStatus.Starting ||
                  session.status === SessionStatus.WaitingForInput ||
                  session.status === SessionStatus.WaitingForQuestionAnswer ||
                  session.status === SessionStatus.WaitingForPlanExecution) &&
                  'bg-yellow-500',
                (session.status === SessionStatus.Stopped ||
                  session.status === SessionStatus.Error) &&
                  'bg-gray-500'
              )}
            />
            <span className="text-muted-foreground">{getSessionStatusLabel(session.status)}</span>
            <span className="text-muted-foreground">•</span>
            <span className="text-muted-foreground">{session.messageCount || 0} messages</span>
          </div>

          {/* Mode, model, and time row */}
          <div className="flex items-center gap-2 text-sm">
            <Badge variant="outline" className="text-xs">
              {session.mode === 0 ? 'Plan' : 'Build'}
            </Badge>
            <span className="text-muted-foreground truncate">{session.model}</span>
            <span className="text-muted-foreground">•</span>
            <span className="text-muted-foreground">
              {formatTimeAgo(
                session.lastActivityAt || session.createdAt || new Date().toISOString()
              )}
            </span>
          </div>
        </div>
      </CardContent>
    </Card>
  )
}
