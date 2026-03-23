import { createFileRoute, useParams } from '@tanstack/react-router'
import { WorkflowList } from '@/features/workflows'

export const Route = createFileRoute('/projects/$projectId/workflows/')({
  component: WorkflowsPage,
})

function WorkflowsPage() {
  const { projectId } = useParams({ from: '/projects/$projectId/workflows/' })

  return <WorkflowList projectId={projectId} />
}
