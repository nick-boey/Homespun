import { createFileRoute, useNavigate } from '@tanstack/react-router'
import { useCallback } from 'react'
import { PullRequestsTab } from '@/features/pull-requests'

export const Route = createFileRoute('/projects/$projectId/pull-requests')({
  component: PullRequestsPage,
})

function PullRequestsPage() {
  const { projectId } = Route.useParams()
  const navigate = useNavigate()

  const handleViewIssue = useCallback(
    (issueId: string) => {
      navigate({
        to: '/projects/$projectId/issues/$issueId/edit',
        params: { projectId, issueId },
      })
    },
    [navigate, projectId]
  )

  const handleStartAgent = useCallback(
    (branchName: string) => {
      // TODO: Implement starting an agent on the PR branch
      // This could navigate to a session creation page or open a modal
      console.log('Start agent on branch:', branchName)
    },
    []
  )

  return (
    <div className="h-[calc(100vh-16rem)]">
      <PullRequestsTab
        projectId={projectId}
        onViewIssue={handleViewIssue}
        onStartAgent={handleStartAgent}
      />
    </div>
  )
}
