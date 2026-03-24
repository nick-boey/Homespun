import { createFileRoute, useParams } from '@tanstack/react-router'
import { WorkflowList } from '@/features/workflows'

export const Route = createFileRoute('/projects/$projectId/workflows/')({
  component: WorkflowsPage,
})

function WorkflowsPage() {
  const { projectId } = useParams({ from: '/projects/$projectId/workflows/' })

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-bold">Workflows</h1>
      <WorkflowList projectId={projectId} />
    </div>
  )
}
