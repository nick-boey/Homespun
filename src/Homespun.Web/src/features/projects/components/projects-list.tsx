import { useState } from 'react'
import { useProjects, useDeleteProject } from '../hooks/use-projects'
import { ProjectCard } from './project-card'
import { ProjectCardSkeleton } from './project-card-skeleton'
import { ProjectsEmptyState } from './projects-empty-state'
import { ErrorFallback } from '@/components/error-boundary'

export function ProjectsList() {
  const { data: projects, isLoading, isError, error, refetch, isFetching } = useProjects()
  const deleteProject = useDeleteProject()
  const [isRetrying, setIsRetrying] = useState(false)

  const handleDelete = (projectId: string) => {
    deleteProject.mutate(projectId)
  }

  const handleRetry = async () => {
    setIsRetrying(true)
    try {
      await refetch()
    } finally {
      setIsRetrying(false)
    }
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
      <ErrorFallback
        error={error}
        title="Error loading projects"
        description="Something went wrong while loading your projects."
        variant="inline"
        onRetry={handleRetry}
        isRetrying={isRetrying || isFetching}
      />
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
