import { createFileRoute, useParams } from '@tanstack/react-router'
import { BranchesList } from '@/features/branches'
import { useProject } from '@/features/projects'
import { Skeleton } from '@/components/ui/skeleton'

export const Route = createFileRoute('/projects/$projectId/branches')({
  component: Branches,
})

function Branches() {
  const { projectId } = useParams({ from: '/projects/$projectId/branches' })
  const { project, isLoading, isError } = useProject(projectId)

  if (isLoading) {
    return (
      <div className="space-y-4">
        <div className="flex items-center justify-between">
          <Skeleton className="h-6 w-40" />
          <Skeleton className="h-9 w-28" />
        </div>
        <div className="space-y-3">
          <Skeleton className="h-24 w-full" />
          <Skeleton className="h-24 w-full" />
          <Skeleton className="h-24 w-full" />
        </div>
      </div>
    )
  }

  if (isError || !project?.localPath) {
    return (
      <div className="border-border rounded-lg border p-8 text-center">
        <p className="text-muted-foreground">
          Unable to load branches. Please make sure the project is properly configured.
        </p>
      </div>
    )
  }

  return <BranchesList projectId={projectId} repoPath={project.localPath} />
}
