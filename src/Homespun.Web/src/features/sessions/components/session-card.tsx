import { Link } from '@tanstack/react-router'
import { MessageSquare, Square, FileCheck } from 'lucide-react'
import { Card, CardHeader, CardTitle, CardDescription, CardAction } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { StatusIndicator } from './status-indicator'
import { ClaudeSessionStatus, SessionMode, SessionType } from '@/api'
import type {
  SessionSummary,
  ClaudeSessionStatus as ClaudeSessionStatusType,
} from '@/api/generated/types.gen'

interface SessionCardProps {
  session: SessionSummary
  entityTitle?: string
  entityType?: 'issue' | 'pr'
  projectName?: string
  messageCount?: number
  onStop?: (sessionId: string) => void
  isStopPending?: boolean
}

function getStatusLabel(status: ClaudeSessionStatusType | undefined): string {
  switch (status) {
    case ClaudeSessionStatus.STARTING:
      return 'Starting'
    case ClaudeSessionStatus.RUNNING_HOOKS:
      return 'Running Hooks'
    case ClaudeSessionStatus.RUNNING:
      return 'Running'
    case ClaudeSessionStatus.WAITING_FOR_INPUT:
      return 'Waiting'
    case ClaudeSessionStatus.WAITING_FOR_QUESTION_ANSWER:
      return 'Question'
    case ClaudeSessionStatus.WAITING_FOR_PLAN_EXECUTION:
      return 'Plan Ready'
    case ClaudeSessionStatus.STOPPED:
      return 'Stopped'
    case ClaudeSessionStatus.ERROR:
      return 'Error'
    default:
      return 'Unknown'
  }
}

function getStatusVariant(
  status: ClaudeSessionStatusType | undefined
): 'default' | 'secondary' | 'destructive' | 'outline' {
  switch (status) {
    case ClaudeSessionStatus.RUNNING:
    case ClaudeSessionStatus.RUNNING_HOOKS:
      return 'default'
    case ClaudeSessionStatus.STARTING:
    case ClaudeSessionStatus.WAITING_FOR_INPUT:
    case ClaudeSessionStatus.WAITING_FOR_QUESTION_ANSWER:
    case ClaudeSessionStatus.WAITING_FOR_PLAN_EXECUTION:
      return 'secondary'
    case ClaudeSessionStatus.ERROR:
      return 'destructive'
    case ClaudeSessionStatus.STOPPED:
    default:
      return 'outline'
  }
}

function isActiveStatus(status: ClaudeSessionStatusType | undefined): boolean {
  return (
    status === ClaudeSessionStatus.STARTING ||
    status === ClaudeSessionStatus.RUNNING_HOOKS ||
    status === ClaudeSessionStatus.RUNNING ||
    status === ClaudeSessionStatus.WAITING_FOR_INPUT ||
    status === ClaudeSessionStatus.WAITING_FOR_QUESTION_ANSWER ||
    status === ClaudeSessionStatus.WAITING_FOR_PLAN_EXECUTION
  )
}

function formatRelativeTime(dateString: string | undefined): string {
  if (!dateString) return 'Unknown'

  const date = new Date(dateString)
  const now = new Date()
  const diffMs = now.getTime() - date.getTime()
  const diffSecs = Math.floor(diffMs / 1000)
  const diffMins = Math.floor(diffSecs / 60)
  const diffHours = Math.floor(diffMins / 60)
  const diffDays = Math.floor(diffHours / 24)

  if (diffDays > 0) {
    return `${diffDays} day${diffDays > 1 ? 's' : ''} ago`
  }
  if (diffHours > 0) {
    return `${diffHours} hour${diffHours > 1 ? 's' : ''} ago`
  }
  if (diffMins > 0) {
    return `${diffMins} minute${diffMins > 1 ? 's' : ''} ago`
  }
  return 'just now'
}

function getModeLabel(mode: string): string {
  return mode === SessionMode.PLAN ? 'Plan' : 'Build'
}

