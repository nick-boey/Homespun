import { Link } from '@tanstack/react-router'
import { useProjects, useDeleteProject } from '../hooks/use-projects'
import { ProjectCard } from './project-card'
import { ProjectCardSkeleton } from './project-card-skeleton'
import { Button } from '@/components/ui/button'
import { AlertCircle, FolderPlus } from 'lucide-react'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog'
import { useState } from 'react'

export function ProjectsList() {
  const { data: projects, isLoading, isError, refetch } = useProjects()
  const deleteProject = useDeleteProject()
  const [projectToDelete, setProjectToDelete] = useState<string | null>(null)

  const handleDelete = (projectId: string) => {
    setProjectToDelete(projectId)
  }

  const confirmDelete = () => {
    if (projectToDelete) {
      deleteProject.mutate(projectToDelete)
      setProjectToDelete(null)
    }
  }

  if (isLoading) {
    return (
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {[1, 2, 3].map((i) => (
          <ProjectCardSkeleton key={i} />
        ))}
      </div>
    )
  }

  if (isError) {
    return (
      <div className="border-border rounded-lg border p-8 text-center">
        <AlertCircle className="text-destructive mx-auto mb-4 h-12 w-12" />
        <p className="text-muted-foreground mb-4">
          Failed to load projects. Please try again.
        </p>
        <Button onClick={() => refetch()} variant="outline">
          Try Again
        </Button>
      </div>
    )
  }

  if (!projects || projects.length === 0) {
    return (
      <div className="border-border rounded-lg border p-8 text-center">
        <FolderPlus className="text-muted-foreground mx-auto mb-4 h-12 w-12" />
        <p className="text-muted-foreground mb-4">
          No projects yet. Create your first project to get started.
        </p>
        <Button asChild>
          <Link to="/projects/new">Create Your First Project</Link>
        </Button>
      </div>
    )
  }

  return (
    <>
      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {projects.map((project) => (
          <ProjectCard
            key={project.id}
            project={project}
            onDelete={handleDelete}
          />
        ))}
      </div>

      <AlertDialog
        open={projectToDelete !== null}
        onOpenChange={(open) => !open && setProjectToDelete(null)}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Delete Project</AlertDialogTitle>
            <AlertDialogDescription>
              Are you sure you want to delete this project? This action cannot be
              undone.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel>Cancel</AlertDialogCancel>
            <AlertDialogAction
              onClick={confirmDelete}
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            >
              Delete
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  )
}
