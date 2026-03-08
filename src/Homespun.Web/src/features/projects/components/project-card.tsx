import { Link } from '@tanstack/react-router'
import { FolderGit2, Github, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { Card, CardHeader, CardTitle, CardDescription, CardAction } from '@/components/ui/card'
import { Button } from '@/components/ui/button'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from '@/components/ui/alert-dialog'
import type { Project } from '@/api/generated/types.gen'

interface ProjectCardProps {
  project: Project
  onDelete: (projectId: string) => void
}

function formatRelativeTime(dateString: string): string {
  const date = new Date(dateString)
  const now = new Date()
  const diffMs = now.getTime() - date.getTime()
  const diffSecs = Math.floor(diffMs / 1000)
  const diffMins = Math.floor(diffSecs / 60)
  const diffHours = Math.floor(diffMins / 60)
  const diffDays = Math.floor(diffHours / 24)

  if (diffDays > 0) {
    return `${diffDays} day${diffDays > 1 ? 's' : ''} ago`
  }
  if (diffHours > 0) {
    return `${diffHours} hour${diffHours > 1 ? 's' : ''} ago`
  }
  if (diffMins > 0) {
    return `${diffMins} minute${diffMins > 1 ? 's' : ''} ago`
  }
  return 'just now'
}

export function ProjectCard({ project, onDelete }: ProjectCardProps) {
  const [isDeleteDialogOpen, setIsDeleteDialogOpen] = useState(false)

  const handleDelete = () => {
    if (project.id) {
      onDelete(project.id)
    }
    setIsDeleteDialogOpen(false)
  }

  return (
    <Card className="hover:bg-muted/50 transition-colors">
      <CardHeader>
        <Link
          to="/projects/$projectId"
          params={{ projectId: project.id ?? '' }}
          className="hover:underline"
          data-testid="project-card-link"
        >
          <CardTitle className="flex items-center gap-2">
            <FolderGit2 className="h-5 w-5" />
            {project.name}
          </CardTitle>
        </Link>
        <CardDescription className="space-y-1">
          <span className="block font-mono text-xs">{project.localPath}</span>
          {project.gitHubOwner && project.gitHubRepo && (
            <span className="flex items-center gap-1.5 text-xs">
              <Github className="h-3.5 w-3.5" />
              {project.gitHubOwner}/{project.gitHubRepo}
            </span>
          )}
          {project.updatedAt && (
            <span className="text-muted-foreground/70 block text-xs">
              Updated {formatRelativeTime(project.updatedAt)}
            </span>
          )}
        </CardDescription>
        <CardAction>
          <AlertDialog open={isDeleteDialogOpen} onOpenChange={setIsDeleteDialogOpen}>
            <AlertDialogTrigger asChild>
              <Button variant="ghost" size="icon" aria-label="Delete project">
                <Trash2 className="h-4 w-4" />
              </Button>
            </AlertDialogTrigger>
            <AlertDialogContent>
              <AlertDialogHeader>
                <AlertDialogTitle>Delete Project</AlertDialogTitle>
                <AlertDialogDescription>
                  Are you sure you want to delete "{project.name}"? This action cannot be undone.
                </AlertDialogDescription>
              </AlertDialogHeader>
              <AlertDialogFooter>
                <AlertDialogCancel>Cancel</AlertDialogCancel>
                <AlertDialogAction variant="destructive" onClick={handleDelete}>
                  Delete
                </AlertDialogAction>
              </AlertDialogFooter>
            </AlertDialogContent>
          </AlertDialog>
        </CardAction>
      </CardHeader>
    </Card>
  )
}
