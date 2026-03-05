import { createFileRoute, useParams, Link } from '@tanstack/react-router'
import { useEffect, useRef, useCallback, useState } from 'react'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { useBreadcrumbSetter } from '@/hooks/use-breadcrumbs'
import {
  useSession,
  useSessionMessages,
  MessageList,
  ChatInput,
  usePlanApproval,
  useApprovePlan,
  PlanApprovalPanel,
} from '@/features/sessions'
import { useAnswerQuestion } from '@/features/questions'
import { useClaudeCodeHub } from '@/providers/signalr-provider'
import { ArrowLeft, AlertCircle, RefreshCw } from 'lucide-react'
import { ScrollToBottom } from '@/components/ui/scroll-to-bottom'
import { Sessions, SessionMode as ApiSessionMode } from '@/api'
import { toast } from 'sonner'
import type { ModelSelection } from '@/stores/chat-input-store'
import type { SessionMode } from '@/types/signalr'

export const Route = createFileRoute('/sessions/$sessionId')({
  component: SessionChat,
})

function SessionChat() {
  const { sessionId } = useParams({ from: '/sessions/$sessionId' })
  const { session, isLoading, isNotFound, error, refetch } = useSession(sessionId)
  const { isConnected } = useClaudeCodeHub()
  const scrollContainerRef = useRef<HTMLDivElement>(null)
  const [isSending, setIsSending] = useState(false)

  // Get session messages with real-time updates
  const { messages } = useSessionMessages({
    sessionId,
    initialMessages: session?.messages ?? [],
  })

  // Handle question answering
  const { answerQuestion, isSubmitting: isSubmittingAnswer } = useAnswerQuestion({
    sessionId,
  })

  // Plan approval state and actions (must be called before early returns)
  const { hasPendingPlan, planContent, planFilePath } = usePlanApproval(sessionId, session)
  const {
    approveClearContext,
    approveKeepContext,
    reject,
    isLoading: isApprovingPlan,
    error: approvalError,
  } = useApprovePlan(sessionId)

  // Determine if the session is processing (not accepting input)
  const isProcessing =
    session?.status === 'Running' ||
    session?.status === 'RunningHooks' ||
    session?.status === 'Starting'

  // Handle sending messages
  const handleSend = useCallback(
    async (message: string, sessionMode: SessionMode, _model: ModelSelection) => {
      if (!isConnected) return

      setIsSending(true)

      try {
        // Map our string SessionMode to API's numeric enum
        const apiMode: ApiSessionMode = sessionMode === 'Plan' ? 1 : 0

        await Sessions.postApiSessionsByIdMessages({
          path: { id: sessionId },
          body: { message, mode: apiMode },
        })
      } catch (error) {
        const errorMessage =
          error && typeof error === 'object' && 'status' in error && error.status === 404
            ? 'Session not found'
            : 'Failed to send message'
        toast.error(errorMessage)
      } finally {
        setIsSending(false)
      }
    },
    [isConnected, sessionId]
  )

  // Auto-scroll to bottom when new messages arrive or when pending question appears
  useEffect(() => {
    if (scrollContainerRef.current) {
      scrollContainerRef.current.scrollTop = scrollContainerRef.current.scrollHeight
    }
  }, [messages, session?.pendingQuestion])

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
    <div className="flex h-full flex-col">
      <SessionHeader sessionId={sessionId} session={session} />
      {/* Messages area - flex-1 takes remaining space */}
      <div className="relative mt-4 min-h-0 flex-1">
        <div
          ref={scrollContainerRef}
          className="border-border absolute inset-0 overflow-y-auto rounded-lg border"
        >
          <MessageList
            messages={messages}
            isLoading={isLoading}
            pendingQuestion={session?.pendingQuestion}
            onAnswerQuestion={answerQuestion}
            isSubmittingAnswer={isSubmittingAnswer}
          />
          {/* Plan approval panel displayed inline after messages */}
          {hasPendingPlan && planContent && (
            <div className="p-4">
              <PlanApprovalPanel
                planContent={planContent}
                planFilePath={planFilePath}
                onApproveClearContext={approveClearContext}
                onApproveKeepContext={approveKeepContext}
                onReject={reject}
                isLoading={isApprovingPlan}
                error={approvalError}
              />
            </div>
          )}
        </div>
        <ScrollToBottom scrollRef={scrollContainerRef} />
      </div>
      {/* Chat input - sticky at bottom with safe area inset for mobile keyboards */}
      <div className="bg-background sticky bottom-0 mt-3 pb-[env(safe-area-inset-bottom)] md:mt-4">
        <ChatInput
          onSend={handleSend}
          disabled={isProcessing || !isConnected}
          isLoading={isSending}
          placeholder={
            !isConnected ? 'Connecting...' : isProcessing ? 'Processing...' : 'Type a message...'
          }
        />
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
    <div className="flex items-center justify-between gap-2">
      <div className="flex min-w-0 items-center gap-2 md:gap-4">
        {/* Touch-friendly back button */}
        <Button variant="ghost" size="icon" asChild className="h-10 w-10 shrink-0">
          <Link to="/sessions">
            <ArrowLeft className="h-5 w-5" />
          </Link>
        </Button>
        <div className="min-w-0">
          <h1 className="truncate text-lg font-semibold md:text-2xl">
            Session {sessionId.slice(0, 8)}...
          </h1>
          {session && (
            <div className="text-muted-foreground flex flex-wrap items-center gap-1 text-xs md:gap-2 md:text-sm">
              <span className="capitalize">{session.mode}</span>
              <span className="hidden sm:inline">•</span>
              <span className="hidden sm:inline">{session.model}</span>
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
