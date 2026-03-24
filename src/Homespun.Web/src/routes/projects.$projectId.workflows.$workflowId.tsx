import { createFileRoute, useParams } from '@tanstack/react-router'
import { WorkflowDetail } from '@/features/workflows'

export const Route = createFileRoute('/projects/$projectId/workflows/$workflowId')({
  component: WorkflowDetailPage,
})

function WorkflowDetailPage() {
  const { projectId, workflowId } = useParams({
    from: '/projects/$projectId/workflows/$workflowId',
  })

  return <WorkflowDetail projectId={projectId} workflowId={workflowId} />
}
