import { createFileRoute, useParams } from '@tanstack/react-router'
import { WorkflowExecutionView } from '@/features/workflows'

export const Route = createFileRoute(
  '/projects/$projectId/workflows/$workflowId/executions/$executionId'
)({
  component: ExecutionDetailPage,
})

function ExecutionDetailPage() {
  const { projectId, workflowId, executionId } = useParams({
    from: '/projects/$projectId/workflows/$workflowId/executions/$executionId',
  })

  return (
    <WorkflowExecutionView
      executionId={executionId}
      projectId={projectId}
      workflowId={workflowId}
    />
  )
}
