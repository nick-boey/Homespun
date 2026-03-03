import { AlertCircle, RefreshCw } from 'lucide-react'
import { useProjects, useDeleteProject } from '../hooks/use-projects'
import { ProjectCard } from './project-card'
import { ProjectCardSkeleton } from './project-card-skeleton'
import { ProjectsEmptyState } from './projects-empty-state'
import { Button } from '@/components/ui/button'

export function ProjectsList() {
  const { data: projects, isLoading, isError, refetch } = useProjects()
  const deleteProject = useDeleteProject()

  const handleDelete = (projectId: string) => {
    deleteProject.mutate(projectId)
  }

  if (isLoading) {
    return (
      <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
        {[1, 2, 3].map((i) => (
          <ProjectCardSkeleton key={i} />
        ))}
      </div>
    )
  }

  if (isError) {
    return (
      <div className="border-destructive/50 bg-destructive/10 flex flex-col items-center justify-center rounded-lg border p-8 text-center">
        <AlertCircle className="text-destructive h-10 w-10" />
        <h3 className="mt-4 text-lg font-semibold">Error loading projects</h3>
        <p className="text-muted-foreground mt-2 text-sm">
          Something went wrong while loading your projects.
        </p>
        <Button variant="outline" className="mt-4" onClick={() => refetch()}>
          <RefreshCw className="mr-2 h-4 w-4" />
          Retry
        </Button>
      </div>
    )
  }

  if (!projects || projects.length === 0) {
    return <ProjectsEmptyState />
  }

  return (
    <div className="grid gap-4 md:grid-cols-2 lg:grid-cols-3">
      {projects.map((project) => (
        <ProjectCard key={project.id} project={project} onDelete={handleDelete} />
      ))}
    </div>
  )
}