function getModelName(model: string | null): string {
  if (!model) return 'Unknown'

  // Extract the model type (sonnet, opus, haiku) from the full model string
  const match = model.match(/(sonnet|opus|haiku)/i)
  return match ? match[1].toLowerCase() : model
}

export function SessionCard({
  session,
  entityTitle,
  entityType,
  projectName,
  messageCount,
  onStop,
  isStopPending,
}: SessionCardProps) {
  const displayTitle = entityTitle || session.entityId || 'Unknown Entity'
  const isActive = isActiveStatus(session.status)

  const handleStopClick = (event: React.MouseEvent) => {
    event.preventDefault()
    event.stopPropagation()
    if (session.id) {
      onStop?.(session.id)
    }
  }

  const handleLinkClick = (event: React.MouseEvent) => {
    event.stopPropagation()
  }

  return (
    <Link to="/sessions/$sessionId" params={{ sessionId: session.id ?? '' }}>
      <Card className="hover:bg-muted/50 relative transition-colors">
        <CardHeader>
          <div className="flex items-start justify-between gap-2">
            <div className="min-w-0 flex-1">
              <div className="mb-1 flex items-center gap-2">
                {session.sessionType === SessionType.ISSUE_AGENT_MODIFICATION ? (
                  <Badge
                    variant="outline"
                    className="border-purple-500 bg-purple-50 text-purple-700 dark:bg-purple-950 dark:text-purple-300"
                  >
                    Issues Agent
                  </Badge>
                ) : (
                  <Badge variant="outline" className="text-xs">
                    {entityType === 'pr' ? 'PR' : 'Issue'}
                  </Badge>
                )}
                {projectName && (
                  <span className="text-muted-foreground text-xs">{projectName}</span>
                )}
                {projectName && session.entityId && (
                  <span className="text-muted-foreground text-xs">•</span>
                )}
                {session.entityId && (
                  <span className="text-muted-foreground text-xs">{session.entityId}</span>
                )}
              </div>
              <CardTitle className="line-clamp-2 text-base">{displayTitle}</CardTitle>
            </div>
            <div className="flex items-center gap-2">
              <Badge
                variant={getStatusVariant(session.status)}
                className="flex items-center gap-1.5"
              >
                <StatusIndicator status={session.status} size="sm" />
                {getStatusLabel(session.status)}
              </Badge>
            </div>
          </div>
          <CardDescription className="mt-3 space-y-2">
            <div className="flex items-center gap-4 text-xs">
              <div className="flex items-center gap-1">
                <Badge variant="secondary" className="text-xs">
                  {getModeLabel(session.mode)}
                </Badge>
              </div>
              <span className="text-muted-foreground">{getModelName(session.model)}</span>
            </div>
            <div className="text-muted-foreground flex items-center gap-4 text-xs">
              {session.createdAt && <span>Started {formatRelativeTime(session.createdAt)}</span>}
              {session.lastActivityAt && (
                <span>Active {formatRelativeTime(session.lastActivityAt)}</span>
              )}
            </div>
            {messageCount !== undefined && (
              <div className="text-muted-foreground flex items-center gap-1 text-xs">
                <MessageSquare className="h-3 w-3" />
                <span>{messageCount} messages</span>
              </div>
            )}
          </CardDescription>
          <CardAction>
            <div className="flex items-center gap-2">
              {session.sessionType === SessionType.ISSUE_AGENT_MODIFICATION && (
                <Link
                  to="/sessions/$sessionId/issue-diff"
                  params={{ sessionId: session.id ?? '' }}
                  className="inline-flex"
                  onClick={handleLinkClick}
                >
                  <Button variant="ghost" size="sm">
                    <FileCheck className="mr-1 h-3 w-3" />
                    Review Changes
                  </Button>
                </Link>
              )}
              {isActive && onStop && (
                <Button
                  variant="destructive"
                  size="icon-sm"
                  onClick={handleStopClick}
                  disabled={isStopPending}
                  aria-label="Stop session"
                >
                  <Square className="h-3 w-3" />
                </Button>
              )}
            </div>
          </CardAction>
        </CardHeader>
      </Card>
    </Link>
  )
}
