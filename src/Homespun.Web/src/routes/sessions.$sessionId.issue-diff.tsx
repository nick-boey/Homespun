import { createFileRoute, useParams, useNavigate, Link } from '@tanstack/react-router'
import { ArrowLeft, Check, X } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { Loader } from '@/components/ui/loader'
import { useSession } from '@/features/sessions'
import {
  useIssuesDiff,
  useAcceptIssues,
  useCancelIssuesSession,
  IssueDiffView,
} from '@/features/issues-agent'
import { SessionType } from '@/api'
import { toast } from 'sonner'

export const Route = createFileRoute('/sessions/$sessionId/issue-diff')({
  component: IssueDiffPage,
})

function IssueDiffPage() {
  const { sessionId } = useParams({ from: '/sessions/$sessionId/issue-diff' })
  const navigate = useNavigate()

  // Get session info
  const { session, isLoading: sessionLoading, error: sessionError } = useSession(sessionId)

  // Get diff data
  const { data: diff, isLoading: diffLoading, error: diffError } = useIssuesDiff(sessionId)

  // Mutations
  const acceptMutation = useAcceptIssues()
  const cancelMutation = useCancelIssuesSession()

  const isLoading = sessionLoading || diffLoading
  const error = sessionError || diffError

  // Validate this is an IssueModify session
  if (session && session.sessionType !== SessionType.ISSUE_MODIFY) {
    return (
      <div className="flex h-full flex-col items-center justify-center gap-4">
        <p className="text-muted-foreground">This is not an Issues Agent session.</p>
        <Link to="/sessions/$sessionId" params={{ sessionId }}>
          <Button variant="outline">Go to Session</Button>
        </Link>
      </div>
    )
  }

  const handleAccept = async () => {
    try {
      const result = await acceptMutation.mutateAsync(sessionId)
      toast.success(result?.message ?? 'Changes accepted')
      // Navigate to issues page
      if (session?.projectId) {
        navigate({
          to: '/projects/$projectId/issues',
          params: { projectId: session.projectId },
        })
      } else {
        navigate({ to: '/sessions' })
      }
    } catch {
      toast.error('Failed to accept changes')
    }
  }

  const handleCancel = async () => {
    try {
      await cancelMutation.mutateAsync(sessionId)
      toast.success('Session cancelled')
      navigate({ to: '/sessions' })
    } catch {
      toast.error('Failed to cancel session')
    }
  }

  const handleBackToSession = () => {
    navigate({ to: '/sessions/$sessionId', params: { sessionId } })
  }

  return (
    <div className="flex h-full flex-col">
      {/* Header */}
      <div className="flex items-center justify-between border-b px-4 py-3">
        <div className="flex items-center gap-3">
          <Button variant="ghost" size="icon" onClick={handleBackToSession}>
            <ArrowLeft className="h-4 w-4" />
          </Button>
          <h1 className="text-lg font-semibold">Review Issue Changes</h1>
        </div>
        <div className="flex items-center gap-2">
          <Button
            variant="outline"
            onClick={handleCancel}
            disabled={cancelMutation.isPending || acceptMutation.isPending}
          >
            {cancelMutation.isPending ? (
              <Loader variant="circular" size="sm" />
            ) : (
              <X className="mr-1 h-4 w-4" />
            )}
            Cancel
          </Button>
          <Button
            onClick={handleAccept}
            disabled={acceptMutation.isPending || cancelMutation.isPending || !diff}
          >
            {acceptMutation.isPending ? (
              <Loader variant="circular" size="sm" />
            ) : (
              <Check className="mr-1 h-4 w-4" />
            )}
            Accept Changes
          </Button>
        </div>
      </div>

      {/* Content */}
      <div className="flex-1 overflow-auto p-4">
        {isLoading && (
          <div className="flex items-center justify-center py-8">
            <Loader variant="circular" size="md" />
          </div>
        )}

        {error && (
          <div className="flex flex-col items-center justify-center gap-4 py-8">
            <p className="text-destructive">Failed to load diff: {String(error)}</p>
            <Button variant="outline" onClick={handleBackToSession}>
              Back to Session
            </Button>
          </div>
        )}

        {!isLoading && !error && diff && session && <IssueDiffView diff={diff} />}
      </div>
    </div>
  )
}
