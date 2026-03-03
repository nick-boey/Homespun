import { createFileRoute, useParams, Link } from '@tanstack/react-router'
import { useEffect, useRef } from 'react'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { useBreadcrumbSetter } from '@/hooks/use-breadcrumbs'
import { useSession, useSessionMessages, MessageList } from '@/features/sessions'
import { ArrowLeft, AlertCircle, RefreshCw } from 'lucide-react'

export const Route = createFileRoute('/sessions/$sessionId')({
  component: SessionChat,
})

function SessionChat() {
  const { sessionId } = useParams({ from: '/sessions/$sessionId' })
  const { session, isLoading, isNotFound, error, refetch } = useSession(sessionId)
  const scrollContainerRef = useRef<HTMLDivElement>(null)

  // Get session messages with real-time updates
  const { messages } = useSessionMessages({
    sessionId,
    initialMessages: session?.messages ?? [],
  })

  // Auto-scroll to bottom when new messages arrive
  useEffect(() => {
    if (scrollContainerRef.current) {
      scrollContainerRef.current.scrollTop = scrollContainerRef.current.scrollHeight
    }
  }, [messages])

  useBreadcrumbSetter(
    [
      { title: 'Sessions', url: '/sessions' },
      { title: session ? `Session ${sessionId.slice(0, 8)}...` : 'Loading...' },
    ],
    [sessionId, session]
  )

  // Loading state
  if (isLoading) {
    return (
      <div className="flex h-full flex-col space-y-4">
        <SessionHeader sessionId={sessionId} />
        <div className="flex flex-1 flex-col gap-4 overflow-hidden rounded-lg border p-4">
          <div className="flex justify-end">
            <Skeleton className="h-12 w-48 rounded-lg" />
          </div>
          <div className="flex justify-start">
            <Skeleton className="h-24 w-64 rounded-lg" />
          </div>
          <div className="flex justify-end">
            <Skeleton className="h-12 w-56 rounded-lg" />
          </div>
        </div>
      </div>
    )
  }

  // Session not found
  if (isNotFound) {
    return (
      <div className="flex h-full flex-col space-y-4">
        <SessionHeader sessionId={sessionId} />
        <div className="border-border flex flex-1 flex-col items-center justify-center rounded-lg border p-8">
          <AlertCircle className="text-muted-foreground mb-4 h-12 w-12" />
          <h2 className="mb-2 text-lg font-semibold">Session not found</h2>
          <p className="text-muted-foreground mb-4">
            The session you are looking for does not exist or has been deleted.
          </p>
          <Button asChild>
            <Link to="/sessions">View all sessions</Link>
          </Button>
        </div>
      </div>
    )
  }

  // Error state
  if (error) {
    return (
      <div className="flex h-full flex-col space-y-4">
        <SessionHeader sessionId={sessionId} />
        <div className="border-border flex flex-1 flex-col items-center justify-center rounded-lg border p-8">
          <AlertCircle className="text-destructive mb-4 h-12 w-12" />
          <h2 className="mb-2 text-lg font-semibold">Error loading session</h2>
          <p className="text-muted-foreground mb-4">{error}</p>
          <Button onClick={() => refetch()}>
            <RefreshCw className="mr-2 h-4 w-4" />
            Retry
          </Button>
        </div>
      </div>
    )
  }

  return (
    <div className="flex h-full flex-col space-y-4">
      <SessionHeader sessionId={sessionId} session={session} />
      <div
        ref={scrollContainerRef}
        className="border-border flex-1 overflow-y-auto rounded-lg border"
      >
        <MessageList messages={messages} isLoading={isLoading} />
      </div>
    </div>
  )
}

interface SessionHeaderProps {
  sessionId: string
  session?: {
    mode: string
    status: string
    model: string
  } | null
}

function SessionHeader({ sessionId, session }: SessionHeaderProps) {
  return (
    <div className="flex items-center justify-between">
      <div className="flex items-center gap-4">
        <Button variant="ghost" size="icon" asChild>
          <Link to="/sessions">
            <ArrowLeft className="h-4 w-4" />
          </Link>
        </Button>
        <div>
          <h1 className="text-2xl font-semibold">Session {sessionId.slice(0, 8)}...</h1>
          {session && (
            <div className="text-muted-foreground flex items-center gap-2 text-sm">
              <span className="capitalize">{session.mode}</span>
              <span>•</span>
              <span>{session.model}</span>
              <span>•</span>
              <SessionStatusBadge status={session.status} />
            </div>
          )}
        </div>
      </div>
    </div>
  )
}

function SessionStatusBadge({ status }: { status: string }) {
  const getStatusColor = (status: string) => {
    switch (status) {
      case 'Running':
      case 'RunningHooks':
        return 'bg-green-500/20 text-green-700'
      case 'WaitingForInput':
      case 'WaitingForQuestionAnswer':
      case 'WaitingForPlanExecution':
        return 'bg-yellow-500/20 text-yellow-700'
      case 'Stopped':
        return 'bg-gray-500/20 text-gray-700'
      case 'Error':
        return 'bg-red-500/20 text-red-700'
      default:
        return 'bg-blue-500/20 text-blue-700'
    }
  }

  return (
    <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${getStatusColor(status)}`}>
      {status}
    </span>
  )
}
